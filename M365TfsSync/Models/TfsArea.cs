namespace M365TfsSync.Models;

/// <summary>TFS Area 節點</summary>
public class TfsArea
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>完整 Area Path，例如 MyProject\Team\SubArea</summary>
    public string AreaPath { get; set; } = string.Empty;
    /// <summary>顯示用名稱（最後兩層）</summary>
    public string DisplayName => GetDisplayName();

    private string GetDisplayName()
    {
        var parts = AreaPath.Split('\\');
        return parts.Length >= 2
            ? string.Join("\\", parts[^2..])
            : AreaPath;
    }
}
