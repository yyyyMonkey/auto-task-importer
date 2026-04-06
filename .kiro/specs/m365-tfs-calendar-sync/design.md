# 設計文件：M365 TFS 行事曆同步工具

## 概覽

本工具為一款 Windows 本機桌面應用程式，採用 .NET 8 + WPF + MVVM 架構開發，目的是將本機 `.ics` 行事曆檔案中的會議自動同步為 TFS / Azure DevOps Kanban 看板上的 Task，消除人工重複登錄的時間成本。

使用者透過公司內部 Windows AD 完成身分驗證後，匯入本機 `.ics` 行事曆檔案，選擇目標 TFS Team / Sprint / Area，系統自動比對重複任務（Levenshtein Distance），並允許使用者選擇性地將會議建立為 TFS Task。

---

## 架構

### 整體架構圖

```
┌─────────────────────────────────────────────────────────────┐
│                        WPF 應用程式                          │
│                                                             │
│  ┌──────────────┐    ┌──────────────────────────────────┐  │
│  │    Views     │◄──►│           ViewModels             │  │
│  │  (XAML UI)   │    │  MainViewModel / SettingsViewModel│  │
│  └──────────────┘    └──────────────┬───────────────────┘  │
│                                     │                       │
│                      ┌──────────────▼───────────────────┐  │
│                      │          Services Layer           │  │
│                      │  AuthService / IcsParser /        │  │
│                      │  TfsClient / DuplicateDetector    │  │
│                      └──────────────┬───────────────────┘  │
│                                     │                       │
│                      ┌──────────────▼───────────────────┐  │
│                      │          Models Layer             │  │
│                      │  CalendarEvent / TfsTask /        │  │
│                      │  Sprint / TfsTeam / TfsArea /     │  │
│                      │  AppSettings                      │  │
│                      └──────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
         │                          │
         ▼                          ▼
  本機 .ics 檔案              TFS REST API /
  （IcsParser 解析）    Microsoft.TeamFoundationServer.Client
```

### 專案結構

```
M365TfsSync/
├── App.xaml / App.xaml.cs          # 應用程式進入點、DI 容器設定
├── Models/
│   ├── AuthMode.cs                 # 驗證方式列舉
│   ├── CalendarEvent.cs            # 行事曆會議資料模型
│   ├── TfsTask.cs                  # TFS Work Item 資料模型
│   ├── Sprint.cs                   # TFS Sprint 資料模型（含 DisplayName）
│   ├── TfsTeam.cs                  # TFS Team 資料模型
│   ├── TfsArea.cs                  # TFS Area 資料模型（含 DisplayName）
│   └── AppSettings.cs              # 應用程式設定模型
├── ViewModels/
│   ├── ViewModelBase.cs            # INotifyPropertyChanged 基底類別
│   ├── RelayCommand.cs             # ICommand 實作
│   ├── CalendarEventViewModel.cs   # 包裝 CalendarEvent 的 ViewModel
│   ├── MainViewModel.cs            # 主視窗 ViewModel
│   └── SettingsViewModel.cs        # 設定視窗 ViewModel
├── Views/
│   ├── MainWindow.xaml             # 主視窗
│   ├── MainWindow.xaml.cs          # 主視窗 code-behind
│   ├── SettingsWindow.xaml         # 設定視窗
│   └── SettingsWindow.xaml.cs      # 設定視窗 code-behind
├── Services/
│   ├── Interfaces/
│   │   ├── IAuthService.cs
│   │   ├── IGraphClient.cs         # 保留介面（未來擴充用）
│   │   ├── ITfsClient.cs
│   │   └── IDuplicateDetector.cs
│   ├── AuthService.cs
│   ├── GraphClient.cs              # 保留實作（未來擴充用）
│   ├── IcsParser.cs                # 本機 .ics 檔案解析器
│   ├── TfsClient.cs
│   ├── DuplicateDetector.cs
│   ├── LevenshteinDistance.cs      # 字串相似度演算法
│   ├── Exceptions.cs               # 自訂例外類別
│   └── SettingsService.cs          # 設定加密讀寫服務
├── Converters/
│   └── BoolToVisibilityConverter.cs
└── M365TfsSync.csproj
```

### 各層職責

| 層級 | 職責 |
|------|------|
| Views | 純 XAML UI 定義，不含業務邏輯，透過 DataBinding 與 ViewModel 溝通 |
| ViewModels | 協調 UI 狀態與業務邏輯，呼叫 Services，暴露 Commands 與 Properties |
| Services | 封裝外部 API 呼叫（Graph、TFS）、身分驗證、重複比對演算法 |
| Models | 純資料結構，不含業務邏輯 |

