# 開發環境建置指引

## 前置需求

| 工具 | 版本 | 說明 |
|------|------|------|
| .NET SDK | 10.0+ | 主要開發框架 |
| Visual Studio / Rider | 2022+ | IDE（VS Code 亦可） |
| PostgreSQL | 16+ | 執行整合測試所需（建議用 Docker，見[整合測試](#整合測試需要-postgresql建議用-docker)） |
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
│   ├── PostgresDeployer.Core.Tests/  # 單元測試與整合測試（xUnit；整合測試在 Integration/）
│   └── PostgresDeployer.Cli.Tests/   # CLI 單元測試（xUnit）
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
dotnet test PostgresDeployer.sln
```

未設定 `PGD_TEST_CONNECTION` 時，需要 PostgreSQL 的整合測試（`test/PostgresDeployer.Core.Tests/Integration/`，標記為 `[PostgresFact]`）會顯示為 **Skipped**，其餘單元測試照常執行。

### 整合測試（需要 PostgreSQL，建議用 Docker）

整合測試會以伺服器連線為每個測試建立獨立的拋棄式資料庫（名稱前綴 `pgd_it_`），測試結束後以 `DROP DATABASE ... WITH (FORCE)` 刪除。
建議使用專用的 Docker 容器，避免動到開發用資料庫：

```bash
# 1. 啟動測試用 PostgreSQL（對外 port 55432）
docker run -d --name pgd-test-pg -e POSTGRES_PASSWORD=pgdtest -p 55432:5432 postgis/postgis:18-3.6

# 2. 設定連線字串（伺服器層級，不需要 Database）並執行所有測試
#    bash
PGD_TEST_CONNECTION="Host=localhost;Port=55432;Username=postgres;Password=pgdtest" dotnet test PostgresDeployer.sln
#    PowerShell
$env:PGD_TEST_CONNECTION = "Host=localhost;Port=55432;Username=postgres;Password=pgdtest"; dotnet test PostgresDeployer.sln

# 3. 用完移除容器
docker rm -f pgd-test-pg
```

連線帳號需要 `CREATEDB` 權限。只跑整合測試可加上 `--filter "FullyQualifiedName~Integration"`。

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
