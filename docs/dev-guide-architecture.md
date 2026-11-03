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
├── Helpers/         # 工具類別（見下方 Helpers 說明）
└── Resources/       # CoreStrings 多語系資源
```

### Helpers

| 類別 | 用途 |
|------|------|
| `TypeNormalizer` | 欄位型別正規化與比對（見[型別正規化](#型別正規化)） |
| `DefaultValueNormalizer` | 預設值正規化與比對 |
| `SqlTextScanner` | SQL 文字掃描：遮蔽註解／字串／引號識別項／dollar-quote（`Mask`）、以分號切割陳述式、括號配對、偵測漏分號（`FindMissingTerminator`，`CREATE [OR REPLACE] RULE` 不檢查） |
| `IndexExpressionNormalizer` | 索引欄位項目解析（欄位名稱 vs. 運算式）與運算式／WHERE 條件片段的正規化比較 |
| `IndexDefinitionComparer` | 比對兩個 `IndexDefinition` 是否相同（唯一性、存取方法、欄位與排序、INCLUDE、WHERE），`SchemaDiffer` 與 `DeployOrchestrator` 共用 |

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
    public List<ForeignKeyDefinition> ForeignKeys { get; set; }
}
```

### IndexDefinition（索引定義）

```csharp
public class IndexDefinition
{
    public string Name { get; set; }
    public List<IndexColumn> Columns { get; set; }     // IndexColumn：Name、IsDescending、IsExpression（運算式／opclass／COLLATE）
    public bool IsUnique { get; set; }
    public string Method { get; set; }                 // 存取方法（btree、gin、gist…），小寫
    public List<string> IncludeColumns { get; set; }   // INCLUDE 子句的非鍵欄位
    public string? Predicate { get; set; }             // partial index 的 WHERE 條件（不含 WHERE）
    public string? Definition { get; set; }            // SQL 檔中的原始 CREATE INDEX（部署時優先原樣執行，CONCURRENTLY 會被移除）
}
```

### SchemaChange（變更記錄）

```csharp
public class SchemaChange
{
    public ChangeType Type { get; set; }
    public string EntityName { get; set; }                 // 資料表或 View 名稱
    public string? ColumnName { get; set; }                // 欄位名稱；索引／外鍵變更時為索引或約束名稱
    public string Description { get; set; }
    public string? CautionMessage { get; set; }            // 非 null 代表高風險操作
    public string? Sql { get; set; }
    public List<string>? CascadeDropObjects { get; set; }  // Pre-Drop 的 DROP VIEW ... CASCADE 會連帶刪除、且不在 schema 目錄中的物件（顯示名稱）
    public string? BlockerMessage { get; set; }            // 非 null 代表此變更阻擋整份計畫
}
```

### DeployPlan（部署計畫）

```csharp
public class DeployPlan
{
    public List<DeployGroup> Groups { get; set; }  // 依群組分組的變更
    public List<SchemaChange> Cautions { get; }    // 高風險變更清單
    public List<SchemaChange> Blockers { get; }    // 阻擋項目：有任何一筆時整份計畫不得執行
    public bool IsBlocked { get; }                 // Blockers.Count > 0
    public int TotalStatements { get; }
    public bool HasChanges { get; }
}
```

### CASCADE 刪除的阻擋判斷

`ReplaceView` 會在資料表變更前執行 `DROP VIEW ... CASCADE`（`Drop Views (Pre-Alter)` 群組）。
`SchemaIntrospector.GetViewDependentsAsync` 回傳結構化的 `DependentObject(Schema, Name, Kind)`，
Orchestrator 以 `(Schema == "public", Name)` 比對 schema 目錄中的 View，其餘物件記在 `CascadeDropObjects`。

是否阻擋由 `CascadeDropPolicy`（`Services/CascadeDropPolicy.cs`）統一判斷，分析與執行共用：

- `AnalyzeAsync`：未放行（`DeployOptions.AllowCascadeDrop = false`）→ 移到 `Blockers` 並填入 `BlockerMessage`；放行 → 維持 Caution。
- `ExecuteAsync`：以**執行當下**的設定重新判斷（不只看分析時的 `IsBlocked`），未放行就在連線前拋出 `InvalidOperationException`，不執行任何群組。
  例如 WPF 先勾選放行再分析、之後取消勾選才部署，仍會被擋下。


## 變更類型（ChangeType）

所有變更類型均會自動執行。高風險操作（`CautionMessage != null`）會在 CLI 顯示警告，在 WPF 中以橘色標示：

