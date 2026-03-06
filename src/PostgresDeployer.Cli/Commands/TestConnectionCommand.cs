namespace PostgresDeployer.Cli.Commands;

using PostgresDeployer.Cli.Helpers;
using PostgresDeployer.Core.Services;

public static class TestConnectionCommand
{
    public static async Task<int> HandleAsync(
        string? config, string? host, int? port,
        string? database, string? username, string? password)
    {
        // 1. 合併設定
        var settings = SettingsMerger.Merge(
            config, host, port, database, username, password,
            null, null, null);

        // 2. 驗證必要參數
        if (string.IsNullOrEmpty(settings.Connection.Database))
        {
            Console.Error.WriteLine("Error: database name is required (--database or config file)");
            return 1;
        }

        // 3. 顯示連線資訊
        Console.WriteLine($"Target:   {settings.Connection.Host}:{settings.Connection.Port}/{settings.Connection.Database}");
        Console.WriteLine($"Username: {settings.Connection.Username}");

        // 4. 測試連線
        var introspector = new SchemaIntrospector(settings.Connection.ToConnectionString());
        if (await introspector.TestConnectionAsync())
        {
            Console.WriteLine("Connection successful!");
            return 0;
        }
        else
        {
            Console.Error.WriteLine("Connection failed.");
            return 1;
        }
    }
}
