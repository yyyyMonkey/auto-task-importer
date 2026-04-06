# 實作計畫：M365 TFS 行事曆同步工具

## 概覽

本計畫將設計文件拆解為可逐步執行的編碼任務，採用 .NET 8 + WPF + MVVM 架構，以 C# 實作。每個任務均可獨立執行，並在前一任務的基礎上累積建構，最終整合為完整的單一執行檔應用程式。

## 任務清單

- [x] 1. 建立專案基礎結構與 NuGet 套件設定
  - 建立 `M365TfsSync.csproj`，設定 `net8.0-windows`、`UseWPF`、`Nullable`、`ImplicitUsings`
  - 加入發布設定：`RuntimeIdentifier=win-x64`、`SelfContained=true`、`PublishSingleFile=true`、`EnableCompressionInSingleFile=true`
  - 加入 NuGet 套件：`Microsoft.Graph`、`Microsoft.Identity.Client`、`Microsoft.TeamFoundationServer.Client`、`Microsoft.VisualStudio.Services.Client`、`System.DirectoryServices.AccountManagement`
  - 建立目錄結構：`Models/`、`ViewModels/`、`Views/`、`Services/Interfaces/`、`Converters/`
  - 建立 `App.xaml` / `App.xaml.cs` 進入點（含基本 DI 容器設定）
  - _需求：8.1, 8.2, 8.3_

- [x] 2. 實作資料模型層
  - [x] 2.1 建立核心資料模型
    - 實作 `Models/CalendarEvent.cs`：`Id`、`Subject`、`StartTime`、`EndTime`
    - 實作 `Models/TfsTask.cs`：`Id`、`Title`、`AssignedTo`、`IterationPath`、`State`
    - 實作 `Models/Sprint.cs`：`Id`、`Name`、`IterationPath`、`StartDate`、`EndDate`
    - 實作 `Models/AppSettings.cs`：`TfsServerUrl`、`TfsProjectName`、`AdDomain`、`SimilarityThreshold`（預設 80.0）
    - 實作 `Models/AuthMode.cs` 列舉：`WindowsIntegrated`、`UsernamePassword`
    - _需求：1.2, 1.3, 2.3, 3.1, 7.1_

- [x] 3. 定義服務層介面
  - [x] 3.1 建立所有服務介面
    - 實作 `Services/Interfaces/IAuthService.cs`：`IsAuthenticated`、`CurrentUsername`、`CurrentCredential`、`LoginWithWindowsCredentialsAsync`、`LoginWithCredentialsAsync`、`Logout`
    - 實作 `Services/Interfaces/IGraphClient.cs`：`GetCalendarEventsAsync`
    - 實作 `Services/Interfaces/ITfsClient.cs`：`GetSprintsAsync`、`GetTasksBySprintAsync`、`CreateTaskAsync`、`TestConnectionAsync`
    - 實作 `Services/Interfaces/IDuplicateDetector.cs`：`CheckDuplicate`、`CheckAllDuplicates`，以及 `DuplicateCheckResult` 類別
    - 建立自訂例外類別：`AuthException`、`GraphApiException`（含 `StatusCode`）、`TfsApiException`、`SettingsException`
    - _需求：1.1, 2.2, 3.1, 4.1, 7.5_

- [x] 4. 實作設定服務（SettingsService + DPAPI 加密）
  - [x] 4.1 實作 SettingsService
    - 實作 `Services/SettingsService.cs`
    - `Load()`：從 `%APPDATA%\M365TfsSync\settings.dat` 讀取 Base64 → `ProtectedData.Unprotect` → JSON 反序列化
    - `Save()`：JSON 序列化 → `ProtectedData.Protect` → Base64 → 寫入檔案
    - 檔案不存在時回傳預設 `AppSettings`；解密失敗時捕捉例外並回傳預設值
    - _需求：7.2, 7.3_

  - [ ]* 4.2 撰寫 SettingsService 屬性測試
    - **屬性 16：設定儲存 Round-Trip**
    - **驗證需求：7.2**

