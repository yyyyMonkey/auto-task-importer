using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using M365TfsSync.Models;
using M365TfsSync.Services.Interfaces;
using Microsoft.Identity.Client;

namespace M365TfsSync.Services;

public class GraphClient : IGraphClient
{
    private readonly string _clientId;
    private readonly string _tenantId;
    private static readonly string[] Scopes = ["Calendars.Read"];
    private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";

    public GraphClient(string clientId = "", string tenantId = "organizations")
    {
        _clientId = clientId;
        _tenantId = tenantId;
    }

    public async Task<IReadOnlyList<CalendarEvent>> GetCalendarEventsAsync(
        DateTime startDate,
        DateTime endDate,
        NetworkCredential credential,
        CancellationToken cancellationToken = default)
    {
        var token = await AcquireTokenAsync(credential, cancellationToken);
        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var startStr = startDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
        var endStr = endDate.ToUniversalTime().AddDays(1).AddSeconds(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        var url = $"{GraphBaseUrl}/me/calendarView?startDateTime={startStr}&endDateTime={endStr}&$select=id,subject,start,end&$top=100";

        var response = await httpClient.GetAsync(url, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new GraphApiException(response.StatusCode, errorBody);
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseCalendarEvents(json);
    }

    private async Task<string> AcquireTokenAsync(NetworkCredential credential, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_clientId))
                throw new GraphApiException(HttpStatusCode.Unauthorized,
                    "尚未設定 Azure AD Client ID，請至設定頁面完成設定。");

            var app = PublicClientApplicationBuilder
                .Create(_clientId)
                .WithAuthority(AzureCloudInstance.AzurePublic, _tenantId)
                .Build();

            AuthenticationResult result;

            if (string.IsNullOrWhiteSpace(credential.Password))
            {
                // Windows 整合驗證
                result = await app.AcquireTokenByIntegratedWindowsAuth(Scopes)
                    .WithUsername(credential.UserName)
                    .ExecuteAsync(cancellationToken);
            }
            else
            {
                // 帳號密碼驗證
                var securePassword = new System.Security.SecureString();
                foreach (var c in credential.Password)
                    securePassword.AppendChar(c);

                result = await app.AcquireTokenByUsernamePassword(Scopes, credential.UserName, securePassword)
                    .ExecuteAsync(cancellationToken);
            }

            return result.AccessToken;
        }
        catch (MsalException ex)
        {
            throw new GraphApiException(HttpStatusCode.Unauthorized, $"取得 Graph API Token 失敗：{ex.Message}");
        }
    }

    private static List<CalendarEvent> ParseCalendarEvents(string json)
    {
        var events = new List<CalendarEvent>();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("value", out var valueArray))
            return events;

        foreach (var item in valueArray.EnumerateArray())
        {
            var id = item.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
            var subject = item.TryGetProperty("subject", out var subjectProp) ? subjectProp.GetString() ?? "(無主旨)" : "(無主旨)";

            DateTime startTime = DateTime.Now;
            DateTime endTime = DateTime.Now;

            if (item.TryGetProperty("start", out var startProp) &&
                startProp.TryGetProperty("dateTime", out var startDt))
                DateTime.TryParse(startDt.GetString(), out startTime);

            if (item.TryGetProperty("end", out var endProp) &&
                endProp.TryGetProperty("dateTime", out var endDt))
                DateTime.TryParse(endDt.GetString(), out endTime);

            events.Add(new CalendarEvent
            {
                Id = id,
                Subject = subject,
                StartTime = startTime,
                EndTime = endTime
            });
        }

        return events;
    }
}