---

## 元件與介面

### UI 佈局設計

#### 主視窗（MainWindow）

```
┌─────────────────────────────────────────────────────────────────┐
│  M365 TFS 行事曆同步工具                          [設定] [_][□][X]│
├─────────────────────────────────────────────────────────────────┤
│ ┌─── 身分驗證區 ──────────────────────────────────────────────┐ │
│ │ 驗證方式: ● 帳號密碼驗證  ○ Windows 整合驗證（停用）        │ │
│ │ 帳號: [___________]  密碼: [___________]  [登入] [登出]     │ │
│ │ 狀態: 已登入：username@domain                               │ │
│ └─────────────────────────────────────────────────────────────┘ │
│ ┌─── 查詢條件區 ──────────────────────────────────────────────┐ │
│ │ Team: [ComboBox ▼]  Sprint: [ComboBox ▼]  Area: [ComboBox ▼]│ │
│ │ 開始日期: [DatePicker]  結束日期: [DatePicker]  [載入 ICS]  │ │
│ └─────────────────────────────────────────────────────────────┘ │
│ ┌─── 會議清單區 ──────────────────────────────────────────────┐ │
│ │ [全選] [全部取消]                          ⟳ 載入中...      │ │
│ │ ┌──┬──────────────────┬──────────────┬──────────────┬────┐ │ │
│ │ │☑ │ 會議主旨          │ 開始時間      │ 結束時間      │警告│ │ │
│ │ ├──┼──────────────────┼──────────────┼──────────────┼────┤ │ │
│ │ │☑ │ 週會             │2024/01/15 09:00│2024/01/15 10:00│    │ │ │
│ │ │⊘ │ Sprint Review    │2024/01/15 14:00│2024/01/15 15:00│⚠疑似│ │ │
│ │ └──┴──────────────────┴──────────────┴──────────────┴────┘ │ │
│ └─────────────────────────────────────────────────────────────┘ │
│                                              [確定（建立 Task）] │
└─────────────────────────────────────────────────────────────────┘
```

#### 設定視窗（SettingsWindow）

```
┌─────────────────────────────────────────────────────┐
│  應用程式設定                              [_][□][X] │
├─────────────────────────────────────────────────────┤
│ TFS 伺服器 URL:    [________________________________] │
│ TFS 專案名稱:      [________________________________] │
│ AD 網域名稱:       [________________________________] │
│                                                     │
│              [測試連線]  [儲存]  [取消]              │
└─────────────────────────────────────────────────────┘
```

### MVVM 架構設計

#### ViewModelBase

```csharp
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null);
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null);
}
```

#### MainViewModel

| 屬性 | 型別 | 說明 |
|------|------|------|
| `AuthMode` | `AuthMode` (enum) | 目前選擇的驗證方式（預設 UsernamePassword） |
| `Username` | `string` | 帳號輸入欄位 |
| `Password` | `string` | 密碼輸入欄位 |
| `IsAuthenticated` | `bool` | 是否已完成驗證 |
| `LoggedInUser` | `string` | 已登入使用者名稱 |
| `StartDate` | `DateTime?` | 查詢開始日期（選 Sprint 後自動帶入） |
| `EndDate` | `DateTime?` | 查詢結束日期（選 Sprint 後自動帶入） |
| `CalendarEvents` | `ObservableCollection<CalendarEventViewModel>` | 會議清單 |
| `Teams` | `ObservableCollection<TfsTeam>` | Team 清單 |
| `SelectedTeam` | `TfsTeam?` | 目前選擇的 Team |
| `Sprints` | `ObservableCollection<Sprint>` | Sprint 清單 |
| `SelectedSprint` | `Sprint?` | 目前選擇的 Sprint |
| `Areas` | `ObservableCollection<TfsArea>` | Area 清單 |
| `SelectedArea` | `TfsArea?` | 目前選擇的 Area（選填） |
| `IsLoading` | `bool` | 是否正在載入 |
| `StatusMessage` | `string` | 狀態列訊息 |
| `CanConfirm` | `bool` | 確定按鈕是否可用（計算屬性） |

