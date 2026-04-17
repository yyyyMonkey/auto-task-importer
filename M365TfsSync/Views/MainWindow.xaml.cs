using System.Windows;
using M365TfsSync.Services;
using M365TfsSync.Services.Interfaces;
using M365TfsSync.ViewModels;

namespace M365TfsSync.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly SettingsService _settingsService;
    private readonly ITfsClient _tfsClient;

    public MainWindow(MainViewModel viewModel, SettingsService settingsService, ITfsClient tfsClient)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsService = settingsService;
        _tfsClient = tfsClient;
        DataContext = viewModel;

        // 訂閱開啟設定視窗事件
        viewModel.RequestOpenSettings += OpenSettingsWindow;

        // 訂閱開啟 ICS 檔案對話框事件
        viewModel.RequestOpenIcsFile += OpenIcsFileDialog;

        // 訂閱開啟 CSV 檔案對話框事件
        viewModel.RequestOpenCsvFile += OpenCsvFileDialog;

        // PasswordBox 需要手動綁定（WPF 安全限制）
        if (PasswordBox != null)
        {
            PasswordBox.PasswordChanged += (s, e) =>
            {
                viewModel.Password = PasswordBox.Password;
            };
        }
    }

    private void OpenSettingsWindow()
    {
        var settingsVm = new SettingsViewModel(_settingsService, _tfsClient);
        var settingsWindow = new SettingsWindow(settingsVm) { Owner = this };
        settingsWindow.ShowDialog();
    }

    private void OpenIcsFileDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "選擇 ICS 行事曆檔案",
            Filter = "iCalendar 檔案 (*.ics)|*.ics|所有檔案 (*.*)|*.*",
            DefaultExt = ".ics"
        };

        if (dialog.ShowDialog() == true)
            _viewModel.LoadIcsFile(dialog.FileName);
    }

    private void OpenCsvFileDialog()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "選擇 CSV 行事曆檔案",
            Filter = "CSV 檔案 (*.csv)|*.csv|所有檔案 (*.*)|*.*",
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog() == true)
            _viewModel.LoadCsvFile(dialog.FileName);
    }
}
