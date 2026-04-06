namespace M365TfsSync.Models;

public class Sprint
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string IterationPath { get; set; } = string.Empty;
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }

    /// <summary>顯示最後兩層路徑，例如 "2026 Q1\(0323~0403)"</summary>
    public string DisplayName
    {
        get
        {
            var parts = IterationPath.Split('\\');
            return parts.Length >= 2
                ? string.Join("\\", parts[^2], parts[^1])
                : Name;
        }
    }

    public override string ToString() => DisplayName;
}
