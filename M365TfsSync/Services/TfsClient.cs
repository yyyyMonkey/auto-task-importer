using System.Net;
using M365TfsSync.Models;
using M365TfsSync.Services.Interfaces;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.Core.WebApi.Types;
using Microsoft.TeamFoundation.Work.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.VisualStudio.Services.WebApi.Patch;
using Microsoft.VisualStudio.Services.WebApi.Patch.Json;

namespace M365TfsSync.Services;

public class TfsClient : ITfsClient
{
    private readonly string _serverUrl;
    private readonly string _projectName;

    public TfsClient(string serverUrl, string projectName)
    {
        _serverUrl = serverUrl;
        _projectName = projectName;
    }

    public async Task<IReadOnlyList<TfsTeam>> GetTeamsAsync(
        NetworkCredential credential,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = CreateConnection(credential);
            var client = await connection.GetClientAsync<TeamHttpClient>(cancellationToken);
            var teams = await client.GetTeamsAsync(_projectName, mine: true, cancellationToken: cancellationToken);
            return teams.Select(t => new TfsTeam { Id = t.Id.ToString(), Name = t.Name }).ToList();
        }
        catch (Exception ex) when (ex is not TfsApiException)
        {
            throw new TfsApiException($"取得 Team 清單失敗：{ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<Sprint>> GetSprintsAsync(
        NetworkCredential credential,
        CancellationToken cancellationToken = default,
        string? teamName = null)
    {
        try
        {
            using var connection = CreateConnection(credential);

            if (!string.IsNullOrWhiteSpace(teamName))
            {
                // 使用 Work ItemTracking Team Settings 取得該 Team 實際設定的 iterations
                var workClient = await connection.GetClientAsync<WorkHttpClient>(cancellationToken);
                var teamContext = new TeamContext(_projectName, teamName);
                var iterations = await workClient.GetTeamIterationsAsync(teamContext, cancellationToken: cancellationToken);
                return iterations.Select(i => new Sprint
                {
                    Id = i.Id.ToString(),
                    Name = i.Name,
                    IterationPath = i.Path.TrimStart('\\'),
                    StartDate = i.Attributes?.StartDate,
                    EndDate = i.Attributes?.FinishDate
                }).ToList();
            }

            // 未指定 Team 時回傳全部 iterations
            var witClient = await connection.GetClientAsync<WorkItemTrackingHttpClient>(cancellationToken);
            var rootNode = await witClient.GetClassificationNodeAsync(
                _projectName, TreeStructureGroup.Iterations, depth: 5,
                cancellationToken: cancellationToken);
            var sprints = new List<Sprint>();
            FlattenIterations(rootNode, sprints, _projectName);
            return sprints;
        }
        catch (Exception ex) when (ex is not TfsApiException)
        {
            throw new TfsApiException($"取得 Sprint 清單失敗：{ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<TfsArea>> GetAreasAsync(
        NetworkCredential credential,
        string? teamName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = CreateConnection(credential);
            var witClient = await connection.GetClientAsync<WorkItemTrackingHttpClient>(cancellationToken);
            var rootNode = await witClient.GetClassificationNodeAsync(
                _projectName, TreeStructureGroup.Areas, depth: 5,
                cancellationToken: cancellationToken);

            var areas = new List<TfsArea>();
            FlattenAreas(rootNode, areas, _projectName);

            // 若指定 teamName，只保留 areaPath 第二層等於 teamName 的節點
            // 例如 teamName="TeamA" 時，保留 "Mgt-Con...\TeamA\..." 的 Area
            if (!string.IsNullOrWhiteSpace(teamName))
            {
                areas.RemoveAll(a =>
                {
                    var parts = a.AreaPath.Split('\\');
                    // parts[0] = projectName, parts[1] = 第一層 area（對應 team）
                    return parts.Length < 2 ||
                           !string.Equals(parts[1], teamName, StringComparison.OrdinalIgnoreCase);
                });
            }

            return areas;
        }
        catch (Exception ex) when (ex is not TfsApiException)
        {
            throw new TfsApiException($"取得 Area 清單失敗：{ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyList<TfsTask>> GetTasksBySprintAsync(
        string iterationPath,
        NetworkCredential credential,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = CreateConnection(credential);
            var client = await connection.GetClientAsync<WorkItemTrackingHttpClient>(cancellationToken);

            // 移除 @Me 過濾，改為取得該 Sprint 所有 Task，避免 AssignedTo 格式不符導致查詢空結果
            var wiql = new Wiql
            {
                Query = $@"SELECT [System.Id], [System.Title], [System.AssignedTo], [System.IterationPath], [System.State]
                           FROM WorkItems
                           WHERE [System.TeamProject] = '{_projectName}'
                             AND [System.IterationPath] = '{iterationPath}'
                             AND [System.WorkItemType] = 'Task'
                             AND [System.State] <> 'Removed'"
            };

            var result = await client.QueryByWiqlAsync(wiql, cancellationToken: cancellationToken);
            if (!result.WorkItems.Any())
                return Array.Empty<TfsTask>();

            var ids = result.WorkItems.Select(w => w.Id).ToArray();
            var fields = new[] { "System.Id", "System.Title", "System.AssignedTo", "System.IterationPath", "System.State" };
            var workItems = await client.GetWorkItemsAsync(ids, fields, cancellationToken: cancellationToken);

            return workItems.Select(wi => new TfsTask
            {
                Id = wi.Id ?? 0,
                Title = wi.Fields.TryGetValue("System.Title", out var t) ? t?.ToString() ?? "" : "",
                AssignedTo = wi.Fields.TryGetValue("System.AssignedTo", out var a) ? a?.ToString() ?? "" : "",
                IterationPath = wi.Fields.TryGetValue("System.IterationPath", out var ip) ? ip?.ToString() ?? "" : "",
                State = wi.Fields.TryGetValue("System.State", out var s) ? s?.ToString() ?? "" : ""
            }).ToList();
        }
        catch (Exception ex) when (ex is not TfsApiException)
        {
            throw new TfsApiException($"取得 Sprint 任務失敗：{ex.Message}", ex);
        }
    }

    public async Task<TfsTask> CreateTaskAsync(
        string title,
        string iterationPath,
        string assignedTo,
        NetworkCredential credential,
        string? areaPath = null,
        double durationHours = 0,
        bool isPastSprint = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = CreateConnection(credential);
            var client = await connection.GetClientAsync<WorkItemTrackingHttpClient>(cancellationToken);

            var patchDocument = new JsonPatchDocument
            {
                new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.Title",
                    Value = title
                },
                new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.IterationPath",
                    Value = iterationPath
                },
                new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.AssignedTo",
                    Value = assignedTo
                }
            };

            // 若有指定 Area，加入 AreaPath
            if (!string.IsNullOrWhiteSpace(areaPath))
            {
                patchDocument.Add(new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/System.AreaPath",
                    Value = areaPath
                });
            }