- [x] 5. 實作身分驗證服務（AuthService）
  - [x] 5.1 實作 AuthService
    - 實作 `Services/AuthService.cs`，實作 `IAuthService`
    - `LoginWithWindowsCredentialsAsync`：使用 `CredentialCache.DefaultNetworkCredentials`，對 TFS 伺服器發出測試請求確認憑證有效
    - `LoginWithCredentialsAsync`：使用 `PrincipalContext(ContextType.Domain, domain).ValidateCredentials(username, password)` 驗證，成功後建立 `NetworkCredential`
    - `Logout`：清除 `CurrentCredential`，重置 `IsAuthenticated` 為 false
    - 驗證失敗時拋出 `AuthException` 並包含失敗原因
    - _需求：1.2, 1.3, 1.4, 1.5, 1.6, 1.7_

  - [ ]* 5.2 撰寫 AuthService 單元測試
    - 測試 Windows 整合驗證成功/失敗的狀態轉換（對應屬性 1、2）
    - 測試帳號密碼驗證成功/失敗的狀態轉換
    - 測試登出後狀態重置（對應屬性 3）
    - _需求：1.5, 1.6, 1.7_

- [x] 6. 實作 Microsoft Graph API 整合（GraphClient）
  - [x] 6.1 實作 GraphClient
    - 實作 `Services/GraphClient.cs`，實作 `IGraphClient`
    - 使用 MSAL + Windows 整合驗證取得 Graph API token
    - 呼叫 `/me/calendarView?startDateTime=...&endDateTime=...` 端點
    - 將 Graph API 回應映射為 `CalendarEvent` 清單
    - HTTP 4xx/5xx 或網路逾時時拋出 `GraphApiException`（含 `StatusCode` 與錯誤描述）
    - 支援 `CancellationToken`
    - _需求：2.2, 2.5_

  - [ ]* 6.2 撰寫 GraphClient 單元測試
    - Mock `HttpClient`，測試 API 回應映射正確性（對應屬性 4）
    - 測試 HTTP 錯誤時 `GraphApiException` 包含正確狀態碼（對應屬性 5）
    - _需求：2.3, 2.5_

- [x] 7. 實作 TFS REST API 整合（TfsClient）
  - [x] 7.1 實作 TfsClient
    - 實作 `Services/TfsClient.cs`，實作 `ITfsClient`
    - `GetSprintsAsync`：使用 `WorkItemTrackingHttpClient.GetClassificationNodeAsync` 取得 Iteration 樹狀結構
    - `GetTasksBySprintAsync`：使用 WIQL 查詢指派給 `@Me` 的 Task（`System.WorkItemType = 'Task'`、`System.IterationPath`、`System.AssignedTo = @Me`）
    - `CreateTaskAsync`：使用 `JsonPatchDocument` 設定 `System.Title`（格式：`yyyy/MM/dd 會議主旨`）、`System.IterationPath`、`System.AssignedTo`
    - `TestConnectionAsync`：嘗試連線至 TFS 伺服器，回傳 bool
    - 失敗時拋出 `TfsApiException`
    - _需求：3.1, 3.2, 6.1, 6.2, 6.3, 6.4, 7.5_

  - [ ]* 7.2 撰寫 TfsClient 單元測試
    - Mock TFS SDK，測試 WIQL 查詢結果映射（對應屬性 14）
    - 測試 Work Item 建立時欄位格式正確性（對應屬性 14）
    - 測試批次建立時單一失敗不中止其餘任務（對應屬性 15）
    - _需求：6.2, 6.3, 6.4, 6.6_

- [x] 8. 實作字串相似度演算法（LevenshteinDistance + DuplicateDetector）
  - [x] 8.1 實作 LevenshteinDistance 靜態類別
    - 建立 `Services/LevenshteinDistance.cs`（靜態類別）
    - `Compute(string s1, string s2)`：標準動態規劃實作，正規化為小寫後計算編輯距離
    - `ComputeSimilarity(string s1, string s2)`：依公式 `(1 - editDistance / max(len(s1), len(s2))) * 100` 計算，兩者均為空字串時回傳 100.0
    - _需求：4.1, 4.2_

  - [ ]* 8.2 撰寫 LevenshteinDistance 屬性測試
    - **屬性 9：相似度值域在 0.0 ~ 100.0 之間**
    - **驗證需求：4.1, 4.2**

  - [ ]* 8.3 撰寫 LevenshteinDistance 屬性測試（對稱性）
    - **屬性 9：相似度計算滿足對稱性 Similarity(s1, s2) == Similarity(s2, s1)**
    - **驗證需求：4.1, 4.2**

  - [x] 8.4 實作 DuplicateDetector
    - 實作 `Services/DuplicateDetector.cs`，實作 `IDuplicateDetector`
    - `CheckDuplicate`：對單一 CalendarEvent 與所有 TfsTask 計算相似度，找出最高分，若 >= threshold 則標記為重複
    - `CheckAllDuplicates`：對所有 CalendarEvent 逐一呼叫 `CheckDuplicate`，回傳 `DuplicateCheckResult` 清單
    - 輸入為 null 或空清單時進行防禦性檢查，回傳空結果
    - _需求：4.1, 4.2, 4.3, 3.5_

  - [ ]* 8.5 撰寫 DuplicateDetector 屬性測試
    - **屬性 10：相似度 >= 80% 時 IsDuplicate 為 true，< 80% 時為 false**
    - **驗證需求：4.3, 4.4**

  - [ ]* 8.6 撰寫 DuplicateDetector 單元測試
    - 測試空 TfsTask 清單時所有 CalendarEvent 標記為非重複（需求 3.5）
    - 測試邊界值（相似度恰好等於 80%）
    - _需求：3.5, 4.3_

