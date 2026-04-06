using M365TfsSync.Models;

namespace M365TfsSync.Services.Interfaces;

public interface IDuplicateDetector
{
    DuplicateCheckResult CheckDuplicate(
        CalendarEvent calendarEvent,
        IReadOnlyList<TfsTask> existingTasks,
        double similarityThreshold = 80.0);

    IReadOnlyList<DuplicateCheckResult> CheckAllDuplicates(
        IReadOnlyList<CalendarEvent> calendarEvents,
        IReadOnlyList<TfsTask> existingTasks,
        double similarityThreshold = 80.0);
}

public class DuplicateCheckResult
{
    public CalendarEvent Event { get; set; } = null!;
    public bool IsDuplicate { get; set; }
    public TfsTask? MostSimilarTask { get; set; }
    public double SimilarityScore { get; set; }
}
