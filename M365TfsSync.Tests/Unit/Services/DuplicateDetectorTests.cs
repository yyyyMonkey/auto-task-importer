using M365TfsSync.Models;
using M365TfsSync.Services;
using M365TfsSync.Services.Interfaces;
using Xunit;

namespace M365TfsSync.Tests.Unit.Services;

public class DuplicateDetectorTests
{
    private readonly DuplicateDetector _detector = new();

    [Fact]
    public void CheckDuplicate_EmptyTaskList_ReturnsNotDuplicate()
    {
        var evt = new CalendarEvent { Subject = "週會" };
        var result = _detector.CheckDuplicate(evt, Array.Empty<TfsTask>());
        Assert.False(result.IsDuplicate);
    }

    [Fact]
    public void CheckDuplicate_IdenticalTitle_ReturnsDuplicate()
    {
        var evt = new CalendarEvent { Subject = "週會" };
        var tasks = new[] { new TfsTask { Title = "週會" } };
        var result = _detector.CheckDuplicate(evt, tasks);
        Assert.True(result.IsDuplicate);
        Assert.Equal(100.0, result.SimilarityScore, 1);
    }

    [Fact]
    public void CheckDuplicate_BelowThreshold_ReturnsNotDuplicate()
    {
        var evt = new CalendarEvent { Subject = "ABCDE" };
        var tasks = new[] { new TfsTask { Title = "ZZZZZ" } };
        var result = _detector.CheckDuplicate(evt, tasks, 80.0);
        Assert.False(result.IsDuplicate);
    }

    [Fact]
    public void CheckAllDuplicates_EmptyEvents_ReturnsEmpty()
    {
        var results = _detector.CheckAllDuplicates(Array.Empty<CalendarEvent>(), Array.Empty<TfsTask>());
        Assert.Empty(results);
    }
}
