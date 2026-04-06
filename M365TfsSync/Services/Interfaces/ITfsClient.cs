using System.Net;
using M365TfsSync.Models;

namespace M365TfsSync.Services.Interfaces;

public interface ITfsClient
{
    Task<IReadOnlyList<TfsTeam>> GetTeamsAsync(
        NetworkCredential credential,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Sprint>> GetSprintsAsync(
        NetworkCredential credential,
        CancellationToken cancellationToken = default,
        string? teamName = null);

    Task<IReadOnlyList<TfsArea>> GetAreasAsync(
        NetworkCredential credential,
        string? teamName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TfsTask>> GetTasksBySprintAsync(
        string iterationPath,
        NetworkCredential credential,
        CancellationToken cancellationToken = default);

    Task<TfsTask> CreateTaskAsync(
        string title,
        string iterationPath,
        string assignedTo,
        NetworkCredential credential,
        string? areaPath = null,
        double durationHours = 0,
        bool isPastSprint = false,
        CancellationToken cancellationToken = default);

    Task<bool> TestConnectionAsync(
        string serverUrl,
        NetworkCredential credential,
        CancellationToken cancellationToken = default);
}
