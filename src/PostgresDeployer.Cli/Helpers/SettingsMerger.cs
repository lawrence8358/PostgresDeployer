namespace PostgresDeployer.Cli.Helpers;

using PostgresDeployer.Core.Models;

/// <summary>
/// CLI 直接傳入的連線與部署參數
/// </summary>
public record CliArgs
{
    public string? ConfigPath { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Database { get; init; }
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? ConnectionString { get; init; }
    public string? Schema { get; init; }
    public string? InitData { get; init; }
    public string? Extensions { get; init; }
    public bool? CreateDatabaseIfNotExists { get; init; }
}

/// <summary>
/// 設定合併工具。
/// 將 --config 設定檔和 CLI 直接參數合併為最終的 DeploySettings。
/// 優先順序：CLI --connection-string > CLI 個別連線參數 > 設定檔 connectionString > 設定檔個別欄位 > 預設值
/// </summary>
public static class SettingsMerger
{
    public static DeploySettings Merge(CliArgs args)
    {
        // 1. 基底設定：從設定檔載入或使用預設值
        var settings = !string.IsNullOrEmpty(args.ConfigPath)
            ? DeploySettings.LoadFromFile(args.ConfigPath)
            : new DeploySettings();

        // 2. CLI 個別連線參數覆蓋（僅覆蓋非 null 的值）
        if (args.Host != null) settings.Connection.Host = args.Host;
        if (args.Port != null) settings.Connection.Port = args.Port.Value;
        if (args.Database != null) settings.Connection.Database = args.Database;
        if (args.Username != null) settings.Connection.Username = args.Username;
        if (args.Password != null) settings.Connection.Password = args.Password;

        // 3. CLI --connection-string 優先權最高，覆蓋所有連線欄位
        if (!string.IsNullOrEmpty(args.ConnectionString))
            settings.Connection.ApplyConnectionString(args.ConnectionString);

        // 4. 路徑參數
        if (args.Schema != null) settings.Paths.Schema = args.Schema;
        if (args.InitData != null) settings.Paths.InitData = args.InitData;

        // 5. Extensions
        if (args.Extensions != null)
        {
            settings.Extensions = args.Extensions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        // 6. 選項覆蓋
        if (args.CreateDatabaseIfNotExists.HasValue)
            settings.Options.CreateDatabaseIfNotExists = args.CreateDatabaseIfNotExists.Value;

        return settings;
    }
}
