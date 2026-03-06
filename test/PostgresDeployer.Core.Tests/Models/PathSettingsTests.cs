namespace PostgresDeployer.Core.Tests.Models;

using PostgresDeployer.Core.Models;

public class PathSettingsTests
{
    [Fact]
    public void Schema_DefaultValue_IsEmptyString()
    {
        var paths = new PathSettings();
        Assert.Equal("", paths.Schema);
    }

    [Fact]
    public void InitData_DefaultValue_IsEmptyString()
    {
        var paths = new PathSettings();
        Assert.Equal("", paths.InitData);
    }

    [Fact]
    public void Schema_SetAbsolutePath_ReturnsCorrectValue()
    {
        var absPath = Path.Combine(Path.GetTempPath(), "MyProject", "Schema");
        var paths = new PathSettings { Schema = absPath };
        Assert.Equal(absPath, paths.Schema);
    }

    [Fact]
    public void InitData_SetAbsolutePath_ReturnsCorrectValue()
    {
        var absPath = Path.Combine(Path.GetTempPath(), "MyProject", "InitData");
        var paths = new PathSettings { InitData = absPath };
        Assert.Equal(absPath, paths.InitData);
    }
}