- [x] 9. 建立測試專案基礎結構
  - 建立 `M365TfsSync.Tests/M365TfsSync.Tests.csproj`
  - 加入 NuGet 套件：`xunit`、`xunit.runner.visualstudio`、`Moq`、`FsCheck.Xunit`
  - 建立目錄結構：`Unit/Services/`、`Unit/ViewModels/`、`Properties/`
  - 設定 FsCheck 每個屬性測試執行 100 次隨機輸入
  - _需求：（測試基礎設施）_

- [x] 10. 建立 MVVM 基礎設施（ViewModelBase、RelayCommand）
  - [x] 10.1 實作 ViewModelBase
    - 實作 `ViewModels/ViewModelBase.cs`，繼承 `INotifyPropertyChanged`
    - 實作 `OnPropertyChanged([CallerMemberName])` 與 `SetProperty<T>` 輔助方法
    - _需求：5.3_

  - [x] 10.2 實作 RelayCommand
    - 建立 `ViewModels/RelayCommand.cs`，實作 `ICommand`
    - 支援 `Action execute` 與 `Func<bool> canExecute` 建構子
    - 實作 `RaiseCanExecuteChanged()` 方法
    - _需求：1.1, 2.7, 5.4, 5.5_

- [x] 11. 實作 CalendarEventViewModel
  - [x] 11.1 實作 CalendarEventViewModel
    - 實作 `ViewModels/CalendarEventViewModel.cs`，繼承 `ViewModelBase`
    - 屬性：`Event`（CalendarEvent）、`IsSelected`（bool）、`IsDuplicate`（bool）、`DuplicateWarning`（string）
    - 計算屬性 `IsSelectable`：回傳 `!IsDuplicate`，`IsDuplicate` 變更時觸發 `IsSelectable` 的 PropertyChanged
    - _需求：4.5, 4.6_

  - [ ]* 11.2 撰寫 CalendarEventViewModel 屬性測試
    - **屬性 11：IsSelectable 恆等於 !IsDuplicate**
    - **驗證需求：4.5, 4.6**

- [x] 12. 實作 SettingsViewModel
  - [x] 12.1 實作 SettingsViewModel
    - 實作 `ViewModels/SettingsViewModel.cs`，繼承 `ViewModelBase`
    - 屬性：`TfsServerUrl`、`TfsProjectName`、`AdDomain`
    - `SaveCommand`：呼叫 `SettingsService.Save`，關閉視窗
    - `TestConnectionCommand`：呼叫 `TfsClient.TestConnectionAsync`，顯示連線結果
    - `CancelCommand`：關閉視窗不儲存
    - 建構子注入 `SettingsService`、`ITfsClient`
    - _需求：7.1, 7.5_

