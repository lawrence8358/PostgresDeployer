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

        var settings = SettingsMerger.Merge(new CliArgs { ConfigPath = configPath });

        Assert.Equal("C:/TestProject/Schema", settings.Paths.Schema);
        Assert.Equal("C:/TestProject/InitData", settings.Paths.InitData);
    }

    [Fact]
    public void Merge_WithConfig_LoadsConnectionSettings()
    {
        var configPath = GetTestDataPath("sample-config.json");

        var settings = SettingsMerger.Merge(new CliArgs { ConfigPath = configPath });

        Assert.Equal("localhost", settings.Connection.Host);
        Assert.Equal(5432, settings.Connection.Port);
        Assert.Equal("testdb", settings.Connection.Database);
        Assert.Equal("testuser", settings.Connection.Username);
    }

    [Fact]
    public void Merge_WithConfig_LoadsExtensions()
    {
        var configPath = GetTestDataPath("sample-config.json");

        var settings = SettingsMerger.Merge(new CliArgs { ConfigPath = configPath });

        Assert.Single(settings.Extensions);
        Assert.Equal("pgcrypto", settings.Extensions[0]);
    }

    // ═══ CLI 覆蓋 ═══

    [Fact]
    public void Merge_CliSchemaOverride_OverridesConfigValue()
    {
        var configPath = GetTestDataPath("sample-config.json");

        var settings = SettingsMerger.Merge(new CliArgs
        {
            ConfigPath = configPath,
            Schema = "D:/Override/Schema"
        });

        Assert.Equal("D:/Override/Schema", settings.Paths.Schema);
        Assert.Equal("C:/TestProject/InitData", settings.Paths.InitData);
    }

    [Fact]
    public void Merge_CliInitDataOverride_OverridesConfigValue()
    {
        var configPath = GetTestDataPath("sample-config.json");

        var settings = SettingsMerger.Merge(new CliArgs
        {
            ConfigPath = configPath,
            InitData = "D:/Override/InitData"
        });

        Assert.Equal("C:/TestProject/Schema", settings.Paths.Schema);
        Assert.Equal("D:/Override/InitData", settings.Paths.InitData);
    }

    [Fact]
    public void Merge_CliConnectionOverrides_OverrideConfigValues()
    {
        var configPath = GetTestDataPath("sample-config.json");

        var settings = SettingsMerger.Merge(new CliArgs
        {
            ConfigPath = configPath,
            Host = "remotehost",
            Port = 5433,
            Database = "proddb",
            Username = "admin",
            Password = "secret"
        });

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

        var settings = SettingsMerger.Merge(new CliArgs
        {
            ConfigPath = configPath,
            Extensions = "postgis, uuid-ossp"
        });

        Assert.Equal(2, settings.Extensions.Count);
        Assert.Contains("postgis", settings.Extensions);
        Assert.Contains("uuid-ossp", settings.Extensions);
    }

    // ═══ 無 Config ═══

    [Fact]
    public void Merge_NoConfig_UsesDefaultEmptyPaths()
    {
        var settings = SettingsMerger.Merge(new CliArgs());

        Assert.Equal("", settings.Paths.Schema);
        Assert.Equal("", settings.Paths.InitData);
    }

    [Fact]
    public void Merge_NoConfig_CliPathsUsedDirectly()
    {
        var settings = SettingsMerger.Merge(new CliArgs
        {
            Schema = "E:/My/Schema",
            InitData = "E:/My/Seeds"
        });

        Assert.Equal("E:/My/Schema", settings.Paths.Schema);
        Assert.Equal("E:/My/Seeds", settings.Paths.InitData);
    }

    // ═══ ConnectionString ═══

    [Fact]
    public void Merge_CliConnectionString_PopulatesAllConnectionFields()
    {
        var settings = SettingsMerger.Merge(new CliArgs
        {
            ConnectionString = "Host=remotehost;Port=5433;Database=proddb;Username=admin;Password=secret"
        });

        Assert.Equal("remotehost", settings.Connection.Host);
        Assert.Equal(5433, settings.Connection.Port);
        Assert.Equal("proddb", settings.Connection.Database);
        Assert.Equal("admin", settings.Connection.Username);
        Assert.Equal("secret", settings.Connection.Password);
    }

    [Fact]
    public void Merge_CliConnectionString_OverridesConfigIndividualFields()
    {
        var configPath = GetTestDataPath("sample-config.json");

        var settings = SettingsMerger.Merge(new CliArgs
        {
            ConfigPath = configPath,
            ConnectionString = "Host=clihost;Port=9999;Database=clidb;Username=cliuser;Password=clipass"
        });

        Assert.Equal("clihost", settings.Connection.Host);
        Assert.Equal(9999, settings.Connection.Port);
        Assert.Equal("clidb", settings.Connection.Database);
        Assert.Equal("cliuser", settings.Connection.Username);
        Assert.Equal("clipass", settings.Connection.Password);
    }

    [Fact]
    public void Merge_ConfigWithConnectionString_PopulatesConnectionFields()
    {
        var configPath = GetTestDataPath("sample-config-connstring.json");

        var settings = SettingsMerger.Merge(new CliArgs { ConfigPath = configPath });

        Assert.Equal("cshost", settings.Connection.Host);
        Assert.Equal(5433, settings.Connection.Port);
        Assert.Equal("csdb", settings.Connection.Database);
        Assert.Equal("csuser", settings.Connection.Username);
        Assert.Equal("cspass", settings.Connection.Password);
    }

    [Fact]
    public void Merge_ConfigWithConnectionString_SetsCreateDatabaseIfNotExists()
    {
        var configPath = GetTestDataPath("sample-config-connstring.json");

        var settings = SettingsMerger.Merge(new CliArgs { ConfigPath = configPath });

        Assert.True(settings.Options.CreateDatabaseIfNotExists);
    }

    // ═══ CreateDatabaseIfNotExists ═══

    [Fact]
    public void Merge_CliCreateDb_SetsOptionTrue()
    {
        var settings = SettingsMerger.Merge(new CliArgs
        {
            Database = "mydb",
            CreateDatabaseIfNotExists = true
        });

        Assert.True(settings.Options.CreateDatabaseIfNotExists);
    }

    [Fact]
    public void Merge_CliCreateDb_DefaultIsFalse()
    {
        var settings = SettingsMerger.Merge(new CliArgs { Database = "mydb" });

        Assert.False(settings.Options.CreateDatabaseIfNotExists);
    }
}
