using System.Windows;
using M365TfsSync.Services;
using M365TfsSync.Services.Interfaces;
using M365TfsSync.ViewModels;
using M365TfsSync.Views;
using Microsoft.Extensions.DependencyInjection;

namespace M365TfsSync;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // 設定服務（先載入設定，供其他服務使用）
        services.AddSingleton<SettingsService>();

        // 身分驗證服務
        services.AddSingleton<IAuthService, AuthService>();

        // Graph API 客戶端（clientId/tenantId 從設定動態讀取，初始留空）
        services.AddSingleton<IGraphClient>(provider =>
        {
            var settingsService = provider.GetRequiredService<SettingsService>();
            var settings = settingsService.Load();
            return new GraphClient(
                clientId: settings.AzureClientId ?? string.Empty,
                tenantId: settings.AzureTenantId ?? "organizations");
        });

        // TFS 客戶端（serverUrl/projectName 從設定動態讀取）
        services.AddSingleton<ITfsClient>(provider =>
        {
            var settingsService = provider.GetRequiredService<SettingsService>();
            var settings = settingsService.Load();
            return new TfsClient(
                serverUrl: settings.TfsServerUrl ?? string.Empty,
                projectName: settings.TfsProjectName ?? string.Empty);
        });

        // 重複偵測服務
        services.AddSingleton<IDuplicateDetector, DuplicateDetector>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<SettingsViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
