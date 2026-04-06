namespace M365TfsSync.Models;

public class TfsTask
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
    public string IterationPath { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}
