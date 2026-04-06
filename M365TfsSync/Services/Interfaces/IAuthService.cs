using System.Net;
using M365TfsSync.Models;

namespace M365TfsSync.Services.Interfaces;

public interface IAuthService
{
    bool IsAuthenticated { get; }
    string? CurrentUsername { get; }
    NetworkCredential? CurrentCredential { get; }

    Task<AuthResult> LoginWithWindowsCredentialsAsync();
    Task<AuthResult> LoginWithCredentialsAsync(string username, string password, string domain);
    void Logout();
}

public class AuthResult
{
    public bool Success { get; set; }
    public string? Username { get; set; }
    public string? ErrorMessage { get; set; }

    public static AuthResult Ok(string username) => new() { Success = true, Username = username };
    public static AuthResult Fail(string error) => new() { Success = false, ErrorMessage = error };
}
