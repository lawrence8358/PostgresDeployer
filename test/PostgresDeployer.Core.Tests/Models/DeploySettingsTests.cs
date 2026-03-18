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
}
