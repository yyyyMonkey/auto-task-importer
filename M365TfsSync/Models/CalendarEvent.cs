namespace M365TfsSync.Models;

public class CalendarEvent
{
    public string Id { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
}
