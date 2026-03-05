namespace PostgresDeployer.Cli.Commands;

using PostgresDeployer.Core.Models;

public static class InitCommand
{
    public static Task<int> HandleAsync(string output)
    {
        try
        {
            var settings = new DeploySettings { Extensions = ["pgcrypto"] };
            settings.SaveToFile(output);
            Console.WriteLine($"Settings file created: {output}");
            return Task.FromResult(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return Task.FromResult(1);
        }
    }
}