| Command | 說明 |
|---------|------|
| `LoginCommand` | 執行 AD 驗證 |
| `LogoutCommand` | 清除憑證並重置狀態 |
| `FetchEventsCommand` | 呼叫 GraphClient 取得會議（保留，目前停用） |
| `LoadIcsCommand` | 開啟檔案對話框載入本機 .ics 檔案 |
| `SelectAllCommand` | 全選未重複的會議 |
| `DeselectAllCommand` | 全部取消勾選 |
| `ConfirmCommand` | 建立 TFS Task |
| `OpenSettingsCommand` | 開啟設定視窗 |

#### SettingsViewModel

| 屬性 | 型別 | 說明 |
|------|------|------|
| `TfsServerUrl` | `string` | TFS 伺服器 URL |
| `TfsProjectName` | `string` | TFS 專案名稱 |
| `AdDomain` | `string` | AD 網域名稱 |

| Command | 說明 |
|---------|------|
| `SaveCommand` | 儲存設定 |
| `TestConnectionCommand` | 測試 TFS 連線 |
| `CancelCommand` | 取消並關閉視窗 |

#### CalendarEventViewModel（包裝 CalendarEvent 的 ViewModel）

| 屬性 | 型別 | 說明 |
|------|------|------|
| `Event` | `CalendarEvent` | 原始資料 |
| `IsSelected` | `bool` | 是否被勾選 |
| `IsDuplicate` | `bool` | 是否被標記為疑似重複 |
| `DuplicateWarning` | `string` | 重複警告訊息 |
| `IsSelectable` | `bool` | 勾選框是否可用（= !IsDuplicate） |

---

## 資料模型

### CalendarEvent

```csharp
public class CalendarEvent
{
    public string Id { get; set; }           // ICS UID 或 GUID
    public string Subject { get; set; }      // 會議主旨（SUMMARY）
    public DateTime StartTime { get; set; }  // 開始時間（DTSTART）
    public DateTime EndTime { get; set; }    // 結束時間（DTEND）
}
```

### TfsTask

```csharp
public class TfsTask
{
    public int Id { get; set; }              // Work Item ID
    public string Title { get; set; }        // 任務主旨
    public string AssignedTo { get; set; }   // 指派給
    public string IterationPath { get; set; }// 迭代路徑
    public string State { get; set; }        // 狀態
}
```

### Sprint

```csharp
public class Sprint
{
    public string Id { get; set; }           // Sprint ID
    public string Name { get; set; }         // Sprint 名稱
    public string IterationPath { get; set; }// 完整迭代路徑
    public DateTime? StartDate { get; set; } // Sprint 開始日期
    public DateTime? EndDate { get; set; }   // Sprint 結束日期
    public string DisplayName { get; }       // 顯示最後兩層路徑，例如 "2026 Q1\(0323~0403)"
}
```

### TfsTeam

```csharp
public class TfsTeam
{
    public string Id { get; set; }           // Team ID
    public string Name { get; set; }         // Team 名稱
}
```

### TfsArea

```csharp
public class TfsArea
{
    public string Id { get; set; }           // Area 節點 ID
    public string Name { get; set; }         // Area 名稱
    public string AreaPath { get; set; }     // 完整 Area Path（已去除 \Area\ 中間層）
    public string DisplayName { get; }       // 顯示最後兩層路徑
}
```

### AppSettings

```csharp
public class AppSettings
{
    public string TfsServerUrl { get; set; }      // TFS 伺服器 URL
    public string TfsProjectName { get; set; }    // TFS 專案名稱
    public string AdDomain { get; set; }          // AD 網域名稱
    public double SimilarityThreshold { get; set; } = 80.0; // 相似度門檻（%）
    public string AzureClientId { get; set; }     // Azure AD Client ID（選用）
    public string AzureTenantId { get; set; }     // Azure AD Tenant ID（選用）
}
```

### AuthMode 列舉

```csharp
public enum AuthMode
{
    WindowsIntegrated,  // Windows 整合驗證（停用）
    UsernamePassword    // 帳號密碼驗證（預設）
}
```

---

## 服務層設計

### IAuthService

```csharp
public interface IAuthService
{
    bool IsAuthenticated { get; }
    string? CurrentUsername { get; }
    NetworkCredential? CurrentCredential { get; }

    // Windows 整合驗證
    Task<AuthResult> LoginWithWindowsCredentialsAsync();

    // 帳號密碼驗證
    Task<AuthResult> LoginWithCredentialsAsync(string username, string password, string domain);

    void Logout();
}
```

