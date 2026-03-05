# 多語系（i18n）開發指引

## 架構概述

專案使用 .NET 內建的 `ResourceManager` 機制，搭配手寫 wrapper 類別，避免依賴 Visual Studio 的程式碼生成器。UI 層（WPF）與 Core 層各自維護獨立的資源檔。

| 層 | 資源類別 | resx 檔案 |
|----|---------|-----------|
| WPF UI | `Strings`（`Services/LocalizationService.cs` 包裝） | `Resources/Strings.resx`、`Resources/Strings.zh-TW.resx` |
| Core | `CoreStrings` | `Resources/CoreStrings.resx`、`Resources/CoreStrings.zh-TW.resx` |

## 執行時語系切換

`LocalizationService`（WPF 專案）是單例（`LocalizationService.Instance`），實作 `INotifyPropertyChanged`。語系切換後觸發 `PropertyChanged(Binding.IndexerName)`，所有 XAML Binding 自動更新，無需重新啟動應用程式。

```
使用者切換語系
    │
    ▼
LocalizationService.SetLanguage("zh-TW")
    │
    ▼
ResourceManager 切換 CultureInfo
    │
    ▼
PropertyChanged(Binding.IndexerName) 廣播
    │
    ▼
所有 {l:Loc Key} 與 {Binding [Key], Source=...} 自動刷新
```

## 新增語系（完整步驟）

以新增日文（`ja`）為例：

### 步驟 1：新增 WPF 資源檔

在 `src/PostgresDeployer.Wpf/Resources/` 新增 `Strings.ja.resx`：

- 複製 `Strings.resx` 的所有項目
- 將所有 Value 翻譯為日文
- `Build Action` 設為 `Embedded Resource`（在 .csproj 中確認）

### 步驟 2：新增 Core 資源檔（若有 Core 字串需要翻譯）

在 `src/PostgresDeployer.Core/Resources/` 新增 `CoreStrings.ja.resx`，同上。

### 步驟 3：在 LocalizationService 加入語系定義

編輯 `src/PostgresDeployer.Wpf/Services/LocalizationService.cs`：

```csharp
public static readonly IReadOnlyList<Language> SupportedLanguages = new List<Language>
{
    new Language("en",    "English"),
    new Language("zh-TW", "繁體中文"),
    new Language("ja",    "日本語"),   // 新增這行
};
```

### 步驟 4：重新建置

```bash
dotnet build src/PostgresDeployer.Wpf/
```

.NET 建置系統會自動將 `Strings.ja.resx` 編譯為衛星組件（`ja/PostgresDeployer.Wpf.resources.dll`）。

---

## 在 XAML 中使用多語系字串

### 一般文字（使用 LocExtension）

```xml
xmlns:l="clr-namespace:PostgresDeployer.Wpf.Extensions"

<TextBlock Text="{l:Loc Settings_Title}" />
<Button Content="{l:Loc Deploy_BtnAnalyze}" />
```

### GridViewColumn Header（使用 Binding）

`GridViewColumn.Header` 不支援 MarkupExtension，改用：

```xml
xmlns:svc="clr-namespace:PostgresDeployer.Wpf.Services"

<GridViewColumn Header="{Binding [Log_ColTime], Source={x:Static svc:LocalizationService.Instance}}" />
```

### 動態內容（ComboBox ItemsSource）

語系選項清單直接綁定 `LocalizationService.SupportedLanguages`：

```xml
<ComboBox ItemsSource="{x:Static svc:LocalizationService.SupportedLanguages}"
          DisplayMemberPath="DisplayName" />
```

---

## 在 C# / ViewModel 中使用多語系字串

```csharp
// 取得字串
string title = LocalizationService.Instance["Settings_Title"];

// 使用格式化（傳入 string.Format 參數）
string msg = string.Format(
    LocalizationService.Instance["Status_AnalyzeComplete"],
    count, cautionCount);
```

---

## 新增字串 Key（WPF 層）

1. 在 `Strings.resx`（英文）加入新 Key-Value
2. 在 `Strings.zh-TW.resx`（繁中）加入相同 Key 的中文翻譯
3. 在所有其他語言的 resx 加入翻譯（若未提供，fallback 為英文）
4. 在 XAML 使用 `{l:Loc YourNewKey}` 或在 C# 使用 `LocalizationService.Instance["YourNewKey"]`

**注意**：Key 命名規則為 `{模組}_{說明}`，例如：
- `Settings_Title`、`Settings_BtnSave`
- `Deploy_BtnAnalyze`、`Deploy_ColChangeType`
- `Log_ColTime`、`Log_ColMessage`
- `Status_Ready`、`Status_AnalyzeComplete`

---

## 新增字串 Key（Core 層）

Core 層的字串（群組名稱、狀態訊息等）存放在 `CoreStrings.resx`。

使用方式：

```csharp
// 在 Core Services 中
CoreStrings.ResourceManager.GetString("KeyName", CultureInfo.CurrentUICulture)
```

---

## 語系偏好持久化

使用者選擇的語系儲存在：

```
%APPDATA%/PostgresDeployer/appsettings.json
```

`AppSettings.Language` 屬性記錄語系代碼（如 `"zh-TW"`）。應用程式啟動時由 `LocalizationService.InitFromSystem(settings.Language)` 載入，若無設定則自動偵測 OS 語系，fallback 為英文。

---

## DeployViewModel 的群組名稱對應

Core 層的部署群組名稱為固定的中文字串（由 `DeployGroupNames` 定義）。WPF 的 `DeployViewModel` 內有 `CoreNameToGroupType` Dictionary，將這些固定字串對應到 `ChangeGroupType` enum，再由 `ChangeTypeToColorConverter` 決定顯示顏色，避免 UI 直接比對中文字串。

若新增 Core 層的群組，需同步更新 `DeployViewModel.CoreNameToGroupType`。

---

## 相關文件

- [架構說明](dev-guide-architecture.md)
- [開發環境建置](dev-guide-getting-started.md)
