using System.Net;
using M365TfsSync.Models;
using M365TfsSync.Services;
using M365TfsSync.Services.Interfaces;

namespace M365TfsSync.ViewModels;

public class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly ITfsClient _tfsClient;

    private string _tfsServerUrl = string.Empty;
    private string _tfsProjectName = string.Empty;
    private string _adDomain = string.Empty;
    private string _statusMessage = string.Empty;
    private bool _isTestingConnection;

    public SettingsViewModel(SettingsService settingsService, ITfsClient tfsClient)
    {
        _settingsService = settingsService;
        _tfsClient = tfsClient;

        SaveCommand = new RelayCommand(ExecuteSave, CanSave);
        TestConnectionCommand = new RelayCommand(async () => await ExecuteTestConnectionAsync(), () => !_isTestingConnection && !string.IsNullOrWhiteSpace(_tfsServerUrl));
        CancelCommand = new RelayCommand(ExecuteCancel);

        // 載入現有設定
        var settings = _settingsService.Load();
        TfsServerUrl = settings.TfsServerUrl;
        TfsProjectName = settings.TfsProjectName;
        AdDomain = settings.AdDomain;
    }

    public string TfsServerUrl
    {
        get => _tfsServerUrl;
        set
        {
            SetProperty(ref _tfsServerUrl, value);
            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (TestConnectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string TfsProjectName
    {
        get => _tfsProjectName;
        set
        {
            SetProperty(ref _tfsProjectName, value);
            (SaveCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    public string AdDomain
    {
        get => _adDomain;
        set => SetProperty(ref _adDomain, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public RelayCommand SaveCommand { get; }
    public RelayCommand TestConnectionCommand { get; }
    public RelayCommand CancelCommand { get; }

    // 視窗關閉事件（由 View 訂閱）
    public event Action? RequestClose;

    private bool CanSave() =>
        !string.IsNullOrWhiteSpace(_tfsServerUrl) &&
        !string.IsNullOrWhiteSpace(_tfsProjectName);

    private void ExecuteSave()
    {
        _settingsService.Save(new AppSettings
        {
            TfsServerUrl = _tfsServerUrl,
            TfsProjectName = _tfsProjectName,
            AdDomain = _adDomain
        });
        RequestClose?.Invoke();
    }

    private async Task ExecuteTestConnectionAsync()
    {
        _isTestingConnection = true;
        (TestConnectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        StatusMessage = "正在測試連線...";

        try
        {
            // 使用 Windows 整合驗證測試連線
            var credential = new NetworkCredential(
                Environment.UserName, (string?)null, Environment.UserDomainName);
            var success = await _tfsClient.TestConnectionAsync(_tfsServerUrl, credential);
            StatusMessage = success ? "✓ 連線成功" : "✗ 連線失敗，請確認 TFS 伺服器 URL 是否正確";
        }
        catch (Exception ex)
        {
            StatusMessage = $"✗ 連線失敗：{ex.Message}";
        }
        finally
        {
            _isTestingConnection = false;
            (TestConnectionCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }

    private void ExecuteCancel()
    {
        RequestClose?.Invoke();
    }
}