**實作說明：**
- `LoginWithWindowsCredentialsAsync`：使用 `CredentialCache.DefaultNetworkCredentials` 取得目前 Windows 登入憑證，並嘗試對 TFS 伺服器發出一次測試請求以確認憑證有效。
- `LoginWithCredentialsAsync`：使用 `PrincipalContext(ContextType.Domain, domain)` 建立 AD 連線，呼叫 `ValidateCredentials(username, password)` 驗證，成功後建立 `NetworkCredential` 物件儲存。
- `Logout`：清除 `CurrentCredential`，重置 `IsAuthenticated` 為 false。

### IGraphClient / GraphClient

介面保留供未來擴充，目前行事曆資料改由本機 `.ics` 檔案提供。

### IcsParser（靜態類別）

```csharp
public static class IcsParser
{
    public static IReadOnlyList<CalendarEvent> Parse(string filePath);
}
```

**實作說明：**
- 讀取 `.ics` 檔案，依 RFC 5545 規範解析 `VEVENT` 區塊。
- 支援折行（Unfold）處理。
- 解析 `SUMMARY`（主旨）、`UID`、`DTSTART`、`DTEND`。
- 支援 UTC 時間（尾端 `Z`）自動轉換為本地時間，以及 `TZID` 參數。
- 解碼 RFC 5545 跳脫字元（`\n`、`\,`、`\;`、`\\`）。

### ITfsClient

```csharp
public interface ITfsClient
{
    // 取得使用者有權限的 Team 清單
    Task<IReadOnlyList<TfsTeam>> GetTeamsAsync(
        NetworkCredential credential,
        CancellationToken cancellationToken = default);

    // 取得指定 Team 的 Sprint 清單
    Task<IReadOnlyList<Sprint>> GetSprintsAsync(
        NetworkCredential credential,
        CancellationToken cancellationToken = default,
        string? teamName = null);

    // 取得指定 Team 的 Area 清單（已去除 \Area\ 中間層）
    Task<IReadOnlyList<TfsArea>> GetAreasAsync(
        NetworkCredential credential,
        string? teamName = null,
        CancellationToken cancellationToken = default);

    // 取得指定 Sprint 的所有 Task
    Task<IReadOnlyList<TfsTask>> GetTasksBySprintAsync(
        string iterationPath,
        NetworkCredential credential,
        CancellationToken cancellationToken = default);

    // 建立 Task，支援 Area、時數、過去 Sprint 自動關閉
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
```

**實作說明：**
- 使用 `Microsoft.TeamFoundationServer.Client` NuGet 套件（`Microsoft.VisualStudio.Services.Client`）。
- 驗證使用 `VssCredentials(new WindowsCredential(credential))`，設定 `PromptType = DoNotPrompt` 防止帳號被鎖。
- `GetTeamsAsync`：呼叫 `TeamHttpClient.GetTeamsAsync(projectName, mine: true)` 只取使用者有權限的 Team。
- `GetSprintsAsync`：若指定 teamName，使用 `WorkHttpClient.GetTeamIterationsAsync` 取得該 Team 的 Sprint；否則用 `GetClassificationNodeAsync` 取全部。
- `GetAreasAsync`：呼叫 `GetClassificationNodeAsync(Areas)`，去除 `\Area\` 中間層後，依 teamName 過濾第二層路徑。
- `GetTasksBySprintAsync`：WIQL 查詢該 Sprint 所有 Task（不限 AssignedTo，避免格式不符導致空結果）。
- `CreateTaskAsync`：
  - 使用 `JsonPatchDocument` 建立 Work Item
  - 設定 `System.Title`、`System.IterationPath`、`System.AssignedTo`
  - 若有 `areaPath`，設定 `System.AreaPath`
  - 若有 `durationHours`，設定 `OriginalEstimate`
  - 若是當前/未來 Sprint，設定 `RemainingWork = durationHours`
  - 若是過去 Sprint（EndDate < 今天），設定 `CompletedWork = durationHours`，建立後再 `UpdateWorkItemAsync` 將 State 改為 Closed

### IDuplicateDetector

```csharp
public interface IDuplicateDetector
{
    DuplicateCheckResult CheckDuplicate(
        CalendarEvent calendarEvent,
        IReadOnlyList<TfsTask> existingTasks,
        double similarityThreshold = 80.0);

    IReadOnlyList<DuplicateCheckResult> CheckAllDuplicates(
        IReadOnlyList<CalendarEvent> calendarEvents,
        IReadOnlyList<TfsTask> existingTasks,
        double similarityThreshold = 80.0);
}

