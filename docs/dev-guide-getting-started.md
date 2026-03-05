# 開發環境建置指引

## 前置需求

| 工具 | 版本 | 說明 |
|------|------|------|
| .NET SDK | 10.0+ | 主要開發框架 |
| Visual Studio / Rider | 2022+ | IDE（VS Code 亦可） |
| PostgreSQL | 16+ | 執行整合測試所需 |
| Git | 任意版本 | 版本控制 |

## 取得原始碼

```bash
git clone https://github.com/your-org/PostgresDeployer.git
cd PostgresDeployer
```

## 方案結構

```
PostgresDeployer.sln
├── src/
│   ├── PostgresDeployer.Core/        # 核心邏輯（Class Library, .NET 10.0）
│   ├── PostgresDeployer.Cli/         # CLI 工具（Console App, .NET 10.0）
│   └── PostgresDeployer.Wpf/         # 桌面應用程式（WPF, .NET 10.0-windows）
├── test/
│   └── PostgresDeployer.Core.Tests/  # 單元測試與整合測試（xUnit）
├── docs/                             # 開發者文件
└── samples/                          # 範例 SQL Schema 檔案
```

## 建置

### 整個方案

```bash
dotnet build
```

### 個別專案

```bash
dotnet build src/PostgresDeployer.Core/
dotnet build src/PostgresDeployer.Cli/
dotnet build src/PostgresDeployer.Wpf/
```

## 執行

### CLI

```bash
dotnet run --project src/PostgresDeployer.Cli -- --help
dotnet run --project src/PostgresDeployer.Cli -- deploy --config your-config.json --dry-run
```

### WPF

```bash
dotnet run --project src/PostgresDeployer.Wpf/
```

或直接在 Visual Studio 中按 F5 執行。

## 執行測試

### 單元測試（不需要 DB）

```bash
dotnet test test/PostgresDeployer.Core.Tests/ --filter "Category!=Integration"
```

### 整合測試（需要 PostgreSQL）

整合測試會連接到本機的 `PostgresDeployer_Test` 資料庫。執行前請確認：

1. PostgreSQL 服務已啟動
2. 建立測試資料庫：

```sql
CREATE DATABASE "PostgresDeployer_Test";
```

3. 在 `test/PostgresDeployer.Core.Tests/` 中確認連線字串（若有 `appsettings.Test.json` 或直接寫在測試程式中）

執行所有測試：

```bash
dotnet test test/PostgresDeployer.Core.Tests/
```

## 發布

### CLI（Self-contained，單一執行檔）

```bash
dotnet publish src/PostgresDeployer.Cli/ \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/cli
```

### WPF（Self-contained）

```bash
dotnet publish src/PostgresDeployer.Wpf/ \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/wpf
```

## VS Code 設定

`.vscode/` 目錄已包含：

- `launch.json` — 除錯設定（CLI 與 WPF）
- `tasks.json` — 建置任務

開啟專案後直接按 `F5` 即可啟動除錯。

## 相關文件

- [架構說明](dev-guide-architecture.md)
- [多語系開發指引](dev-guide-i18n.md)
