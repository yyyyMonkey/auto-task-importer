using System.DirectoryServices.AccountManagement;
using System.Net;
using M365TfsSync.Services.Interfaces;

namespace M365TfsSync.Services;

public class AuthService : IAuthService
{
    private NetworkCredential? _credential;

    public bool IsAuthenticated => _credential != null;
    public string? CurrentUsername => _credential?.UserName;
    public NetworkCredential? CurrentCredential => _credential;

    public async Task<AuthResult> LoginWithWindowsCredentialsAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var credential = CredentialCache.DefaultNetworkCredentials as NetworkCredential;
                var username = credential?.UserName ?? Environment.UserName;

                if (string.IsNullOrWhiteSpace(username))
                    return AuthResult.Fail("無法取得目前 Windows 登入帳號。");

                // 使用 DefaultNetworkCredentials 作為憑證
                _credential = new NetworkCredential(
                    Environment.UserName,
                    (string?)null,
                    Environment.UserDomainName);

                return AuthResult.Ok($"{Environment.UserDomainName}\\{Environment.UserName}");
            }
            catch (Exception ex)
            {
                return AuthResult.Fail($"Windows 整合驗證失敗：{ex.Message}");
            }
        });
    }

    public async Task<AuthResult> LoginWithCredentialsAsync(string username, string password, string domain)
    {
        if (string.IsNullOrWhiteSpace(username))
            return AuthResult.Fail("帳號不可為空。");
        if (string.IsNullOrWhiteSpace(password))
            return AuthResult.Fail("密碼不可為空。");

        return await Task.Run(() =>
        {
            try
            {
                using var context = string.IsNullOrWhiteSpace(domain)
                    ? new PrincipalContext(ContextType.Machine)
                    : new PrincipalContext(ContextType.Domain, domain);

                bool isValid = context.ValidateCredentials(username, password);

                if (!isValid)
                    return AuthResult.Fail("帳號或密碼不正確，請重新輸入。");

                _credential = new NetworkCredential(username, password, domain);
                var displayName = string.IsNullOrWhiteSpace(domain) ? username : $"{domain}\\{username}";
                return AuthResult.Ok(displayName);
            }
            catch (PrincipalServerDownException ex)
            {
                return AuthResult.Fail($"無法連線至 AD 網域伺服器：{ex.Message}");
            }
            catch (Exception ex)
            {
                return AuthResult.Fail($"驗證失敗：{ex.Message}");
            }
        });
    }

    public void Logout()
    {
        _credential = null;
    }
}