public class DuplicateCheckResult
{
    public CalendarEvent Event { get; set; }
    public bool IsDuplicate { get; set; }
    public TfsTask? MostSimilarTask { get; set; }
    public double SimilarityScore { get; set; }  // 0.0 ~ 100.0
}
```

### SettingsService

```csharp
public class SettingsService
{
    private readonly string _settingsFilePath;  // %APPDATA%\M365TfsSync\settings.json

    public AppSettings Load();
    public void Save(AppSettings settings);
}
```

---

## 字串相似度演算法

### 選擇理由

採用 **Levenshtein Distance（編輯距離）** 演算法，原因如下：

1. **語意直覺**：會議主旨與 TFS Task 標題通常為短字串，編輯距離能直觀反映兩個字串的相似程度。
2. **實作簡單**：標準動態規劃實作，無需外部相依套件。
3. **需求明確**：需求文件已明確指定使用此演算法，並定義相似度公式。

### 演算法說明

Levenshtein Distance 計算將字串 `s1` 轉換為字串 `s2` 所需的最少單字元操作次數（插入、刪除、替換）。

相似度公式（依需求 4.2）：
```
similarity = (1 - editDistance / max(len(s1), len(s2))) * 100
```

特殊情況：若兩個字串均為空字串，相似度定義為 100%。

### C# 實作虛擬碼

```csharp
public static class LevenshteinDistance
{
    public static int Compute(string s1, string s2)
    {
        // 正規化：忽略大小寫
        s1 = s1.ToLowerInvariant();
        s2 = s2.ToLowerInvariant();

        int m = s1.Length, n = s2.Length;

        // 建立 (m+1) x (n+1) 的 DP 矩陣
        int[,] dp = new int[m + 1, n + 1];

        // 初始化邊界：空字串到長度 i 的字串需要 i 次操作
        for (int i = 0; i <= m; i++) dp[i, 0] = i;
        for (int j = 0; j <= n; j++) dp[0, j] = j;

        for (int i = 1; i <= m; i++)
        {
            for (int j = 1; j <= n; j++)
            {
                int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1,      // 刪除
                             dp[i, j - 1] + 1),      // 插入
                    dp[i - 1, j - 1] + cost           // 替換
                );
            }
        }

        return dp[m, n];
    }

    public static double ComputeSimilarity(string s1, string s2)
    {
        if (s1.Length == 0 && s2.Length == 0) return 100.0;
        int maxLen = Math.Max(s1.Length, s2.Length);
        int distance = Compute(s1, s2);
        return (1.0 - (double)distance / maxLen) * 100.0;
    }
}
```

---

## 設定儲存設計

### 加密方案：DPAPI（Data Protection API）

使用 .NET 內建的 `System.Security.Cryptography.ProtectedData` 類別，以 **DPAPI（Data Protection API）** 加密設定檔。

**選擇理由：**
- DPAPI 與 Windows 使用者帳號綁定，只有同一使用者才能解密，無需管理加密金鑰。
- 完全本機運作，不依賴外部服務。
- .NET 內建支援，無需額外套件。

### 儲存流程

```
AppSettings 物件
    │
    ▼ JSON 序列化（System.Text.Json）
JSON 字串（UTF-8 bytes）
    │
    ▼ ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser)
加密後的 byte[]
    │
    ▼ Convert.ToBase64String
Base64 字串
    │
    ▼ 寫入檔案
%APPDATA%\M365TfsSync\settings.dat
```

### 讀取流程（反向）

```
%APPDATA%\M365TfsSync\settings.dat
    │
    ▼ Convert.FromBase64String
加密後的 byte[]
    │
    ▼ ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser)
JSON 字串（UTF-8 bytes）
    │
    ▼ JSON 反序列化（System.Text.Json）