- [x] 13. 實作 MainViewModel
  - [x] 13.1 實作 MainViewModel 核心屬性與驗證邏輯
    - 實作 `ViewModels/MainViewModel.cs`，繼承 `ViewModelBase`
    - 宣告所有屬性：`AuthMode`、`Username`、`Password`、`IsAuthenticated`、`LoggedInUser`、`StartDate`、`EndDate`、`CalendarEvents`、`Sprints`、`SelectedSprint`、`IsLoading`、`StatusMessage`
    - 計算屬性 `CanConfirm`：`CalendarEvents` 中有 `IsSelected=true` 的項目且 `SelectedSprint != null`
    - 日期驗證邏輯：`StartDate`、`EndDate` 任一為 null 或 `EndDate < StartDate` 時 `FetchEventsCommand.CanExecute` 為 false
    - 建構子注入所有 Service 介面
    - _需求：2.7, 2.8, 5.4, 5.5_

  - [ ]* 13.2 撰寫 MainViewModel 日期驗證屬性測試
    - **屬性 6：日期驗證規則（EndDate < StartDate 時 CanExecute 為 false）**
    - **驗證需求：2.7, 2.8**

  - [x] 13.3 實作 LoginCommand 與 LogoutCommand
    - `LoginCommand`：依 `AuthMode` 呼叫對應的 `IAuthService` 方法，成功後更新 `IsAuthenticated`、`LoggedInUser`，失敗後更新 `StatusMessage`
    - `LoginCommand.CanExecute`：`TfsServerUrl` 與 `TfsProjectName` 均不為空，且未在載入中
    - `LogoutCommand`：呼叫 `IAuthService.Logout`，重置所有狀態
    - _需求：1.1, 1.2, 1.3, 1.4, 1.5, 1.6, 1.7, 7.4_

  - [ ]* 13.4 撰寫 MainViewModel 驗證狀態屬性測試
    - **屬性 1：驗證成功後 IsAuthenticated 為 true 且 Command CanExecute 正確**
    - **驗證需求：1.5**

  - [ ]* 13.5 撰寫 MainViewModel 驗證失敗屬性測試
    - **屬性 2：驗證失敗後 IsAuthenticated 仍為 false，StatusMessage 包含失敗原因**
    - **驗證需求：1.6**

  - [x] 13.6 實作 FetchEventsCommand
    - 呼叫 `IGraphClient.GetCalendarEventsAsync`，將結果映射為 `CalendarEventViewModel` 清單填入 `CalendarEvents`
    - 呼叫前設定 `IsLoading = true`，完成後設定 `IsLoading = false`
    - 無結果時更新 `StatusMessage` 為「查詢區間內無會議」
    - 失敗時捕捉 `GraphApiException`，更新 `StatusMessage` 包含 HTTP 狀態碼與錯誤描述
    - 取得結果後若 `SelectedSprint` 不為 null，立即觸發重複比對
    - _需求：2.2, 2.3, 2.4, 2.5, 2.6_

  - [x] 13.7 實作 Sprint 載入與選擇邏輯
    - 驗證成功後呼叫 `ITfsClient.GetSprintsAsync` 填入 `Sprints`
    - `SelectedSprint` 變更時呼叫 `ITfsClient.GetTasksBySprintAsync`，取得後呼叫 `IDuplicateDetector.CheckAllDuplicates` 更新所有 `CalendarEventViewModel` 的 `IsDuplicate` 與 `DuplicateWarning`
    - 載入中設定 `IsLoading = true`，完成後設定 `IsLoading = false`
    - _需求：3.1, 3.2, 3.3, 3.4, 3.6, 4.7, 4.8_

  - [x] 13.8 實作 SelectAllCommand、DeselectAllCommand 與 ConfirmCommand
    - `SelectAllCommand`：將所有 `IsSelectable = true` 的 `CalendarEventViewModel.IsSelected` 設為 true
    - `DeselectAllCommand`：將所有 `CalendarEventViewModel.IsSelected` 設為 false
    - `ConfirmCommand`：逐一呼叫 `ITfsClient.CreateTaskAsync` 建立 TfsTask，採「繼續執行」策略，完成後顯示成功數量與失敗項目摘要，並重新載入 Sprint TfsTask 清單
    - `ConfirmCommand.CanExecute`：等同 `CanConfirm`
    - _需求：5.2, 5.3, 5.4, 5.5, 6.1, 6.5, 6.6, 6.7, 6.8_

  - [ ]* 13.9 撰寫 MainViewModel 載入中狀態屬性測試
    - **屬性 7：IsLoading 為 true 時所有觸發 API 的 Command CanExecute 為 false**
    - **驗證需求：2.6, 3.6, 6.7**

  - [ ]* 13.10 撰寫 MainViewModel 確定按鈕啟用條件屬性測試
    - **屬性 13：ConfirmCommand.CanExecute 當且僅當有選取項目且 SelectedSprint 不為 null**
    - **驗證需求：5.4, 5.5**

  - [ ]* 13.11 撰寫 MainViewModel 必要設定缺失屬性測試
    - **屬性 17：TfsServerUrl 或 TfsProjectName 為空時 LoginCommand.CanExecute 為 false**
    - **驗證需求：7.4**

