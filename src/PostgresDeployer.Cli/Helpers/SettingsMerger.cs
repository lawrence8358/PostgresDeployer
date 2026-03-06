namespace PostgresDeployer.Cli.Helpers;

using PostgresDeployer.Core.Models;

/// <summary>
/// 設定合併工具。
/// 將 --config 設定檔和 CLI 直接參數合併為最終的 DeploySettings。
/// 優先順序：CLI 參數 > 設定檔 > 預設值
/// </summary>
public static class SettingsMerger
{
    public static DeploySettings Merge(
        string? configPath,
        string? cliHost, int? cliPort, string? cliDatabase,
        string? cliUsername, string? cliPassword,
        string? cliSchema, string? cliInitData,
        string? cliExtensions)
    {
        // 1. 基底設定：從設定檔載入或使用預設值
        var settings = !string.IsNullOrEmpty(configPath)
            ? DeploySettings.LoadFromFile(configPath)
            : new DeploySettings();

        // 2. CLI 參數覆蓋（僅覆蓋非 null 的值）
        if (cliHost != null) settings.Connection.Host = cliHost;
        if (cliPort != null) settings.Connection.Port = cliPort.Value;
        if (cliDatabase != null) settings.Connection.Database = cliDatabase;
        if (cliUsername != null) settings.Connection.Username = cliUsername;
        if (cliPassword != null) settings.Connection.Password = cliPassword;

        if (cliSchema != null) settings.Paths.Schema = cliSchema;
        if (cliInitData != null) settings.Paths.InitData = cliInitData;

        if (cliExtensions != null)
        {
            settings.Extensions = cliExtensions
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }

        return settings;
    }
}
