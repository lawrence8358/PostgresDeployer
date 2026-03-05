namespace PostgresDeployer.Core.Resources;

using System.Globalization;
using System.Resources;

/// <summary>
/// Provides access to Core user-facing string resources.
/// The ResourceManager base name matches the embedded resource path:
///   {RootNamespace}.{Folder}.{FileName} = PostgresDeployer.Core.Resources.CoreStrings
///
/// Call SetCulture() whenever the application language changes
/// (e.g., from WPF LocalizationService.SetLanguage).
/// </summary>
public static class CoreStrings
{
    private static readonly ResourceManager _rm =
        new ResourceManager(
            "PostgresDeployer.Core.Resources.CoreStrings",
            typeof(CoreStrings).Assembly);

    private static CultureInfo _culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Sets the active culture for Core user-facing strings.
    /// Should be called by the host application whenever the display language changes.
    /// </summary>
    public static void SetCulture(CultureInfo culture) => _culture = culture;

    internal static string Get(string key) =>
        _rm.GetString(key, _culture) ?? key;

    internal static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);
}
