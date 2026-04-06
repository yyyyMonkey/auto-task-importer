using M365TfsSync.Models;
using M365TfsSync.Services.Interfaces;

namespace M365TfsSync.Services;

public class DuplicateDetector : IDuplicateDetector
{
    public DuplicateCheckResult CheckDuplicate(
        CalendarEvent calendarEvent,
        IReadOnlyList<TfsTask> existingTasks,
        double similarityThreshold = 80.0)
    {
        if (calendarEvent == null)
            throw new ArgumentNullException(nameof(calendarEvent));

        if (existingTasks == null || existingTasks.Count == 0)
        {
            return new DuplicateCheckResult
            {
                Event = calendarEvent,
                IsDuplicate = false,
                MostSimilarTask = null,
                SimilarityScore = 0.0
            };
        }

        TfsTask? mostSimilarTask = null;
        double highestScore = 0.0;

        foreach (var task in existingTasks)
        {
            var score = LevenshteinDistance.ComputeSimilarity(calendarEvent.Subject, task.Title);
            if (score > highestScore)
            {
                highestScore = score;
                mostSimilarTask = task;
            }
        }

        return new DuplicateCheckResult
        {
            Event = calendarEvent,
            IsDuplicate = highestScore >= similarityThreshold,
            MostSimilarTask = highestScore >= similarityThreshold ? mostSimilarTask : null,
            SimilarityScore = highestScore
        };
    }

    public IReadOnlyList<DuplicateCheckResult> CheckAllDuplicates(
        IReadOnlyList<CalendarEvent> calendarEvents,
        IReadOnlyList<TfsTask> existingTasks,
        double similarityThreshold = 80.0)
    {
        if (calendarEvents == null || calendarEvents.Count == 0)
            return Array.Empty<DuplicateCheckResult>();

        return calendarEvents
            .Select(e => CheckDuplicate(e, existingTasks ?? Array.Empty<TfsTask>(), similarityThreshold))
            .ToList();
    }
}