- [x] 14. 實作 Converter 與 UI 輔助元件
  - [x] 14.1 實作 BoolToVisibilityConverter
    - 實作 `Converters/BoolToVisibilityConverter.cs`，實作 `IValueConverter`
    - `true` → `Visibility.Visible`，`false` → `Visibility.Collapsed`
    - 支援 `ConverterParameter="Inverse"` 反轉邏輯
    - _需求：2.6, 3.6, 6.7_

- [x] 15. 實作設定視窗 UI（SettingsWindow.xaml）
  - [x] 15.1 建立 SettingsWindow.xaml
    - 建立 `Views/SettingsWindow.xaml`，DataContext 綁定 `SettingsViewModel`
    - 輸入欄位：TFS 伺服器 URL、TFS 專案名稱、AD 網域名稱（TextBox 雙向綁定）
    - 按鈕：「測試連線」（綁定 `TestConnectionCommand`）、「儲存」（綁定 `SaveCommand`）、「取消」（綁定 `CancelCommand`）
    - _需求：7.1, 7.5_

- [x] 16. 實作主視窗 UI（MainWindow.xaml）
  - [x] 16.1 建立 MainWindow.xaml 身分驗證區
    - 建立 `Views/MainWindow.xaml`，DataContext 綁定 `MainViewModel`
    - 驗證方式 RadioButton（`WindowsIntegrated` / `UsernamePassword`）
    - 帳號/密碼輸入欄位（依 `AuthMode` 顯示/隱藏，使用 `BoolToVisibilityConverter`）
    - 「登入」按鈕（綁定 `LoginCommand`）、「登出」按鈕（綁定 `LogoutCommand`）
    - 已登入使用者名稱顯示（綁定 `LoggedInUser`）
    - _需求：1.1, 1.2, 1.3, 1.5_

  - [x] 16.2 建立 MainWindow.xaml 查詢條件區
    - 開始日期 / 結束日期 DatePicker（雙向綁定 `StartDate`、`EndDate`）
    - 「取得會議」按鈕（綁定 `FetchEventsCommand`）
    - Sprint ComboBox（`ItemsSource` 綁定 `Sprints`，`SelectedItem` 綁定 `SelectedSprint`）
    - 日期驗證錯誤訊息顯示（綁定 `StatusMessage`，使用 Visibility 控制）
    - _需求：2.1, 2.7, 2.8, 3.1_

  - [x] 16.3 建立 MainWindow.xaml 會議清單區與 DataGrid
    - DataGrid 欄位：Checkbox（綁定 `IsSelected`，`IsEnabled` 綁定 `IsSelectable`）、會議主旨、開始時間（格式 `yyyy/MM/dd HH:mm`）、結束時間（格式 `yyyy/MM/dd HH:mm`）、重複警告訊息（綁定 `DuplicateWarning`）
    - 「全選」按鈕（綁定 `SelectAllCommand`）、「全部取消」按鈕（綁定 `DeselectAllCommand`）
    - 載入中指示器（綁定 `IsLoading`，使用 `BoolToVisibilityConverter`）
    - 無資料提示訊息（`CalendarEvents` 為空時顯示）
    - 「確定（建立 Task）」按鈕（綁定 `ConfirmCommand`）
    - 「設定」按鈕（綁定 `OpenSettingsCommand`）
    - _需求：5.1, 5.2, 5.3, 5.4, 5.5, 2.4, 2.6_

- [x] 17. 整合 DI 容器與應用程式進入點
  - [x] 17.1 完成 App.xaml.cs DI 設定
    - 在 `App.xaml.cs` 中設定 `Microsoft.Extensions.DependencyInjection` 容器
    - 註冊所有 Service（`SettingsService`、`AuthService`、`GraphClient`、`TfsClient`、`DuplicateDetector`）
    - 註冊所有 ViewModel（`MainViewModel`、`SettingsViewModel`）
    - 啟動時載入設定（`SettingsService.Load()`），注入至各 Service
    - 建立並顯示 `MainWindow`
    - _需求：7.3_

- [x] 18. 最終檢查點 - 確保所有測試通過
  - 確保所有測試通過，如有問題請向使用者提問。

## 備註

- 標記 `*` 的子任務為選用，可跳過以加速 MVP 開發
- 每個任務均參照具體需求條款以確保可追溯性
- 屬性測試使用 FsCheck，每個屬性預設執行 100 次隨機輸入
- 單元測試使用 xUnit + Moq
- 屬性測試標籤格式：`// Feature: m365-tfs-calendar-sync, Property {編號}: {屬性描述}`
