using System.Collections.ObjectModel;
using System.Net;
using M365TfsSync.Models;
using M365TfsSync.Services;
using M365TfsSync.Services.Interfaces;

namespace M365TfsSync.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly IGraphClient _graphClient;
    private readonly ITfsClient _tfsClient;
    private readonly IDuplicateDetector _duplicateDetector;
    private readonly SettingsService _settingsService;

    private AuthMode _authMode = AuthMode.UsernamePassword;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private bool _isAuthenticated;
    private string _loggedInUser = string.Empty;
    private DateTime? _startDate = DateTime.Today;
    private DateTime? _endDate = DateTime.Today.AddDays(7);
    private Sprint? _selectedSprint;
    private bool _isLoading;
    private string _statusMessage = string.Empty;
    private IReadOnlyList<TfsTask> _currentSprintTasks = Array.Empty<TfsTask>();
    private AppSettings _settings = new();
    private TfsTeam? _selectedTeam;
    private TfsArea? _selectedArea;

    public MainViewModel(
        IAuthService authService,
        IGraphClient graphClient,
        ITfsClient tfsClient,
        IDuplicateDetector duplicateDetector,
        SettingsService settingsService)
    {
        _authService = authService;
        _graphClient = graphClient;
        _tfsClient = tfsClient;
        _duplicateDetector = duplicateDetector;
        _settingsService = settingsService;

        CalendarEvents = new ObservableCollection<CalendarEventViewModel>();
        Sprints = new ObservableCollection<Sprint>();
        Teams = new ObservableCollection<TfsTeam>();
        Areas = new ObservableCollection<TfsArea>();

        LoginCommand = new RelayCommand(async () => await ExecuteLoginAsync(), CanLogin);
        LogoutCommand = new RelayCommand(ExecuteLogout, () => _isAuthenticated);
        FetchEventsCommand = new RelayCommand(async () => await ExecuteFetchEventsAsync(), CanFetchEvents);
        LoadIcsCommand = new RelayCommand(ExecuteLoadIcs, () => _isAuthenticated);
        LoadCsvCommand = new RelayCommand(ExecuteLoadCsv, () => _isAuthenticated);
        SelectAllCommand = new RelayCommand(ExecuteSelectAll, () => _isAuthenticated && CalendarEvents.Any(e => e.IsSelectable));
        DeselectAllCommand = new RelayCommand(ExecuteDeselectAll, () => CalendarEvents.Any(e => e.IsSelected));
        ConfirmCommand = new RelayCommand(async () => await ExecuteConfirmAsync(), () => CanConfirm);
        OpenSettingsCommand = new RelayCommand(ExecuteOpenSettings);

        _settings = _settingsService.Load();
    }

    // ── 屬性 ──────────────────────────────────────────────────────────────

    public AuthMode AuthMode
    {
        get => _authMode;
        set
        {
            SetProperty(ref _authMode, value);
            OnPropertyChanged(nameof(IsUsernamePasswordMode));
            OnPropertyChanged(nameof(IsWindowsAuth));
            OnPropertyChanged(nameof(IsPasswordAuth));
            LoginCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsUsernamePasswordMode => _authMode == AuthMode.UsernamePassword;

    public bool IsWindowsAuth
    {
        get => _authMode == AuthMode.WindowsIntegrated;
        set { if (value) AuthMode = AuthMode.WindowsIntegrated; }
    }

    public bool IsPasswordAuth
    {
        get => _authMode == AuthMode.UsernamePassword;
        set { if (value) AuthMode = AuthMode.UsernamePassword; }
    }

    public string Username
    {
        get => _username;
        set
        {
            SetProperty(ref _username, value);
            LoginCommand.RaiseCanExecuteChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            SetProperty(ref _password, value);
            LoginCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set
        {
            SetProperty(ref _isAuthenticated, value);
            LogoutCommand.RaiseCanExecuteChanged();
            FetchEventsCommand.RaiseCanExecuteChanged();
            LoadIcsCommand.RaiseCanExecuteChanged();
            LoadCsvCommand.RaiseCanExecuteChanged();
            SelectAllCommand.RaiseCanExecuteChanged();
        }
    }

    public string LoggedInUser
    {
        get => _loggedInUser;
        private set => SetProperty(ref _loggedInUser, value);
    }

    public DateTime? StartDate
    {
        get => _startDate;
        set
        {
            SetProperty(ref _startDate, value);
            FetchEventsCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(DateValidationError));
        }
    }

    public DateTime? EndDate
    {
        get => _endDate;
        set
        {
            SetProperty(ref _endDate, value);
            FetchEventsCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(DateValidationError));
        }
    }

    public string? DateValidationError
    {
        get
        {
            if (_startDate.HasValue && _endDate.HasValue && _endDate < _startDate)
                return "結束日期不可早於開始日期";
            return null;
        }
    }

    public ObservableCollection<CalendarEventViewModel> CalendarEvents { get; }
    public ObservableCollection<Sprint> Sprints { get; }
    public ObservableCollection<TfsTeam> Teams { get; }
    public ObservableCollection<TfsArea> Areas { get; }

    public TfsArea? SelectedArea
    {
        get => _selectedArea;
        set => SetProperty(ref _selectedArea, value);
    }

    public TfsTeam? SelectedTeam
    {
        get => _selectedTeam;
        set
        {
            if (SetProperty(ref _selectedTeam, value))
            {
                Sprints.Clear();
                Areas.Clear();
                _selectedSprint = null;
                _selectedArea = null;
                OnPropertyChanged(nameof(SelectedSprint));
                OnPropertyChanged(nameof(SelectedArea));
                if (value != null)
                {
                    _ = LoadSprintsAsync(value.Name);
                    _ = LoadAreasAsync(value.Name);
                }
            }
        }
    }

    public Sprint? SelectedSprint
    {
        get => _selectedSprint;
        set
        {
            if (SetProperty(ref _selectedSprint, value))
            {
                // 自動帶入 Sprint 的起訖日期到行事曆查詢區間
                if (value?.StartDate != null)
                    StartDate = value.StartDate.Value.Date;
                if (value?.EndDate != null)
                    EndDate = value.EndDate.Value.Date;

                _ = LoadSprintTasksAndDetectDuplicatesAsync(value);
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            SetProperty(ref _isLoading, value);
            LoginCommand.RaiseCanExecuteChanged();
            FetchEventsCommand.RaiseCanExecuteChanged();
            ConfirmCommand.RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool CanConfirm =>
        CalendarEvents.Any(e => e.IsSelected) && _selectedSprint != null;

    // ── Commands ──────────────────────────────────────────────────────────

    public RelayCommand LoginCommand { get; }
    public RelayCommand LogoutCommand { get; }
    public RelayCommand FetchEventsCommand { get; }
    public RelayCommand LoadIcsCommand { get; }
    public RelayCommand LoadCsvCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand DeselectAllCommand { get; }
    public RelayCommand ConfirmCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }

    // 開啟設定視窗的事件（由 View 訂閱）
    public event Action? RequestOpenSettings;

    // ── Command 實作 ──────────────────────────────────────────────────────

    private bool CanLogin()
    {
        if (_isLoading) return false;
        if (string.IsNullOrWhiteSpace(_settings.TfsServerUrl) ||
            string.IsNullOrWhiteSpace(_settings.TfsProjectName))
            return false;
        if (_authMode == AuthMode.UsernamePassword &&
            string.IsNullOrWhiteSpace(_username))
            return false;
        return true;
    }

    private async Task ExecuteLoginAsync()
    {
        IsLoading = true;
        StatusMessage = "正在驗證身分...";

        try
        {
            _settings = _settingsService.Load();
            AuthResult result;

            if (_authMode == AuthMode.WindowsIntegrated)
                result = await _authService.LoginWithWindowsCredentialsAsync();
            else
                result = await _authService.LoginWithCredentialsAsync(_username, _password, _settings.AdDomain);

            if (result.Success)
            {
                IsAuthenticated = true;
                LoggedInUser = result.Username ?? string.Empty;
                StatusMessage = $"已登入：{LoggedInUser}";
                await LoadTeamsAsync();
            }
            else
            {
                StatusMessage = $"登入失敗：{result.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"登入失敗：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ExecuteLogout()
    {
        _authService.Logout();
        IsAuthenticated = false;
        LoggedInUser = string.Empty;
        CalendarEvents.Clear();
        Sprints.Clear();
        Teams.Clear();
        _selectedTeam = null;
        OnPropertyChanged(nameof(SelectedTeam));
        _selectedSprint = null;
        OnPropertyChanged(nameof(SelectedSprint));
        _currentSprintTasks = Array.Empty<TfsTask>();
        StatusMessage = "已登出";
        RaiseAllCommandsCanExecuteChanged();
    }

    private bool CanFetchEvents()
    {
        if (!_isAuthenticated || _isLoading) return false;
        if (!_startDate.HasValue || !_endDate.HasValue) return false;
        if (_endDate < _startDate) return false;
        return true;
    }

    private async Task ExecuteFetchEventsAsync()
    {
        IsLoading = true;
        StatusMessage = "正在取得會議清單...";

        try
        {
            var credential = _authService.CurrentCredential
                ?? new NetworkCredential(Environment.UserName, (string?)null, Environment.UserDomainName);

            var events = await _graphClient.GetCalendarEventsAsync(
                _startDate!.Value, _endDate!.Value, credential);

            CalendarEvents.Clear();

            if (!events.Any())
            {
                StatusMessage = "查詢區間內無會議";
                return;
            }

            foreach (var evt in events)
            {
                var vm = new CalendarEventViewModel(evt);
                vm.SelectionChanged += () =>
                {
                    ConfirmCommand.RaiseCanExecuteChanged();
                    DeselectAllCommand.RaiseCanExecuteChanged();
                };
                CalendarEvents.Add(vm);
            }

            // 若已選擇 Sprint，立即執行重複比對
            if (_selectedSprint != null)
                ApplyDuplicateDetection();

            StatusMessage = $"已取得 {events.Count} 筆會議";
        }
        catch (GraphApiException ex)
        {
            StatusMessage = $"取得會議失敗 [{(int)ex.StatusCode}]：{ex.ErrorDescription}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"取得會議失敗：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
            SelectAllCommand.RaiseCanExecuteChanged();
            DeselectAllCommand.RaiseCanExecuteChanged();
            ConfirmCommand.RaiseCanExecuteChanged();
        }
    }

    private void ExecuteLoadIcs()
    {
        // 透過事件通知 View 開啟檔案對話框
        RequestOpenIcsFile?.Invoke();
    }

    /// <summary>由 View 呼叫，傳入使用者選擇的 ICS 檔案路徑</summary>
    public void LoadIcsFile(string filePath)
    {
        try
        {
            var events = IcsParser.Parse(filePath);

            // 套用日期篩選
            var filtered = events
                .Where(e => (!_startDate.HasValue || e.StartTime.Date >= _startDate.Value.Date)
                         && (!_endDate.HasValue || e.StartTime.Date <= _endDate.Value.Date))
                .ToList();

            CalendarEvents.Clear();
            foreach (var evt in filtered)
            {
                var vm = new CalendarEventViewModel(evt);
                vm.SelectionChanged += () =>
                {
                    ConfirmCommand.RaiseCanExecuteChanged();
                    DeselectAllCommand.RaiseCanExecuteChanged();
                };
                CalendarEvents.Add(vm);
            }

            if (_selectedSprint != null)
                ApplyDuplicateDetection();

            StatusMessage = filtered.Count > 0
                ? $"已從 ICS 載入 {filtered.Count} 筆會議"
                : "ICS 檔案中查詢區間內無會議";
        }
        catch (Exception ex)
        {
            StatusMessage = $"讀取 ICS 失敗：{ex.Message}";
        }
        finally
        {
            SelectAllCommand.RaiseCanExecuteChanged();
            DeselectAllCommand.RaiseCanExecuteChanged();
            ConfirmCommand.RaiseCanExecuteChanged();
        }
    }

    // 通知 View 開啟 ICS 檔案對話框
    public event Action? RequestOpenIcsFile;

    private void ExecuteLoadCsv()
    {
        RequestOpenCsvFile?.Invoke();
    }

    /// <summary>由 View 呼叫，傳入使用者選擇的 CSV 檔案路徑</summary>
    public void LoadCsvFile(string filePath)
    {
        try
        {
            var events = CsvParser.Parse(filePath);

            // 套用日期篩選
            var filtered = events
                .Where(e => (!_startDate.HasValue || e.StartTime.Date >= _startDate.Value.Date)
                         && (!_endDate.HasValue || e.StartTime.Date <= _endDate.Value.Date))
                .ToList();

            CalendarEvents.Clear();
            foreach (var evt in filtered)
            {
                var vm = new CalendarEventViewModel(evt);
                vm.SelectionChanged += () =>
                {
                    ConfirmCommand.RaiseCanExecuteChanged();
                    DeselectAllCommand.RaiseCanExecuteChanged();
                };
                CalendarEvents.Add(vm);
            }

            if (_selectedSprint != null)
                ApplyDuplicateDetection();

            StatusMessage = filtered.Count > 0
                ? $"已從 CSV 載入 {filtered.Count} 筆會議"
                : "CSV 檔案中查詢區間內無會議";
        }
        catch (Exception ex)
        {
            StatusMessage = $"讀取 CSV 失敗：{ex.Message}";
        }
        finally
        {
            SelectAllCommand.RaiseCanExecuteChanged();
            DeselectAllCommand.RaiseCanExecuteChanged();
            ConfirmCommand.RaiseCanExecuteChanged();
        }
    }

    // 通知 View 開啟 CSV 檔案對話框
    public event Action? RequestOpenCsvFile;

    private async Task LoadTeamsAsync()
    {
        IsLoading = true;
        StatusMessage = "正在載入 Team 清單...";
        try
        {
            var credential = GetCurrentCredential();
            var teams = await _tfsClient.GetTeamsAsync(credential);
            Teams.Clear();
            foreach (var team in teams)
                Teams.Add(team);
            StatusMessage = $"已載入 {teams.Count} 個 Team，請選擇 Team";
        }
        catch (TfsApiException ex)
        {
            StatusMessage = $"載入 Team 失敗：{ex.ErrorDescription}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入 Team 失敗：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSprintsAsync(string? teamName = null)    {
        IsLoading = true;
        StatusMessage = "正在載入 Sprint 清單...";

        try
        {
            var credential = GetCurrentCredential();
            var sprints = await _tfsClient.GetSprintsAsync(credential, teamName: teamName);
            Sprints.Clear();
            foreach (var sprint in sprints)
                Sprints.Add(sprint);
            StatusMessage = $"已載入 {sprints.Count} 個 Sprint";
        }
        catch (TfsApiException ex)
        {
            StatusMessage = $"載入 Sprint 失敗：{ex.ErrorDescription}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入 Sprint 失敗：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadAreasAsync(string? teamName = null)
    {
        try
        {
            var credential = GetCurrentCredential();
            var areas = await _tfsClient.GetAreasAsync(credential, teamName);
            Areas.Clear();
            foreach (var area in areas)
                Areas.Add(area);
            if (areas.Count == 0)
                StatusMessage = "Area 清單為空，請確認 TFS 專案有設定 Area";
        }
        catch (TfsApiException ex)
        {
            StatusMessage = $"載入 Area 失敗：{ex.ErrorDescription}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入 Area 失敗：{ex.Message}";
        }
    }

    private async Task LoadSprintTasksAndDetectDuplicatesAsync(Sprint? sprint)
    {
        if (sprint == null)
        {
            _currentSprintTasks = Array.Empty<TfsTask>();
            ApplyDuplicateDetection();
            ConfirmCommand.RaiseCanExecuteChanged();
            return;
        }

        IsLoading = true;
        StatusMessage = $"正在載入 {sprint.Name} 的任務...";

        try
        {
            var credential = GetCurrentCredential();
            _currentSprintTasks = await _tfsClient.GetTasksBySprintAsync(sprint.IterationPath, credential);
            ApplyDuplicateDetection();
            StatusMessage = $"已載入 {_currentSprintTasks.Count} 個現有任務";
        }
        catch (TfsApiException ex)
        {
            StatusMessage = $"載入 Sprint 任務失敗：{ex.ErrorDescription}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"載入 Sprint 任務失敗：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
            ConfirmCommand.RaiseCanExecuteChanged();
        }
    }

    private void ApplyDuplicateDetection()
    {
        if (!CalendarEvents.Any()) return;

        var events = CalendarEvents.Select(vm => vm.Event).ToList();
        var results = _duplicateDetector.CheckAllDuplicates(events, _currentSprintTasks, _settings.SimilarityThreshold);

        foreach (var result in results)
        {
            var vm = CalendarEvents.FirstOrDefault(v => v.Event.Id == result.Event.Id);
            if (vm == null) continue;

            vm.IsDuplicate = result.IsDuplicate;
            vm.DuplicateWarning = result.IsDuplicate && result.MostSimilarTask != null
                ? $"疑似重複：{result.MostSimilarTask.Title}"
                : string.Empty;
        }

        SelectAllCommand.RaiseCanExecuteChanged();
        DeselectAllCommand.RaiseCanExecuteChanged();
        ConfirmCommand.RaiseCanExecuteChanged();
    }

    private void ExecuteSelectAll()
    {
        foreach (var vm in CalendarEvents.Where(e => e.IsSelectable))
            vm.IsSelected = true;
        ConfirmCommand.RaiseCanExecuteChanged();
        DeselectAllCommand.RaiseCanExecuteChanged();
    }

    private void ExecuteDeselectAll()
    {
        foreach (var vm in CalendarEvents)
            vm.IsSelected = false;
        ConfirmCommand.RaiseCanExecuteChanged();
        DeselectAllCommand.RaiseCanExecuteChanged();
    }

    private async Task ExecuteConfirmAsync()
    {
        var selectedEvents = CalendarEvents.Where(e => e.IsSelected).ToList();
        if (!selectedEvents.Any() || _selectedSprint == null) return;

        IsLoading = true;
        StatusMessage = $"正在建立 {selectedEvents.Count} 個任務...";

        var successCount = 0;
        var failedTitles = new List<string>();
        var credential = GetCurrentCredential();

        // 判斷是否為過去的 Sprint（EndDate 早於今天）
        var today = DateTime.Today;
        var isPastSprint = _selectedSprint.EndDate.HasValue && _selectedSprint.EndDate.Value.Date < today;

        foreach (var vm in selectedEvents)
        {
            try
            {
                var title = $"{vm.StartTime:yyyy/MM/dd} {vm.Subject}";
                var assignedTo = _authService.CurrentCredential != null
                    ? $"{_authService.CurrentCredential.Domain}\\{_authService.CurrentCredential.UserName}"
                    : $"{Environment.UserDomainName}\\{Environment.UserName}";
                var durationHours = (vm.EndTime - vm.StartTime).TotalHours;
                await _tfsClient.CreateTaskAsync(
                    title,
                    _selectedSprint.IterationPath,
                    assignedTo,
                    credential,
                    areaPath: _selectedArea?.AreaPath,
                    durationHours: durationHours,
                    isPastSprint: isPastSprint);
                successCount++;
            }
            catch (Exception)
            {
                failedTitles.Add(vm.Subject);
            }
        }

        // 重新載入 Sprint 任務以更新重複比對基準
        try
        {
            _currentSprintTasks = await _tfsClient.GetTasksBySprintAsync(
                _selectedSprint.IterationPath, credential);
            ApplyDuplicateDetection();
        }
        catch { /* 重新載入失敗不影響主流程 */ }

        IsLoading = false;

        if (!failedTitles.Any())
        {
            StatusMessage = $"已成功建立 {successCount} 個任務";
        }
        else
        {
            StatusMessage = $"成功建立 {successCount} 個任務，失敗 {failedTitles.Count} 個：{string.Join("、", failedTitles)}";
        }

        ConfirmCommand.RaiseCanExecuteChanged();
    }

    private void ExecuteOpenSettings()
    {
        RequestOpenSettings?.Invoke();
        // 重新載入設定
        _settings = _settingsService.Load();
        LoginCommand.RaiseCanExecuteChanged();
    }

    private NetworkCredential GetCurrentCredential()
    {
        return _authService.CurrentCredential
            ?? new NetworkCredential(Environment.UserName, (string?)null, Environment.UserDomainName);
    }

    private void RaiseAllCommandsCanExecuteChanged()
    {
        LoginCommand.RaiseCanExecuteChanged();
        LogoutCommand.RaiseCanExecuteChanged();
        FetchEventsCommand.RaiseCanExecuteChanged();
        LoadIcsCommand.RaiseCanExecuteChanged();
        LoadCsvCommand.RaiseCanExecuteChanged();
        SelectAllCommand.RaiseCanExecuteChanged();
        DeselectAllCommand.RaiseCanExecuteChanged();
        ConfirmCommand.RaiseCanExecuteChanged();
    }
}
