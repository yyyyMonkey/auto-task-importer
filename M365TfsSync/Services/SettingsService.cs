using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using M365TfsSync.Models;

namespace M365TfsSync.Services;

public class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "M365TfsSync");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.dat");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFile))
                return new AppSettings();

            var base64 = File.ReadAllText(SettingsFile);
            var encrypted = Convert.FromBase64String(base64);
            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decrypted);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (Exception)
        {
            // 解密失敗或檔案損毀時回傳預設值
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonSerializer.Serialize(settings);
            var data = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
            File.WriteAllText(SettingsFile, Convert.ToBase64String(encrypted));
        }
        catch (Exception ex)
        {
            throw new SettingsException($"儲存設定失敗：{ex.Message}", ex);
        }
    }
}