AppSettings 物件
```

### SettingsService 實作重點

```csharp
public class SettingsService
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "M365TfsSync");
    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.dat");

    public AppSettings Load()
    {
        if (!File.Exists(SettingsFile)) return new AppSettings();
        var base64 = File.ReadAllText(SettingsFile);
        var encrypted = Convert.FromBase64String(base64);
        var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        var json = Encoding.UTF8.GetString(decrypted);
        return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(SettingsDir);
        var json = JsonSerializer.Serialize(settings);
        var data = Encoding.UTF8.GetBytes(json);
        var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
        File.WriteAllText(SettingsFile, Convert.ToBase64String(encrypted));
    }
}
```

---

## 錯誤處理策略

### 自訂例外類別

```csharp
public class AuthException : Exception { ... }          // 驗證失敗
public class GraphApiException : Exception              // Graph API 錯誤
{
    public HttpStatusCode StatusCode { get; }
    public string ErrorDescription { get; }
}
public class TfsApiException : Exception                // TFS API 錯誤
{
    public string ErrorDescription { get; }
}
public class SettingsException : Exception { ... }      // 設定讀寫錯誤
```

### 各服務層例外處理

| 服務 | 例外情境 | 處理方式 |
|------|----------|----------|
| `AuthService` | AD 連線失敗、帳密錯誤 | 拋出 `AuthException`，ViewModel 捕捉後更新 `StatusMessage` |
| `GraphClient` | HTTP 4xx/5xx、網路逾時 | 拋出 `GraphApiException`（含狀態碼），ViewModel 顯示錯誤訊息 |
| `TfsClient` | TFS 連線失敗、WIQL 錯誤、Work Item 建立失敗 | 拋出 `TfsApiException`，建立 Task 時逐一捕捉，收集失敗清單後統一回報 |
| `SettingsService` | 檔案損毀、解密失敗 | 捕捉例外後回傳預設 `AppSettings`，並記錄警告訊息 |
| `DuplicateDetector` | 輸入為 null | 防禦性檢查，空清單回傳空結果 |

### ViewModel 層錯誤處理原則

- 所有 `Command` 的 `Execute` 方法均以 `try-catch` 包覆。
- 捕捉到例外時，更新 `StatusMessage` 屬性以顯示使用者友善的錯誤訊息。
- 使用 `IsBusy` / `IsLoading` 旗標確保 UI 在例外發生後恢復可操作狀態。
- 批次建立 TFS Task 時（需求 6.6），採用「繼續執行」策略：逐一嘗試，收集成功與失敗清單，最後統一顯示摘要報告。

---

## 發布設定

### .csproj 設定

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- 發布設定 -->
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <SelfContained>true</SelfContained>
    <PublishSingleFile>true</PublishSingleFile>
    <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
    <PublishReadyToRun>true</PublishReadyToRun>

    <!-- 排除不必要的檔案以縮小體積 -->
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  </PropertyGroup>
</Project>
```

### 發布指令

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  /p:PublishSingleFile=true \
  /p:EnableCompressionInSingleFile=true
