# 需求文件

## 簡介

本專案為一款在 Windows 本機執行的桌面工具，用於將 Microsoft 365 (M365) 行事曆中的會議自動同步為 Team Foundation Server (TFS / Azure DevOps) Kanban 看板上的 Task。目標是自動化工作追蹤流程，消除人工重複登錄的時間成本。

使用者透過公司內部 Windows AD 完成身分驗證後，可選取時間區間取得 M365 會議清單，並選擇目標 TFS Sprint，系統將自動比對重複任務並允許使用者選擇性地將會議建立為 TFS Task。

---

## 詞彙表

- **Application**：本桌面工具應用程式本體（.NET 8 WPF 單一執行檔）
- **User**：使用本工具的企業員工
- **AuthService**：負責透過公司內部 Windows AD 進行身分驗證的模組，支援 Windows 整合驗證（NTLM / Kerberos）與帳號密碼驗證兩種方式
- **GraphClient**：負責呼叫 Microsoft Graph API 以讀取 M365 行事曆資料的模組
- **TfsClient**：負責呼叫 TFS REST API / Microsoft.TeamFoundationServer.Client 以讀取及建立 Azure DevOps Work Item 的模組
- **DuplicateDetector**：負責執行字串相似度比對演算法（Levenshtein Distance）的模組
- **CalendarEvent**：從 M365 行事曆取得的單一會議項目，包含主旨、開始時間、結束時間
- **TfsTask**：TFS Sprint 中的單一 Work Item（類型為 Task）
- **Sprint**：TFS / Azure DevOps 中的一個迭代週期（Iteration）
- **SimilarityThreshold**：判定兩個字串為重複的相似度門檻值，預設為 80%
- **MainViewModel**：WPF MVVM 架構中的主要 ViewModel，負責協調所有 UI 狀態與業務邏輯
- **DataGrid**：WPF UI 中顯示會議清單的表格元件

---

## 需求

### 需求 1：身分驗證

**使用者故事：** 身為一名企業員工，我希望透過公司內部 Windows AD 帳號登入，以便安全地存取 M365 行事曆與 TFS 資源。

#### 驗收標準

1. WHEN 使用者啟動 Application，THE Application SHALL 顯示驗證選項，並在使用者尚未完成驗證前禁用所有需要授權的功能。
2. THE Application SHALL 提供「Windows 整合驗證」選項，WHEN 使用者選擇此選項，THE AuthService SHALL 透過 `CredentialCache.DefaultNetworkCredentials` 取得目前登入 Windows 的帳號憑證，無需使用者輸入帳號密碼。
3. THE Application SHALL 提供「帳號密碼驗證」選項，WHEN 使用者選擇此選項，THE Application SHALL 顯示 AD 帳號與密碼輸入欄位供使用者填寫。
4. WHEN 使用者選擇「帳號密碼驗證」並點擊登入按鈕，THE AuthService SHALL 透過 `PrincipalContext.ValidateCredentials()` 驗證使用者輸入的 AD 帳號與密碼。
5. WHEN AD 驗證成功，THE AuthService SHALL 儲存已驗證的 `NetworkCredential`，且 THE Application SHALL 顯示已登入使用者的帳號名稱。
6. IF AD 驗證失敗，THEN THE AuthService SHALL 顯示包含失敗原因的錯誤訊息，並允許使用者重新嘗試登入。
7. WHEN 使用者點擊登出按鈕，THE AuthService SHALL 清除所有已儲存的憑證，並將 Application 重置為未登入狀態。

---

### 需求 2：設定時間區間與取得 M365 會議

**使用者故事：** 身為一名企業員工，我希望指定時間區間後取得該區間內的 M365 會議清單，以便選擇要同步的會議。

#### 驗收標準

