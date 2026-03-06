[English](README.md) | [繁體中文](README.zh-TW.md)

# PostgresDeployer

![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16+-336791?logo=postgresql)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows)
![License](https://img.shields.io/badge/License-MIT-green.svg)

A general-purpose PostgreSQL schema deployment tool — define your database schema in SQL files, and let PostgresDeployer automatically detect and apply the differences each time you deploy.

Inspired by MSSQL SSDT/DACPAC, built for PostgreSQL.

## Table of Contents

- [Why PostgresDeployer?](#why-postgresdeployer)
- [Features](#features)
- [Screenshots](#screenshots)
- [Getting Started](#getting-started)
- [Quick Start: Sample Project](#quick-start-sample-project)
- [CLI Usage](#cli-usage)
- [WPF Desktop Application](#wpf-desktop-application)
- [Supported Schema Changes](#supported-schema-changes)
- [Contributing](#contributing)
- [License](#license)

## Why PostgresDeployer?

Visual Studio's **SQL Server Database Projects** offer an excellent developer experience for managing database schemas — every database object lives in its own file, changes are tracked in Git, and deployment automatically computes the diff. The problem is it only works with **Microsoft SQL Server**.

Migration-based tools like **Flyway** and **Liquibase** support PostgreSQL and are widely used, but they take a fundamentally different approach: each change is a versioned migration script, so the current definition of a table is scattered across tens or hundreds of files.

PostgresDeployer takes the **desired state** approach:

- **Single Truth:** Each table is a single `.sql` file that always reflects the **complete, current structure**.
- **Clear History:** Git history on that file shows the complete evolution of the table over time.
- **Auto Diff:** PostgresDeployer compares your **desired schema** with the **actual database schema**, generating and applying only the incremental changes needed.
- **Idempotent:** Running multiple times produces the same result. No migration scripts to maintain.

## Features

- Automatic schema diff detection (tables, columns, types, nullability, defaults, indexes, primary keys)
- Safe, transaction-based deployment with automatic rollback on failure
- Dry-run mode to preview changes without applying them
- Caution warnings for potentially destructive operations
- Seed data support via MERGE statements
- PostgreSQL extension management (e.g., `pgcrypto`)
- CLI for scripted and CI/CD deployments
- WPF desktop application for interactive use
- Runtime language switching (English / 繁體中文)

## Screenshots

### CLI Application

![CLI Demo](docs/screenshots/demo_cli.png)

### WPF Application

#### Settings
![Settings](docs/screenshots/demo_wpf_settings.png)

#### Deploy
![Deploy](docs/screenshots/demo_wpf_deploy.png)

#### Log
![Log](docs/screenshots/demo_wpf_log.png)

## Getting Started

### Prerequisites

- .NET 10.0 Runtime
- PostgreSQL 16+

### Download

Download the latest release from the [Releases](https://github.com/lawrence8358/PostgresDeployer/releases) page:

- `cli-win-x64.zip` — CLI (command-line)
- `wpf-win-x64.zip` — WPF desktop application

## Quick Start: Sample Project

The [`samples/`](samples/) directory contains a complete example database schema ready for deployment:

```
samples/
├── Schema/
│   ├── Tables/       # Table definitions (UUID PK, IDENTITY column, compound PK examples)
│   ├── Views/        # View definitions (complex JOIN queries)
│   ├── Functions/    # Function definitions (password hash, active user queries)
│   ├── Procedures/   # Stored procedures (user lock / reset failure count)
│   └── Sequences/    # Sequence definitions
└── InitData/         # Seed data (MERGE statements for reference data)
```

To deploy the sample schema:

```bash
# 1. Generate a config file for the samples
pgdeploy init --output samples/PostgresDeployer.json

# 2. Edit the connection details in samples/PostgresDeployer.json

# 3. Deploy
pgdeploy deploy --config samples/PostgresDeployer.json
```

## CLI Usage

### Common Commands

```bash
# Generate a sample config file
pgdeploy init

# Test the database connection
pgdeploy test-connection --config PostgresDeployer.json

# Preview schema differences (no changes applied)
pgdeploy diff --config PostgresDeployer.json

# Deploy schema changes (with confirmation prompt)
pgdeploy deploy --config PostgresDeployer.json

# Dry run — preview changes without applying
pgdeploy deploy --config PostgresDeployer.json --dry-run
```

### Advanced Examples

```bash
# Deploy without prompting for confirmation
pgdeploy deploy --config PostgresDeployer.json --yes

# Deploy only tables (skip views and seed data)
pgdeploy deploy --config PostgresDeployer.json --only tables

# Write a deployment log to file
pgdeploy deploy --config PostgresDeployer.json --log-file deploy.log
```

### Command Reference

| Command | Description |
|---------|-------------|
| `deploy` | Detect schema differences and apply changes |
| `diff` | Analyze differences and output a report (no changes applied) |
| `test-connection` | Test database connection |
| `init` | Generate a sample config file |

**Common Options**
All commands accept connection parameters that override the config file:

| Option | Alias | Description |
|--------|-------|-------------|
| `--config` | `-c` | Path to JSON config file |
| `--host` | `-H` | PostgreSQL host |
| `--port` | `-P` | PostgreSQL port |
| `--database` | `-d` | Database name |
| `--username` | `-u` | Username |
| `--password` | `-p` | Password |

**`deploy` Specific Options**

| Option | Description |
|--------|-------------|
| `--dry-run` | Preview changes only, do not execute |
| `--yes` | Skip confirmation prompt |
| `--only` | Deploy only `tables`, `views`, `seeds`, or `all` (default: `all`) |
| `--log-file` | Write deployment log to file |
| `--stop-on-error` | Stop deployment if any group fails (default: true) |
| `--schema` | Schema DDL directory path (contains Tables/, Views/, Functions/, etc.) |
| `--init-data` | Seed data directory path |
| `--extensions` | PostgreSQL extensions to ensure (comma-separated) |

**Run Script Output**
After each successful deployment, PostgresDeployer automatically saves the full SQL executed in this run to a timestamped file under the `RunScripts/` directory beside the executable:

```
RunScripts/
└── 20260304152233.sql
```

This includes all SQL statements in execution order — extensions, table changes, views, seed data, etc.

**`diff` Specific Options**

| Option | Description |
|--------|-------------|
| `--output` / `-o` | Write diff report to file |

### Configuration File

Generate a config file with `pgdeploy init`, then edit to match your environment:

```json
{
  "connection": {
    "host": "localhost",
    "port": 5432,
    "database": "MyDatabase",
    "username": "postgres",
    "password": ""
  },
  "paths": {
    "schema": "C:/Projects/MyApp/Schema",
    "initData": "C:/Projects/MyApp/InitData"
  },
  "extensions": ["pgcrypto"],
  "options": {
    "executeSeedData": true,
    "stopOnError": true
  }
}
```

> **Note**: `schema` and `initData` must be absolute paths (e.g. `C:\Projects\MyApp\Schema`). Relative paths are not supported.

**Parameter priority**: CLI arguments > config file > defaults

### CI/CD Integration

In automated pipelines, storing database credentials in a config file committed to source control is a security risk. The recommended approach is to keep only non-sensitive settings in the config file and inject credentials at runtime from your platform's secret management.

**GitHub Actions example:**

```yaml
- name: Deploy database schema
  run: |
    pgdeploy deploy \
      --config PostgresDeployer.json \
      --host ${{ secrets.DB_HOST }} \
      --database ${{ secrets.DB_NAME }} \
      --username ${{ secrets.DB_USER }} \
      --password ${{ secrets.DB_PASSWORD }} \
      --yes
```

**GitLab CI / Generic shell example:**

```bash
pgdeploy deploy \
  --config PostgresDeployer.json \
  --host "$DB_HOST" \
  --database "$DB_NAME" \
  --username "$DB_USER" \
  --password "$DB_PASSWORD" \
  --yes
```

**Fully config-less example** (no `--config` file at all):

```bash
pgdeploy deploy \
  --host "$DB_HOST" \
  --port 5432 \
  --database "$DB_NAME" \
  --username "$DB_USER" \
  --password "$DB_PASSWORD" \
  --schema "C:\SqlScripts\Schema" \
  --init-data "C:\SqlScripts\InitData" \
  --extensions "pgcrypto,uuid-ossp" \
  --stop-on-error \
  --yes
```

> **Tip**: `--yes` skips the interactive confirmation prompt, which is required in non-interactive CI environments.

## WPF Desktop Application

The WPF desktop application provides an interactive interface for deploying and managing schemas:

1. **Settings** — load or create a config file, enter connection details, and test the connection
2. **Deploy** — analyze schema differences, review the change list, and execute deployment
3. **Log** — real-time deployment log with timestamps

The language can be switched at runtime (English / 繁體中文) from the sidebar.

## Supported Schema Changes

All changes are detected automatically and applied in the correct order:

| Symbol | Change Type | Description |
|--------|-------------|-------------|
| `[+TABLE]` | Create Table | New table in SQL files not present in DB |
| `[+COL]` | Add Column | New column added to a table |
| `[~TYPE]` | Alter Column Type | Column data type changed |
| `[~NULL]` | Alter Column Nullable | NULL / NOT NULL constraint changed |
| `[~DFLT]` | Alter Column Default | DEFAULT value changed or removed |
| `[+IDX]` | Create Index | New index defined |
| `[~IDX]` | Recreate Index | Index definition changed (drop + recreate) |
| `[-IDX]` | Drop Index | Index removed from definition |
| `[-COL]` | Drop Column | Column removed from definition |
| `[~PK]` | Recreate Primary Key | Primary key definition changed |

> **Caution operations** — changes that could result in data loss (column deletion, primary key changes, type narrowing, adding NOT NULL without a DEFAULT) are flagged with `[!]` warnings and require explicit confirmation before applying.

## Contributing

See [docs/](docs/) for developer guides.

## License

MIT
