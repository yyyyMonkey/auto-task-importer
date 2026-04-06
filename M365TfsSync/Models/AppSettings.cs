namespace M365TfsSync.Models;

public class AppSettings
{
    public string TfsServerUrl { get; set; } = string.Empty;
    public string TfsProjectName { get; set; } = string.Empty;
    public string AdDomain { get; set; } = string.Empty;
    public double SimilarityThreshold { get; set; } = 80.0;

    // Graph API 設定（選用，使用 MSAL 整合驗證時需要）
    public string AzureClientId { get; set; } = string.Empty;
    public string AzureTenantId { get; set; } = "organizations";
}
