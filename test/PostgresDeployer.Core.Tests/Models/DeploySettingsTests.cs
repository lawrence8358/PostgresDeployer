namespace PostgresDeployer.Core.Tests.Models;

using PostgresDeployer.Core.Models;

public class DeploySettingsTests
{
    [Fact]
    public void LoadFromFile_WithConnectionStringField_PopulatesConnectionFields()
    {
        var json = """
            {
              "connection": {
                "connectionString": "Host=filehost;Port=5434;Database=filedb;Username=fileuser;Password=filepw"
              },
              "paths": { "schema": "", "initData": "" },
              "extensions": []
            }
            """;

        var tempFile = Path.Combine(Path.GetTempPath(), $"deploy-settings-test-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempFile, json);

            var settings = DeploySettings.LoadFromFile(tempFile);

            Assert.Equal("filehost", settings.Connection.Host);
            Assert.Equal(5434, settings.Connection.Port);
            Assert.Equal("filedb", settings.Connection.Database);
            Assert.Equal("fileuser", settings.Connection.Username);
            Assert.Equal("filepw", settings.Connection.Password);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void LoadFromFile_WithCreateDatabaseIfNotExists_SetsOption()
    {
        var json = """
            {
              "connection": {
                "host": "localhost",
                "port": 5432,
                "database": "testdb",
                "username": "user",
                "password": ""
              },
              "paths": { "schema": "", "initData": "" },
              "extensions": [],
              "options": {
                "createDatabaseIfNotExists": true
              }
            }
            """;

        var tempFile = Path.Combine(Path.GetTempPath(), $"deploy-settings-test-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempFile, json);

            var settings = DeploySettings.LoadFromFile(tempFile);

            Assert.True(settings.Options.CreateDatabaseIfNotExists);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Json_RoundTrip_PreservesAllowCascadeDrop(bool value)
    {
        var original = new DeploySettings { Options = new DeployOptions { AllowCascadeDrop = value } };

        var json = original.ToJson();
        var restored = DeploySettings.FromJson(json);

        Assert.Contains($"\"allowCascadeDrop\": {(value ? "true" : "false")}", json);
        Assert.Equal(value, restored.Options.AllowCascadeDrop);
    }

    [Fact]
    public void FromJson_WithoutAllowCascadeDrop_DefaultsToFalse()
    {
        var settings = DeploySettings.FromJson("""{ "options": { "stopOnError": true } }""");

        Assert.False(settings.Options.AllowCascadeDrop);
    }
}