| 類型 | 說明 | 高風險 |
|------|------|:------:|
| `CreateTable` | 建立新資料表 | |
| `AddColumn` | 新增欄位 | |
| `AlterColumnType` | 變更欄位型別 | 型別縮減時 ✓ |
| `AlterColumnNullable` | 變更 Nullable | 加入 NOT NULL 無預設值時 ✓ |
| `AlterColumnDefault` | 變更預設值 | |
| `AlterTableComment` | 變更資料表註解（`COMMENT ON TABLE`） | |
| `AlterColumnComment` | 變更欄位註解（`COMMENT ON COLUMN`） | |
| `CreateIndex` | 建立索引 | 在既有資料表建立 UNIQUE 索引時 ✓ |
| `RecreateIndex` | 重建索引（DROP + CREATE） | 在既有資料表建立 UNIQUE 索引時 ✓ |
| `DropIndex` | 刪除索引 | |
| `DropColumn` | 刪除欄位 | ✓ |
| `RecreatePrimaryKey` | 重建主鍵 | ✓ |
| `CreateView` | 建立視圖（DB 中尚不存在） | |
| `ReplaceView` | 取代視圖：欄位改名／刪除／改順序／中間插入，或依賴的資料表欄位要變更型別／刪除（Pre-Drop 後重建） | CASCADE 會刪除 schema 目錄外物件時 ✓（未放行則為阻擋項目） |
| `CreateForeignKey` | 新增外鍵約束 | |
| `DropForeignKey` | 刪除外鍵約束 | ✓ |
| `RecreateForeignKey` | 重建外鍵約束 | ✓ |
| `ExtendView` | 擴充視圖：只在尾端新增欄位，直接 `CREATE OR REPLACE VIEW`，不 DROP | |

> `ChangeType` 新增成員一律加在 enum 最後，避免改變既有成員的數值。

## PathSettings 路徑結構

`schema` 與 `initData` 均為使用者提供的**絕對路徑**，由 CLI 參數（`--schema` / `--init-data`）、config 檔或 WPF 資料夾選擇器指定。不支援相對路徑。

```
{schema}/               # 絕對路徑，例如 C:\Test\Script\Schema
├── Tables/             # *.sql → 資料表定義
├── Views/              # *.sql → View 定義
├── Functions/          # *.sql → Function 定義
├── Procedures/         # *.sql → Stored Procedure 定義
└── Sequences/          # *.sql → Sequence 定義

{initData}/             # 絕對路徑，例如 C:\Test\Script\InitData
└── *.sql               # MERGE 陳述式（種子資料）
```

檔案類型由 `SqlSchemaDetector` 根據 SQL 檔案內容（`CREATE TABLE`、`CREATE VIEW` 等關鍵字）自動識別，與資料夾名稱無關。子目錄結構僅為組織慣例。

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
    ├── SettingsMerger.cs       # 合併設定檔 + CLI 參數
    └── PlanBlockerFormatter.cs # deploy / diff 共用的 [BLOCKED] 區塊與 Total 統計列
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

## 索引正規化

SQL 檔與 `pg_catalog` 對同一個索引的寫法可能不同（例如 `"Status" IN ('a', 'b')` 會被 PostgreSQL 改寫為
`("Status")::text = ANY (ARRAY[...])`、運算式索引的括號與型別轉換）。若直接比對文字，每次部署都會誤判為 `RecreateIndex`。

`DeployOrchestrator.AnalyzeAsync` 會先以 `IndexDefinitionComparer` 比對；對於仍不相同、且含 WHERE 條件或運算式欄位的同名索引，
呼叫 `SchemaIntrospector.CanonicalizeIndexesAsync` 請 PostgreSQL 正規化：

1. 開啟交易，對每個資料表以 `CREATE TEMP TABLE ... (LIKE public."T")` 建立暫存表（各自在獨立 savepoint 中建立，
   後續某個索引失敗時不會連帶回滾暫存表）。
2. 在暫存表上建立同樣的索引（每個索引一個 savepoint），讀回 `pg_index` 正規化後的欄位、INCLUDE 與 WHERE 條件，覆寫期望定義後再比對。
3. 結束時**回滾整個交易**，不留下任何物件。

注意事項：

- 這發生在分析階段，所以 `--dry-run`、`diff` 與 WPF 的「分析」也需要資料庫的 `TEMP` 權限。
- 無 `TEMP` 權限、或索引引用了 DB 中尚不存在的欄位時，該索引維持 SQL 檔原文比對（可能顯示為 `RecreateIndex`），不影響部署。

## 相關文件

- [開發環境建置](dev-guide-getting-started.md)
- [多語系開發指引](dev-guide-i18n.md)