1. THE Application SHALL 提供開始日期與結束日期的日期選擇器（DatePicker），供使用者設定查詢時間區間。
2. WHEN 使用者點擊「取得會議」按鈕，THE GraphClient SHALL 呼叫 Microsoft Graph API 的 `/me/calendarView` 端點，取得指定時間區間內的所有 CalendarEvent。
3. WHEN GraphClient 成功取得資料，THE Application SHALL 在 DataGrid 中顯示每個 CalendarEvent 的主旨、開始時間與結束時間。
4. IF 指定時間區間內沒有任何 CalendarEvent，THEN THE Application SHALL 在 DataGrid 區域顯示「查詢區間內無會議」的提示訊息。
5. IF GraphClient 呼叫 Microsoft Graph API 失敗，THEN THE Application SHALL 顯示包含 HTTP 狀態碼與錯誤描述的錯誤訊息。
6. WHILE GraphClient 正在呼叫 API，THE Application SHALL 顯示載入中指示器（Loading Indicator）並禁用「取得會議」按鈕，防止重複呼叫。
7. IF 使用者未選擇開始日期或結束日期，THEN THE Application SHALL 禁用「取得會議」按鈕。
8. IF 使用者設定的結束日期早於開始日期，THEN THE Application SHALL 顯示「結束日期不可早於開始日期」的驗證錯誤訊息。

---

### 需求 3：選擇 TFS Sprint 與取得現有任務

**使用者故事：** 身為一名企業員工，我希望從下拉選單中選擇目標 TFS Sprint，以便系統能比對現有任務並在正確的 Sprint 中建立新任務。

#### 驗收標準

1. WHEN 使用者完成 AD 驗證，THE TfsClient SHALL 呼叫 TFS REST API 取得所有可用的 Sprint 清單，並填入下拉選單（ComboBox）。
2. WHEN 使用者從下拉選單選擇一個 Sprint，THE TfsClient SHALL 呼叫 TFS REST API 取得該 Sprint 內所有指派給當前使用者（@Me）的 TfsTask 清單。
3. WHEN TfsClient 成功取得 Sprint 的 TfsTask 清單，THE DuplicateDetector SHALL 立即對已載入的 CalendarEvent 清單執行重複比對。
4. IF TfsClient 呼叫 TFS REST API 失敗，THEN THE Application SHALL 顯示包含錯誤描述的錯誤訊息，並保留下拉選單的可操作狀態。
5. IF 選擇的 Sprint 中沒有指派給當前使用者的 TfsTask，THEN THE DuplicateDetector SHALL 將所有 CalendarEvent 標記為非重複。
6. WHILE TfsClient 正在呼叫 API，THE Application SHALL 顯示載入中指示器並禁用 Sprint 下拉選單，防止重複呼叫。

---

### 需求 4：智慧重複比對

**使用者故事：** 身為一名企業員工，我希望系統自動偵測 M365 會議與 TFS 現有任務之間的重複項目，以避免建立重複的工作項目。

#### 驗收標準

1. THE DuplicateDetector SHALL 使用 Levenshtein Distance 演算法計算每個 CalendarEvent 主旨與每個 TfsTask 主旨之間的字串相似度。
2. THE DuplicateDetector SHALL 依據以下公式計算相似度百分比：`similarity = (1 - editDistance / max(len(s1), len(s2))) * 100`。
3. WHEN 任一 TfsTask 主旨與 CalendarEvent 主旨的相似度大於或等於 SimilarityThreshold（80%），THE DuplicateDetector SHALL 將該 CalendarEvent 標記為「疑似重複」。
4. WHEN CalendarEvent 被標記為「疑似重複」，THE Application SHALL 在 DataGrid 對應列顯示警告圖示與「疑似重複：[最相似的 TfsTask 主旨]」的提示文字。
5. WHEN CalendarEvent 未被標記為「疑似重複」，THE Application SHALL 在 DataGrid 對應列開放勾選框（Checkbox）供使用者選擇。
6. WHEN CalendarEvent 被標記為「疑似重複」，THE Application SHALL 將 DataGrid 對應列的勾選框設為停用（Disabled）狀態。
7. WHEN 使用者重新選擇不同的 Sprint，THE DuplicateDetector SHALL 重新執行所有 CalendarEvent 的重複比對，並更新 DataGrid 的顯示狀態。
8. FOR ALL CalendarEvent 清單，WHEN DuplicateDetector 執行比對後，每個 CalendarEvent 的重複標記狀態 SHALL 僅取決於當前選擇的 Sprint 的 TfsTask 清單，與其他 CalendarEvent 無關。

---

### 需求 5：UI 顯示與使用者互動

**使用者故事：** 身為一名企業員工，我希望透過清晰的介面查看會議清單並選擇要建立為 TFS Task 的項目，以便有效率地完成同步操作。

#### 驗收標準

