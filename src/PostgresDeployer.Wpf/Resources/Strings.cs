namespace PostgresDeployer.Wpf.Resources;

using System.Resources;

/// <summary>
/// Provides access to the embedded string resources.
/// The ResourceManager base name matches the embedded resource path:
///   {RootNamespace}.{Folder}.{FileName} = PostgresDeployer.Wpf.Resources.Strings
/// </summary>
internal static class Strings
{
    private static ResourceManager? _resourceManager;

    public static ResourceManager ResourceManager =>
        _resourceManager ??= new ResourceManager(
            "PostgresDeployer.Wpf.Resources.Strings",
            typeof(Strings).Assembly);
}
