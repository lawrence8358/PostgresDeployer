namespace PostgresDeployer.Core.Services;

using Npgsql;
using PostgresDeployer.Core.Models;

/// <summary>
/// 負責在目標資料庫不存在時自動建立資料庫。
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// 建立連至伺服器預設資料庫（postgres）的連線字串，
    /// 用於在目標資料庫存在前執行伺服器層級的操作。
    /// </summary>
    public static string BuildServerConnectionString(ConnectionSettings settings)
        => settings.ToServerConnectionString();

    /// <summary>
    /// 檢查目標資料庫是否存在。
    /// </summary>
    public static async Task<bool> DatabaseExistsAsync(
        string serverConnectionString, string databaseName, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(serverConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name", conn);
        cmd.Parameters.AddWithValue("@name", databaseName);
        var result = await cmd.ExecuteScalarAsync(ct);
        return result != null;
    }

    /// <summary>
    /// 建立資料庫。
    /// </summary>
    public static async Task CreateDatabaseAsync(
        string serverConnectionString, string databaseName, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(serverConnectionString);
        await conn.OpenAsync(ct);
        // 使用雙引號包裹識別碼並逸出內部雙引號，防止 SQL Injection
        var quotedName = "\"" + databaseName.Replace("\"", "\"\"") + "\"";
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE {quotedName}", conn);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// 確保目標資料庫存在；若不存在則自動建立。
    /// </summary>
    /// <returns>true = 資料庫已建立；false = 資料庫原本就存在</returns>
    public static async Task<bool> EnsureDatabaseExistsAsync(
        ConnectionSettings settings,
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        var serverConnStr = BuildServerConnectionString(settings);
        var dbName = settings.Database;

        if (await DatabaseExistsAsync(serverConnStr, dbName, ct))
            return false;

        progress?.Report($"Database '{dbName}' does not exist — creating...");
        await CreateDatabaseAsync(serverConnStr, dbName, ct);
        progress?.Report($"Database '{dbName}' created successfully.");
        return true;
    }
}