```

### 注意事項

- WPF 應用程式使用 `PublishSingleFile` 時，部分原生 DLL（如 `PresentationNative_cor3.dll`）可能仍需解壓縮至暫存目錄，此為 WPF 框架限制，不影響使用者體驗。
- 目標平台：Windows 10 版本 1903（Build 18362）及以上、Windows 11。
- 建議在 CI/CD 流程中加入 `dotnet publish` 步驟以確保可重現的建置結果。


---

## 正確性屬性（Correctness Properties）

*屬性（Property）是指在系統所有有效執行過程中都應成立的特性或行為，本質上是對系統應做什麼的形式化陳述。屬性作為人類可讀規格與機器可驗證正確性保證之間的橋樑。*

### 屬性 1：驗證成功後狀態正確性

*對任意* 使用者帳號，當 AuthService 回報驗證成功時，MainViewModel 的 `IsAuthenticated` 應為 true，且 `LoggedInUser` 應包含該使用者的帳號名稱，且所有需要授權的 Command 的 `CanExecute` 應從 false 變為 true。

**驗證需求：1.5**

---

### 屬性 2：驗證失敗後狀態不變

*對任意* 驗證失敗的情況（任意錯誤原因），MainViewModel 的 `IsAuthenticated` 應仍為 false，`StatusMessage` 應包含失敗原因，且使用者應能再次嘗試登入（LoginCommand.CanExecute 仍為 true）。

**驗證需求：1.6**

---

### 屬性 3：登出後狀態重置（Round-Trip）

*對任意* 已完成驗證的狀態，執行登出後，`IsAuthenticated` 應為 false，`CurrentCredential` 應為 null，應用程式狀態應與初始啟動時相同。

**驗證需求：1.7**

---

### 屬性 4：會議清單填充正確性

*對任意* GraphClient 回傳的 CalendarEvent 清單（任意數量、任意內容），MainViewModel 的 `CalendarEvents` 集合應包含與回傳清單數量相同的項目，且每個項目的主旨、開始時間、結束時間應與原始資料一致。

**驗證需求：2.3**

---

### 屬性 5：API 錯誤訊息包含狀態碼

*對任意* HTTP 狀態碼的 GraphApiException，MainViewModel 的 `StatusMessage` 應包含該 HTTP 狀態碼與錯誤描述，且 `CalendarEvents` 集合應保持不變。

**驗證需求：2.5**

---

### 屬性 6：日期驗證規則

*對任意* 日期組合，當 `EndDate < StartDate` 時，`FetchEventsCommand.CanExecute` 應為 false 且應顯示驗證錯誤訊息；當 `StartDate` 或 `EndDate` 為 null 時，`FetchEventsCommand.CanExecute` 應為 false；只有當 `StartDate <= EndDate` 且兩者均不為 null 時，`FetchEventsCommand.CanExecute` 才應為 true。

**驗證需求：2.7, 2.8**

---

### 屬性 7：載入中狀態禁用操作

*對任意* 正在進行 API 呼叫的狀態（`IsLoading = true`），所有觸發 API 呼叫的 Command（`FetchEventsCommand`、Sprint 選擇、`ConfirmCommand`）的 `CanExecute` 應為 false，防止重複提交。

**驗證需求：2.6, 3.6, 6.7**

---

### 屬性 8：Sprint 切換觸發重複比對

*對任意* Sprint 切換操作，切換後 DuplicateDetector 應使用新 Sprint 的 TfsTask 清單重新計算所有 CalendarEvent 的重複標記，且每個 CalendarEvent 的 `IsDuplicate` 狀態應僅取決於新 Sprint 的 TfsTask 清單。

**驗證需求：3.3, 4.7, 4.8**

---

### 屬性 9：Levenshtein 相似度計算正確性

*對任意* 兩個字串 s1 和 s2，`LevenshteinDistance.ComputeSimilarity(s1, s2)` 的回傳值應在 0.0 到 100.0 之間（含邊界），且當 s1 == s2 時相似度應為 100.0，當兩者均為空字串時相似度應為 100.0，且計算結果應滿足對稱性（`Similarity(s1, s2) == Similarity(s2, s1)`）。

**驗證需求：4.1, 4.2**

---

### 屬性 10：重複標記門檻正確性

*對任意* CalendarEvent 和 TfsTask 清單，當任一 TfsTask 主旨與 CalendarEvent 主旨的相似度 >= 80% 時，該 CalendarEvent 的 `IsDuplicate` 應為 true 且 `DuplicateWarning` 應包含最相似的 TfsTask 主旨；當所有 TfsTask 主旨的相似度均 < 80% 時，`IsDuplicate` 應為 false。

**驗證需求：4.3, 4.4**

---

### 屬性 11：勾選框可用性與重複標記互斥

*對任意* CalendarEventViewModel，`IsSelectable` 應恆等於 `!IsDuplicate`，即重複標記與勾選框可用性永遠互斥。

**驗證需求：4.5, 4.6**

---

### 屬性 12：全選僅影響非重複項目

*對任意* 包含重複與非重複 CalendarEvent 的清單，執行「全選」後，所有 `IsDuplicate = false` 的項目的 `IsSelected` 應為 true，所有 `IsDuplicate = true` 的項目的 `IsSelected` 應仍為 false。

**驗證需求：5.2**

---

### 屬性 13：確定按鈕啟用條件

*對任意* 應用程式狀態，`ConfirmCommand.CanExecute` 應當且僅當「至少有一個 `IsSelected = true` 的 CalendarEvent」且「`SelectedSprint` 不為 null」時為 true，否則為 false。

**驗證需求：5.4, 5.5**

---

### 屬性 14：TfsTask 建立欄位規格

*對任意* 已勾選的 CalendarEvent 和選定的 Sprint，建立的 TfsTask 應滿足：標題格式為 `yyyy/MM/dd 會議主旨`（日期為 CalendarEvent 的開始日期）、`AssignedTo` 為當前登入使用者、`IterationPath` 為選定 Sprint 的 IterationPath。

**驗證需求：6.2, 6.3, 6.4**

---

### 屬性 15：批次建立的錯誤恢復

*對任意* 包含部分失敗的批次建立操作，系統應繼續嘗試建立其餘的 TfsTask（不因單一失敗而中止），最終的摘要報告應包含正確的成功數量與所有失敗項目的主旨。

**驗證需求：6.6**

---

### 屬性 16：設定儲存 Round-Trip

*對任意* AppSettings 物件，執行 `SettingsService.Save(settings)` 後再執行 `SettingsService.Load()` 應回傳與原始物件欄位值完全相同的 AppSettings 物件。

**驗證需求：7.2**

---

### 屬性 17：必要設定缺失時禁用登入

*對任意* AppSettings 狀態，當 `TfsServerUrl` 或 `TfsProjectName` 為空字串或 null 時，`LoginCommand.CanExecute` 應為 false。

**驗證需求：7.4**

---

## 測試策略

### 雙軌測試方法

本專案採用**單元測試**與**屬性測試（Property-Based Testing）**並行的測試策略，兩者互補，共同確保軟體正確性。

| 測試類型 | 適用場景 | 工具 |
|----------|----------|------|
| 單元測試 | 特定範例、邊界情況、整合點、錯誤條件 | xUnit + Moq |
| 屬性測試 | 通用規則、大量隨機輸入驗證 | FsCheck（.NET 屬性測試函式庫） |

### 屬性測試函式庫

採用 **FsCheck**（`FsCheck.Xunit` NuGet 套件）作為屬性測試框架。

- FsCheck 是 .NET 生態系中最成熟的屬性測試函式庫，支援 C# 與 F#。
- 每個屬性測試預設執行 **100 次**隨機輸入迭代。
- 每個屬性測試必須以標籤標記，格式：`// Feature: m365-tfs-calendar-sync, Property {編號}: {屬性描述}`

