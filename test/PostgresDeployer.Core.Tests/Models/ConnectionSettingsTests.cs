namespace PostgresDeployer.Core.Tests.Models;

using Npgsql;
using PostgresDeployer.Core.Models;

public class ConnectionSettingsTests
{
    // ═══ ApplyConnectionString ═══

    [Fact]
    public void ApplyConnectionString_PopulatesAllFields()
    {
        var settings = new ConnectionSettings();
        settings.ApplyConnectionString("Host=dbhost;Port=5433;Database=mydb;Username=myuser;Password=secret");

        Assert.Equal("dbhost", settings.Host);
        Assert.Equal(5433, settings.Port);
        Assert.Equal("mydb", settings.Database);
        Assert.Equal("myuser", settings.Username);
        Assert.Equal("secret", settings.Password);
    }

    [Fact]
    public void ApplyConnectionString_WithPartialFields_OnlyOverridesProvidedOnes()
    {
        var settings = new ConnectionSettings
        {
            Host = "original",
            Port = 5432,
            Database = "original",
            Username = "original",
            Password = "original"
        };
        settings.ApplyConnectionString("Host=newhost;Database=newdb");

        Assert.Equal("newhost", settings.Host);
        Assert.Equal("newdb", settings.Database);
        // Port, Username, Password not in the connection string — keep Npgsql defaults
    }

    [Fact]
    public void ApplyConnectionString_WithFullNpgsqlFormat_PopulatesCorrectly()
    {
        var settings = new ConnectionSettings();
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = "pg.example.com",
            Port = 5555,
            Database = "testdb",
            Username = "admin",
            Password = "p@ss"
        };
        settings.ApplyConnectionString(builder.ConnectionString);

        Assert.Equal("pg.example.com", settings.Host);
        Assert.Equal(5555, settings.Port);
        Assert.Equal("testdb", settings.Database);
        Assert.Equal("admin", settings.Username);
        Assert.Equal("p@ss", settings.Password);
    }

    // ═══ ToServerConnectionString ═══

    [Fact]
    public void ToServerConnectionString_UsesDatabasePostgres()
    {
        var settings = new ConnectionSettings
        {
            Host = "myhost",
            Port = 5432,
            Database = "targetdb",
            Username = "user",
            Password = "pass"
        };

        var serverConnStr = settings.ToServerConnectionString();
        var parsed = new NpgsqlConnectionStringBuilder(serverConnStr);

        Assert.Equal("myhost", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("postgres", parsed.Database);
        Assert.Equal("user", parsed.Username);
        Assert.Equal("pass", parsed.Password);
    }

    [Fact]
    public void ToServerConnectionString_DoesNotContainTargetDatabase()
    {
        var settings = new ConnectionSettings
        {
            Host = "localhost",
            Port = 5432,
            Database = "myapp_db",
            Username = "postgres",
            Password = ""
        };

        var serverConnStr = settings.ToServerConnectionString();
        var parsed = new NpgsqlConnectionStringBuilder(serverConnStr);

        Assert.NotEqual("myapp_db", parsed.Database);
        Assert.Equal("postgres", parsed.Database);
    }

    // ═══ ToConnectionString (existing behaviour) ═══

    [Fact]
    public void ToConnectionString_BuildsFromIndividualFields()
    {
        var settings = new ConnectionSettings
        {
            Host = "localhost",
            Port = 5432,
            Database = "mydb",
            Username = "postgres",
            Password = "pw"
        };

        var connStr = settings.ToConnectionString();
        var parsed = new NpgsqlConnectionStringBuilder(connStr);

        Assert.Equal("localhost", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("mydb", parsed.Database);
        Assert.Equal("postgres", parsed.Username);
    }
}
