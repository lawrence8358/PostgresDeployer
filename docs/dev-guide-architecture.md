# 架構說明

## 總覽

PostgresDeployer 採用分層架構，Core 邏輯完全獨立於 UI，CLI 與 WPF 均以 Core 為基礎構建。

```
┌─────────────────────────────────────────────┐
│            使用者界面層                        │
│  ┌──────────────────┐  ┌───────────────────┐ │
│  │  PostgresDeployer│  │  PostgresDeployer │ │
│  │       .Cli       │  │       .Wpf        │ │
│  └────────┬─────────┘  └────────┬──────────┘ │
└───────────┼─────────────────────┼────────────┘
            │                     │
            ▼                     ▼
┌─────────────────────────────────────────────┐
│         PostgresDeployer.Core               │
│  Interfaces → Services → Models             │
└─────────────────────────────────────────────┘
            │
            ▼
      PostgreSQL DB（透過 Npgsql）
```

## Core 專案結構

```
src/PostgresDeployer.Core/
├── Models/          # 資料模型與 Enum
├── Interfaces/      # 服務介面（ISqlFileParser、ISchemaIntrospector 等）
├── Services/        # 介面實作
├── Helpers/         # 工具類別（TypeNormalizer、DefaultValueNormalizer）
└── Resources/       # CoreStrings 多語系資源
```

## 部署流程

完整的部署流程由六個服務協作完成：

```
SQL 定義檔
    │
    ▼
┌─────────────────┐
│  SqlFileParser  │  讀取並解析 .sql 檔案，提取 TableSchema
└────────┬────────┘
         │ 期望的 Schema
         ▼
┌─────────────────────┐      ┌──────────────────────┐
│  SchemaIntrospector │◄─────┤  PostgreSQL Database  │
└────────┬────────────┘      └──────────────────────┘
         │ 實際的 Schema
         ▼
┌─────────────────┐
│  SchemaDiffer   │  比對期望與實際 Schema，輸出 SchemaChange 清單
└────────┬────────┘
         │ SchemaChange[]
         ▼
┌─────────────────┐
│  SqlGenerator   │  將每個 SchemaChange 轉換為 ALTER/CREATE SQL
└────────┬────────┘
         │ SQL 陳述式
         ▼
┌─────────────────┐
│  DeployExecutor │  在 Transaction 中執行 SQL，處理錯誤與 Rollback
└────────┬────────┘
         │
         ▼
┌──────────────────────┐
│  DeployOrchestrator  │  協調以上所有步驟，提供 AnalyzeAsync / ExecuteAsync
└──────────────────────┘
```

## 關鍵模型

### DeploySettings（部署設定）

```csharp
public class DeploySettings
{
    public ConnectionSettings Connection { get; set; }
    public PathSettings Paths { get; set; }
    public List<string> Extensions { get; set; }
    public DeployOptions Options { get; set; }
}
```

### TableSchema（資料表結構）

```csharp
public class TableSchema
{
    public string TableName { get; set; }
    public List<ColumnDefinition> Columns { get; set; }
    public PrimaryKeyDefinition? PrimaryKey { get; set; }
    public List<IndexDefinition> Indexes { get; set; }
}
```

### SchemaChange（變更記錄）

```csharp
public class SchemaChange
{
    public ChangeType Type { get; set; }
    public string Description { get; set; }
    public string? CautionMessage { get; set; }  // 非 null 代表高風險操作
    public string Sql { get; set; }
}
```

### DeployPlan（部署計畫）

```csharp
public class DeployPlan
{
    public List<DeployGroup> Groups { get; set; }  // 依群組分組的變更
    public List<SchemaChange> Cautions { get; }    // 高風險變更清單
    public int TotalStatements { get; }
    public bool HasChanges { get; }
}
```

## 變更類型（ChangeType）

所有變更類型均會自動執行。高風險操作（`CautionMessage != null`）會在 CLI 顯示警告，在 WPF 中以橘色標示：

| 類型 | 說明 | 高風險 |
|------|------|:------:|
| `CreateTable` | 建立新資料表 | |
| `AddColumn` | 新增欄位 | |
| `AlterColumnType` | 變更欄位型別 | 型別縮減時 ✓ |
| `AlterColumnNullable` | 變更 Nullable | 加入 NOT NULL 無預設值時 ✓ |
| `AlterColumnDefault` | 變更預設值 | |
| `CreateIndex` | 建立索引 | |
| `RecreateIndex` | 重建索引 | |
| `DropIndex` | 刪除索引 | |
| `DropColumn` | 刪除欄位 | ✓ |
| `RecreatePrimaryKey` | 重建主鍵 | ✓ |

## PathSettings 路徑結構

```
{basePath}/
├── {schema}/               # 預設 "Schema"
│   ├── Tables/             # *.sql → 資料表定義
│   ├── Views/              # *.sql → View 定義
│   ├── Functions/          # *.sql → Function 定義
│   ├── Procedures/         # *.sql → Stored Procedure 定義
│   └── Sequences/          # *.sql → Sequence 定義
└── {initData}/             # 預設 "InitData"
    └── *.sql               # MERGE 陳述式（種子資料）
```

子目錄名稱（Tables/Views/Functions/等）由 `SqlSchemaDetector` 根據資料夾名稱自動識別檔案類型。

## CLI 專案結構

```
src/PostgresDeployer.Cli/
├── Program.cs           # System.CommandLine 根命令與子命令定義
├── Commands/
│   ├── DeployCommand.cs
│   ├── DiffCommand.cs
│   ├── TestConnectionCommand.cs
│   └── InitCommand.cs
└── Helpers/
    └── SettingsMerger.cs  # 合併設定檔 + CLI 參數
```

## WPF 專案結構

```
src/PostgresDeployer.Wpf/
├── App.xaml / App.xaml.cs         # DI 容器設定、語系初始化
├── MainWindow.xaml / .cs          # TabControl 主視窗
├── Views/                         # SettingsView、DeployView、LogView
├── ViewModels/                    # MainViewModel、SettingsViewModel、DeployViewModel、LogViewModel
├── Services/                      # AppSettingsService、LocalizationService、WpfLoggerProvider 等
├── Converters/                    # ChangeTypeToColorConverter、BoolToVisibilityConverter 等
├── Extensions/                    # LocExtension（XAML 多語系 Markup Extension）
└── Resources/                     # Strings.resx、Strings.zh-TW.resx
```

WPF 採用 MVVM 架構（CommunityToolkit.Mvvm），依賴注入由 `Microsoft.Extensions.DependencyInjection` 提供。

## 型別正規化

`TypeNormalizer`（`Helpers/TypeNormalizer.cs`）負責統一處理 PostgreSQL 型別的比對：

- DB 回傳值（如 `character varying(200)`）正規化為 SQL 定義格式（`VARCHAR(200)`）
- 處理同義詞：`integer` ↔ `INT`、`boolean` ↔ `BOOLEAN` 等
- 識別型別縮減（長度/精度減少），標示為高風險變更

`DefaultValueNormalizer`（`Helpers/DefaultValueNormalizer.cs`）處理預設值的比對：

- 清除 DB 回傳的 `::type` 型別後綴
- 正規化布林值（`false` → `FALSE`）、函式（`now()` → `NOW()`）
- 忽略 `nextval()` Sequence 與 IDENTITY 欄位預設值（不視為差異）

## 相關文件

- [開發環境建置](dev-guide-getting-started.md)
- [多語系開發指引](dev-guide-i18n.md)
