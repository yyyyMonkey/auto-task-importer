using System.Net;
using M365TfsSync.Models;

namespace M365TfsSync.Services.Interfaces;

public interface IGraphClient
{
    Task<IReadOnlyList<CalendarEvent>> GetCalendarEventsAsync(
        DateTime startDate,
        DateTime endDate,
        NetworkCredential credential,
        CancellationToken cancellationToken = default);
}
