namespace PostgresDeployer.Core.Models;

/// <summary>
/// Fixed, language-neutral group name constants used by DeployOrchestrator.
/// Consumers (WPF, CLI) map these to localized display names as needed.
/// </summary>
public static class DeployGroupNames
{
    public const string NewTables    = "New Tables";
    public const string TableChanges = "Table Changes";
    public const string Indexes      = "Indexes";
}
