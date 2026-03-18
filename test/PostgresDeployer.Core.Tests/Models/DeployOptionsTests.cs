namespace PostgresDeployer.Core.Tests.Models;

using PostgresDeployer.Core.Models;

public class DeployOptionsTests
{
    [Fact]
    public void CreateDatabaseIfNotExists_DefaultIsFalse()
    {
        var options = new DeployOptions();

        Assert.False(options.CreateDatabaseIfNotExists);
    }

    [Fact]
    public void CreateDatabaseIfNotExists_CanBeSetToTrue()
    {
        var options = new DeployOptions
        {
            CreateDatabaseIfNotExists = true
        };

        Assert.True(options.CreateDatabaseIfNotExists);
    }
}