            if (durationHours > 0)
            {
                // Original Estimate 永遠填入
                patchDocument.Add(new JsonPatchOperation
                {
                    Operation = Operation.Add,
                    Path = "/fields/Microsoft.VSTS.Scheduling.OriginalEstimate",
                    Value = durationHours
                });

                if (isPastSprint)
                {
                    // 過去的 Sprint：Remaining 留空，Completed 填入時數
                    // State 改為 Closed 會在建立後另外 update
                    patchDocument.Add(new JsonPatchOperation
                    {
                        Operation = Operation.Add,
                        Path = "/fields/Microsoft.VSTS.Scheduling.CompletedWork",
                        Value = durationHours
                    });
                }
                else
                {
                    // 當前或未來 Sprint：Remaining 填入時數
                    patchDocument.Add(new JsonPatchOperation
                    {
                        Operation = Operation.Add,
                        Path = "/fields/Microsoft.VSTS.Scheduling.RemainingWork",
                        Value = durationHours
                    });
                }
            }

            var workItem = await client.CreateWorkItemAsync(
                patchDocument, _projectName, "Task",
                cancellationToken: cancellationToken);

            // 過去的 Sprint：建立後再 update State 為 Closed
            // （TFS workflow 不允許建立時直接設為 Closed）
            if (isPastSprint && workItem.Id.HasValue)
            {
                var updateDoc = new JsonPatchDocument
                {
                    new JsonPatchOperation
                    {
                        Operation = Operation.Add,
                        Path = "/fields/System.State",
                        Value = "Closed"
                    }
                };
                workItem = await client.UpdateWorkItemAsync(
                    updateDoc, workItem.Id.Value,
                    cancellationToken: cancellationToken);
            }

