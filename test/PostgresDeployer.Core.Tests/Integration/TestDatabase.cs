namespace PostgresDeployer.Core.Tests.Integration;

using Npgsql;
using PostgresDeployer.Core.Models;

/// <summary>
/// 整合測試專用的拋棄式資料庫：以 <c>PGD_TEST_CONNECTION</c> 的伺服器連線建立 <c>pgd_it_*</c> 資料庫，Dispose 時刪除。
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _serverConnectionString;

    public string DatabaseName { get; }

    /// <summary>連到本測試資料庫的連線字串</summary>
    public string ConnectionString { get; }

    private TestDatabase(string serverConnectionString, string databaseName)
    {
        _serverConnectionString = serverConnectionString;
        DatabaseName = databaseName;
        ConnectionString = new NpgsqlConnectionStringBuilder(serverConnectionString)
        {
            Database = databaseName,
            Pooling = false
        }.ConnectionString;
    }

    public static async Task<TestDatabase> CreateAsync()
    {
        var raw = Environment.GetEnvironmentVariable(PostgresFactAttribute.EnvironmentVariable)
            ?? throw new InvalidOperationException($"{PostgresFactAttribute.EnvironmentVariable} is not set.");
        var serverConnStr = new NpgsqlConnectionStringBuilder(raw)
        {
            Database = "postgres",
            Pooling = false
        }.ConnectionString;

        var name = $"pgd_it_{Guid.NewGuid():N}"[..20];
        await ExecuteOnAsync(serverConnStr, $"CREATE DATABASE \"{name}\"");
        return new TestDatabase(serverConnStr, name);
    }

    /// <summary>在本測試資料庫執行 SQL（可含多個陳述式）</summary>
    public Task ExecuteAsync(string sql) => ExecuteOnAsync(ConnectionString, sql);

    /// <summary>查詢單一純量值</summary>
    public async Task<T?> ScalarAsync<T>(string sql)
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        var value = await cmd.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)value;
    }

    /// <summary>以本測試資料庫建立部署設定（AnalyzeAsync / ExecuteAsync 用）</summary>
    public DeploySettings CreateSettings(string schemaDirectory, bool allowCascadeDrop = false)
    {
        var connection = new ConnectionSettings();
        connection.ApplyConnectionString(ConnectionString);
        return new DeploySettings
        {
            Connection = connection,
            Paths = new PathSettings { Schema = schemaDirectory },
            Options = new DeployOptions { ExecuteSeedData = false, AllowCascadeDrop = allowCascadeDrop }
        };
    }

    public async ValueTask DisposeAsync()
    {
        await ExecuteOnAsync(_serverConnectionString, $"DROP DATABASE IF EXISTS \"{DatabaseName}\" WITH (FORCE)");
    }

    private static async Task ExecuteOnAsync(string connectionString, string sql)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// 暫存的 schema 目錄（worktree 外的系統暫存資料夾），Dispose 時刪除。
/// </summary>
public sealed class TempSchemaDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), $"pgd_it_{Guid.NewGuid():N}");

    public TempSchemaDirectory() => Directory.CreateDirectory(Path);

    /// <summary>寫入相對路徑的 SQL 檔（如 <c>Tables/T.sql</c>）</summary>
    public void Write(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }
}
