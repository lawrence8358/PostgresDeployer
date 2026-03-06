namespace PostgresDeployer.Cli.Tests.Helpers;

using PostgresDeployer.Cli.Helpers;

public class SettingsMergerTests
{
    private static string GetTestDataPath(string fileName)
        => Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    // ═══ Config 載入 ═══

    [Fact]
    public void Merge_WithConfig_LoadsSchemaAndInitData()
    {
        var configPath = GetTestDataPath("sample-config.json");

        var settings = SettingsMerger.Merge(
            configPath, null, null, null, null, null, null, null, null);

        Assert.Equal("C:/TestProject/Schema", settings.Paths.Schema);
        Assert.Equal("C:/TestProject/InitData", settings.Paths.InitData);
    }

    [Fact]
    public void Merge_WithConfig_LoadsConnectionSettings()
    {
        var configPath = GetTestDataPath("sample-config.json");

        var settings = SettingsMerger.Merge(
            configPath, null, null, null, null, null, null, null, null);

        Assert.Equal("localhost", settings.Connection.Host);
        Assert.Equal(5432, settings.Connection.Port);
        Assert.Equal("testdb", settings.Connection.Database);
        Assert.Equal("testuser", settings.Connection.Username);
    }

    [Fact]
    public void Merge_WithConfig_LoadsExtensions()
    {
        var configPath = GetTestDataPath("sample-config.json");

        var settings = SettingsMerger.Merge(
            configPath, null, null, null, null, null, null, null, null);

        Assert.Single(settings.Extensions);
        Assert.Equal("pgcrypto", settings.Extensions[0]);
    }

    // ═══ CLI 覆蓋 ═══

    [Fact]
    public void Merge_CliSchemaOverride_OverridesConfigValue()
    {
        var configPath = GetTestDataPath("sample-config.json");

        var settings = SettingsMerger.Merge(
            configPath, null, null, null, null, null,
            cliSchema: "D:/Override/Schema", cliInitData: null, cliExtensions: null);

        Assert.Equal("D:/Override/Schema", settings.Paths.Schema);
        Assert.Equal("C:/TestProject/InitData", settings.Paths.InitData);
    }

    [Fact]
    public void Merge_CliInitDataOverride_OverridesConfigValue()
    {
        var configPath = GetTestDataPath("sample-config.json");

        var settings = SettingsMerger.Merge(
            configPath, null, null, null, null, null,
            cliSchema: null, cliInitData: "D:/Override/InitData", cliExtensions: null);

        Assert.Equal("C:/TestProject/Schema", settings.Paths.Schema);
        Assert.Equal("D:/Override/InitData", settings.Paths.InitData);
    }

    [Fact]
    public void Merge_CliConnectionOverrides_OverrideConfigValues()
    {
        var configPath = GetTestDataPath("sample-config.json");

        var settings = SettingsMerger.Merge(
            configPath,
            cliHost: "remotehost", cliPort: 5433, cliDatabase: "proddb",
            cliUsername: "admin", cliPassword: "secret",
            cliSchema: null, cliInitData: null, cliExtensions: null);

        Assert.Equal("remotehost", settings.Connection.Host);
        Assert.Equal(5433, settings.Connection.Port);
        Assert.Equal("proddb", settings.Connection.Database);
        Assert.Equal("admin", settings.Connection.Username);
        Assert.Equal("secret", settings.Connection.Password);
    }

    [Fact]
    public void Merge_CliExtensionsOverride_ReplacesConfigExtensions()
    {
        var configPath = GetTestDataPath("sample-config.json");

        var settings = SettingsMerger.Merge(
            configPath, null, null, null, null, null,
            cliSchema: null, cliInitData: null, cliExtensions: "postgis, uuid-ossp");

        Assert.Equal(2, settings.Extensions.Count);
        Assert.Contains("postgis", settings.Extensions);
        Assert.Contains("uuid-ossp", settings.Extensions);
    }

    // ═══ 無 Config ═══

    [Fact]
    public void Merge_NoConfig_UsesDefaultEmptyPaths()
    {
        var settings = SettingsMerger.Merge(
            null, null, null, null, null, null, null, null, null);

        Assert.Equal("", settings.Paths.Schema);
        Assert.Equal("", settings.Paths.InitData);
    }

    [Fact]
    public void Merge_NoConfig_CliPathsUsedDirectly()
    {
        var settings = SettingsMerger.Merge(
            null, null, null, null, null, null,
            cliSchema: "E:/My/Schema", cliInitData: "E:/My/Seeds", cliExtensions: null);

        Assert.Equal("E:/My/Schema", settings.Paths.Schema);
        Assert.Equal("E:/My/Seeds", settings.Paths.InitData);
    }
}