            return new TfsTask
            {
                Id = workItem.Id ?? 0,
                Title = workItem.Fields.TryGetValue("System.Title", out var t) ? t?.ToString() ?? "" : "",
                AssignedTo = workItem.Fields.TryGetValue("System.AssignedTo", out var a) ? a?.ToString() ?? "" : "",
                IterationPath = workItem.Fields.TryGetValue("System.IterationPath", out var ip) ? ip?.ToString() ?? "" : "",
                State = workItem.Fields.TryGetValue("System.State", out var s) ? s?.ToString() ?? "" : ""
            };
        }
        catch (Exception ex) when (ex is not TfsApiException)
        {
            throw new TfsApiException($"建立任務失敗：{ex.Message}", ex);
        }
    }

    public async Task<bool> TestConnectionAsync(
        string serverUrl,
        NetworkCredential credential,
        CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrWhiteSpace(serverUrl) ? _serverUrl : serverUrl;
        if (string.IsNullOrWhiteSpace(url))
            throw new TfsApiException("TFS 伺服器 URL 不可為空。");

        var vssCredentials = string.IsNullOrEmpty(credential.Password)
            ? new VssCredentials(new WindowsCredential(true))
            : new VssCredentials(new WindowsCredential(credential));

        // 禁止 SDK 自動重試，避免帳號被鎖
        vssCredentials.PromptType = CredentialPromptType.DoNotPrompt;

        using var connection = new VssConnection(new Uri(url), vssCredentials);
        await connection.ConnectAsync(cancellationToken);
        return true;
    }

    private VssConnection CreateConnection(NetworkCredential credential)
    {
        var vssCredentials = string.IsNullOrEmpty(credential.Password)
            ? new VssCredentials(new WindowsCredential(true))
            : new VssCredentials(new WindowsCredential(credential));

        // 禁止 SDK 自動彈出驗證視窗或重試，避免帳號被鎖
        vssCredentials.PromptType = CredentialPromptType.DoNotPrompt;
        return new VssConnection(new Uri(_serverUrl), vssCredentials);
    }

    private static void FlattenIterations(WorkItemClassificationNode node, List<Sprint> sprints, string projectName)
    {
        if (node.StructureType == TreeNodeStructureType.Iteration && node.Children == null)
        {
            var rawPath = node.Path ?? string.Empty;
            var trimmed = rawPath.TrimStart('\\');
            var iterationPath = trimmed.StartsWith(projectName, StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"{projectName}\\{trimmed}";

            sprints.Add(new Sprint
            {
                Id = node.Identifier.ToString(),
                Name = node.Name,
                IterationPath = iterationPath,
                StartDate = node.Attributes?.TryGetValue("startDate", out var sd) == true ? sd as DateTime? : null,
                EndDate = node.Attributes?.TryGetValue("finishDate", out var fd) == true ? fd as DateTime? : null
            });
        }

        if (node.Children != null)
            foreach (var child in node.Children)
                FlattenIterations(child, sprints, projectName);
    }

    private static void FlattenAreas(WorkItemClassificationNode node, List<TfsArea> areas, string projectName)
    {
        var rawPath = node.Path ?? string.Empty;
        var trimmed = rawPath.TrimStart('\\');

        // TFS API 回傳的 Area Path 包含 "\Area\" 中間層，例如：
        //   "Mgt-Con...\Area\xx科\xx組"
        // 但寫入 Work Item 的 System.AreaPath 必須去掉 "\Area\"：
        //   "Mgt-Con...\xx科\xx組"
        var areaSegment = projectName + "\\Area\\";
        var areaPath = trimmed.StartsWith(areaSegment, StringComparison.OrdinalIgnoreCase)
            ? projectName + "\\" + trimmed.Substring(areaSegment.Length)
            : trimmed.StartsWith(projectName, StringComparison.OrdinalIgnoreCase)
                ? trimmed
                : $"{projectName}\\{trimmed}";

        // 排除根節點（路徑等於 projectName）以及 Area 虛擬根節點（路徑等於 projectName\Area）
        var isRoot = string.Equals(areaPath, projectName, StringComparison.OrdinalIgnoreCase);
        var isAreaRoot = string.Equals(rawPath.TrimStart('\\'),
            projectName + "\\Area", StringComparison.OrdinalIgnoreCase);

        if (!isRoot && !isAreaRoot)
        {
            areas.Add(new TfsArea
            {
                Id = node.Identifier.ToString(),
                Name = node.Name,
                AreaPath = areaPath
            });
        }

        if (node.Children != null)
            foreach (var child in node.Children)
                FlattenAreas(child, areas, projectName);
    }
}