### 單元測試重點

- **AuthService**：mock AD 環境，測試驗證成功/失敗的狀態轉換（對應屬性 1、2、3）
- **GraphClient**：mock HttpClient，測試 API 回應映射與錯誤處理（對應屬性 4、5）
- **TfsClient**：mock TFS SDK，測試 WIQL 查詢與 Work Item 建立（對應屬性 14、15）
- **SettingsService**：測試加密儲存的 round-trip（對應屬性 16）
- **MainViewModel**：mock 所有 Service，測試 Command 的 CanExecute 邏輯（對應屬性 6、7、13、17）
- 邊界情況：空 CalendarEvent 清單（需求 2.4）、空 TfsTask 清單（需求 3.5）

### 屬性測試重點

每個屬性測試對應設計文件中的一個正確性屬性：

```csharp
// Feature: m365-tfs-calendar-sync, Property 9: Levenshtein 相似度計算正確性
[Property]
public Property SimilarityIsSymmetric(string s1, string s2)
{
    var sim1 = LevenshteinDistance.ComputeSimilarity(s1 ?? "", s2 ?? "");
    var sim2 = LevenshteinDistance.ComputeSimilarity(s2 ?? "", s1 ?? "");
    return (Math.Abs(sim1 - sim2) < 0.001).ToProperty();
}

// Feature: m365-tfs-calendar-sync, Property 9: Levenshtein 相似度計算正確性
[Property]
public Property SimilarityIsInRange(string s1, string s2)
{
    var sim = LevenshteinDistance.ComputeSimilarity(s1 ?? "", s2 ?? "");
    return (sim >= 0.0 && sim <= 100.0).ToProperty();
}

// Feature: m365-tfs-calendar-sync, Property 11: 勾選框可用性與重複標記互斥
[Property]
public Property IsSelectableIsInverseOfIsDuplicate(bool isDuplicate)
{
    var vm = new CalendarEventViewModel { IsDuplicate = isDuplicate };
    return (vm.IsSelectable == !isDuplicate).ToProperty();
}

// Feature: m365-tfs-calendar-sync, Property 16: 設定儲存 Round-Trip
[Property]
public Property SettingsRoundTrip(string url, string project, string domain)
{
    var settings = new AppSettings
    {
        TfsServerUrl = url ?? "",
        TfsProjectName = project ?? "",
        AdDomain = domain ?? ""
    };
    _settingsService.Save(settings);
    var loaded = _settingsService.Load();
    return (loaded.TfsServerUrl == settings.TfsServerUrl &&
            loaded.TfsProjectName == settings.TfsProjectName &&
            loaded.AdDomain == settings.AdDomain).ToProperty();
}
```

### 測試專案結構

```
M365TfsSync.Tests/
├── Unit/
│   ├── Services/
│   │   ├── AuthServiceTests.cs
│   │   ├── DuplicateDetectorTests.cs
│   │   └── SettingsServiceTests.cs
│   └── ViewModels/
│       ├── MainViewModelTests.cs
│       └── SettingsViewModelTests.cs
└── Properties/
    ├── LevenshteinDistancePropertyTests.cs
    ├── DuplicateDetectorPropertyTests.cs
    ├── CalendarEventViewModelPropertyTests.cs
    └── SettingsServicePropertyTests.cs
```
