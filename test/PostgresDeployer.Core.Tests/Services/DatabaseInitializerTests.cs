namespace PostgresDeployer.Core.Tests.Services;

using Npgsql;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;

public class DatabaseInitializerTests
{
    // ═══ BuildServerConnectionString ═══

    [Fact]
    public void BuildServerConnectionString_ReplacesTargetDatabase_WithPostgres()
    {
        var settings = new ConnectionSettings
        {
            Host = "myhost",
            Port = 5432,
            Database = "myapp",
            Username = "postgres",
            Password = "pw"
        };

        var serverConnStr = DatabaseInitializer.BuildServerConnectionString(settings);
        var parsed = new NpgsqlConnectionStringBuilder(serverConnStr);

        Assert.Equal("myhost", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("postgres", parsed.Database);
        Assert.Equal("postgres", parsed.Username);
    }

    [Fact]
    public void BuildServerConnectionString_PreservesHostPortUsernamePassword()
    {
        var settings = new ConnectionSettings
        {
            Host = "pg.example.com",
            Port = 5555,
            Database = "targetdb",
            Username = "admin",
            Password = "secretpw"
        };

        var serverConnStr = DatabaseInitializer.BuildServerConnectionString(settings);
        var parsed = new NpgsqlConnectionStringBuilder(serverConnStr);

        Assert.Equal("pg.example.com", parsed.Host);
        Assert.Equal(5555, parsed.Port);
        Assert.Equal("admin", parsed.Username);
        Assert.Equal("secretpw", parsed.Password);
        Assert.NotEqual("targetdb", parsed.Database);
    }

    [Fact]
    public void BuildServerConnectionString_DoesNotContainOriginalDatabase()
    {
        var settings = new ConnectionSettings
        {
            Host = "localhost",
            Port = 5432,
            Database = "should_not_appear",
            Username = "user",
            Password = ""
        };

        var serverConnStr = DatabaseInitializer.BuildServerConnectionString(settings);

        Assert.DoesNotContain("should_not_appear", serverConnStr, StringComparison.OrdinalIgnoreCase);
    }
}