1. THE Application SHALL 在 DataGrid 中為每個 CalendarEvent 顯示以下欄位：勾選框（Checkbox）、會議主旨、開始時間（格式：yyyy/MM/dd HH:mm）、結束時間（格式：yyyy/MM/dd HH:mm）、重複警告訊息。
2. THE Application SHALL 提供「全選」與「全部取消」按鈕，僅對未被標記為「疑似重複」的 CalendarEvent 生效。
3. WHEN 使用者勾選或取消勾選 DataGrid 中的項目，THE MainViewModel SHALL 即時更新已選取的 CalendarEvent 清單。
4. WHEN 已選取的 CalendarEvent 清單不為空，且使用者已選擇目標 Sprint，THE Application SHALL 啟用「確定」按鈕。
5. IF 已選取的 CalendarEvent 清單為空，或使用者尚未選擇目標 Sprint，THEN THE Application SHALL 禁用「確定」按鈕。

---

### 需求 6：建立 TFS Task

**使用者故事：** 身為一名企業員工，我希望按下確定後，系統自動在選定的 TFS Sprint 中為每個勾選的會議建立對應的 Task，以節省手動建立的時間。

#### 驗收標準

1. WHEN 使用者點擊「確定」按鈕，THE TfsClient SHALL 為每個已勾選的 CalendarEvent，在選定的 Sprint 中建立一個新的 TfsTask。
2. THE TfsClient SHALL 將新建立的 TfsTask 主旨格式設定為 `yyyy/MM/dd 會議主旨`，其中日期為 CalendarEvent 的開始日期。
3. THE TfsClient SHALL 將新建立的 TfsTask 的「指派給」欄位設定為當前已登入的使用者（@Me）。
4. THE TfsClient SHALL 將新建立的 TfsTask 的「迭代路徑（Iteration Path）」設定為使用者所選擇的 Sprint。
5. WHEN 所有已勾選的 CalendarEvent 均成功建立為 TfsTask，THE Application SHALL 顯示「已成功建立 N 個任務」的成功訊息，其中 N 為實際建立的數量。
6. IF 任一 TfsTask 建立失敗，THEN THE TfsClient SHALL 繼續嘗試建立其餘的 TfsTask，並在全部處理完畢後，顯示包含成功數量與失敗項目主旨的摘要報告。
7. WHILE TfsClient 正在建立 TfsTask，THE Application SHALL 顯示進度指示器（Progress Indicator）並禁用「確定」按鈕，防止重複提交。
8. WHEN 所有 TfsTask 建立完成，THE Application SHALL 重新執行 Sprint 的 TfsTask 查詢，以更新重複比對的基準資料。

---

### 需求 7：應用程式設定

**使用者故事：** 身為一名企業員工，我希望能設定 TFS 伺服器位址與 AD 網域資訊，以便工具能連接到正確的企業環境。

#### 驗收標準

1. THE Application SHALL 提供設定介面，允許使用者輸入並儲存以下設定項目：TFS 伺服器 URL、TFS 專案名稱（Project Name）、AD 網域名稱（AD Domain）。
2. THE Application SHALL 將使用者設定以加密方式儲存於本機使用者設定檔（例如 `%APPDATA%` 目錄下的設定檔）。
3. WHEN Application 啟動時，THE Application SHALL 自動載入上次儲存的設定。
4. IF 必要的設定項目（TFS 伺服器 URL、TFS 專案名稱）尚未完整填寫，THEN THE Application SHALL 禁用登入按鈕，並提示使用者前往設定頁面完成設定。
5. THE Application SHALL 提供「測試連線」功能，WHEN 使用者點擊後，THE TfsClient SHALL 嘗試使用當前憑證連線至設定的 TFS 伺服器，並顯示連線成功或失敗的結果。

---

### 需求 8：發布為單一執行檔

**使用者故事：** 身為一名企業員工，我希望工具以單一 .exe 檔案的形式發布，以便在無需安裝 .NET Runtime 的 Windows 環境中直接執行。

#### 驗收標準

1. THE Application SHALL 以 .NET 8 Self-Contained 模式發布，將所有相依的 .NET Runtime 與程式庫打包至單一 .exe 執行檔中。
2. THE Application SHALL 以 `win-x64` 為目標執行平台（Runtime Identifier）進行發布。
3. THE Application SHALL 啟用 PublishSingleFile 與 EnableCompressionInSingleFile 選項，以產生單一壓縮執行檔。
4. THE Application SHALL 在 Windows 10 (版本 1903 及以上) 與 Windows 11 環境中，無需預先安裝任何額外軟體即可直接執行。
