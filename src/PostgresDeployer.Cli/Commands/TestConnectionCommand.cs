namespace PostgresDeployer.Cli.Commands;

using PostgresDeployer.Cli.Helpers;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

public static class TestConnectionCommand
{
    public static async Task<int> HandleAsync(CliArgs args)
    {
        // 1. 合併設定
        var settings = SettingsMerger.Merge(args);

        // 2. 驗證必要參數
        if (string.IsNullOrEmpty(settings.Connection.Database))
        {
            await Console.Error.WriteLineAsync("Error: database name is required (--database, --connection-string, or config file)");
            return 1;
        }

        // 3. 顯示連線資訊
        await Console.Out.WriteLineAsync($"Target:   {settings.Connection.Host}:{settings.Connection.Port}/{settings.Connection.Database}");
        await Console.Out.WriteLineAsync($"Username: {settings.Connection.Username}");

        // 4. 測試連線
        if (settings.Options.CreateDatabaseIfNotExists)
            return await HandleWithCreateDbAsync(settings);

        var introspector = new SchemaIntrospector(settings.Connection.ToConnectionString());
        if (await introspector.TestConnectionAsync())
        {
            await Console.Out.WriteLineAsync("Connection successful!");
            return 0;
        }

        await Console.Error.WriteLineAsync("Connection failed.");
        return 1;
    }

    /// <summary>
    /// 啟用 CreateDatabaseIfNotExists 時的連線測試流程：
    /// 先測試伺服器連線，再檢查目標資料庫是否存在。
    /// </summary>
    private static async Task<int> HandleWithCreateDbAsync(DeploySettings settings)
    {
        var serverConnStr = settings.Connection.ToServerConnectionString();
        var serverIntrospector = new SchemaIntrospector(serverConnStr);

        await Console.Out.WriteLineAsync("Testing server connection (create-db-if-not-exists is enabled)...");

        if (!await serverIntrospector.TestConnectionAsync())
        {
            await Console.Error.WriteLineAsync("Connection failed — cannot reach PostgreSQL server.");
            return 1;
        }

        await Console.Out.WriteLineAsync("Server connection successful!");

        var dbExists = await DatabaseInitializer.DatabaseExistsAsync(
            serverConnStr, settings.Connection.Database);

        if (dbExists)
            await Console.Out.WriteLineAsync($"Database '{settings.Connection.Database}' exists — full connection OK.");
        else
            await Console.Out.WriteLineAsync($"Database '{settings.Connection.Database}' does not exist — it will be created automatically on the next deploy.");

        return 0;
    }
}
