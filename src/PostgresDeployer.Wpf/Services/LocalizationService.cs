namespace PostgresDeployer.Wpf.Services;

using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows.Data;
using PostgresDeployer.Core.Resources;
using PostgresDeployer.Wpf.Resources;

/// <summary>
/// Singleton localization service. Wraps the RESX ResourceManager and supports
/// runtime language switching via INotifyPropertyChanged indexer notifications.
///
/// Usage in C#:  LocalizationService.Instance["Key"]
/// Usage in XAML: {l:Loc Key}  (via LocExtension)
///
/// To add a new language:
///   1. Add Resources/Strings.{culture}.resx with translations
///   2. Add a new Language entry to SupportedLanguages
///   3. Rebuild — the satellite assembly is created automatically
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    public static readonly LocalizationService Instance = new();

    private CultureInfo _currentCulture = CultureInfo.InvariantCulture;

    public record Language(string Code, string DisplayName);

    public static IReadOnlyList<Language> SupportedLanguages { get; } =
    [
        new Language("en",    "English"),
        new Language("zh-TW", "繁體中文"),
    ];

    /// <summary>Current language code, e.g. "en" or "zh-TW".</summary>
    public string CurrentLanguageCode =>
        string.IsNullOrEmpty(_currentCulture.Name) ? "en" : _currentCulture.Name;

    /// <summary>Returns the localized string for the given key, or "[key]" if not found.</summary>
    public string this[string key] =>
        Strings.ResourceManager.GetString(key, _currentCulture) ?? $"[{key}]";

    /// <summary>
    /// Switches the active language and notifies all XAML bindings using the indexer.
    /// </summary>
    public void SetLanguage(string cultureCode)
    {
        _currentCulture = cultureCode == "en"
            ? CultureInfo.InvariantCulture
            : new CultureInfo(cultureCode);

        CoreStrings.SetCulture(_currentCulture);

        // Binding.IndexerName = "Item[]" — notifies every {Binding [Key]} binding
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }

    /// <summary>
    /// Initializes the language from a saved preference or, if null, from the OS locale.
    /// Falls back to English if the OS locale is not supported.
    /// </summary>
    public void InitFromSystem(string? savedCode = null)
    {
        SetLanguage(savedCode ?? DetectSystemLanguage());
    }

    private static string DetectSystemLanguage()
    {
        var uiCulture = CultureInfo.CurrentUICulture;
        foreach (var lang in SupportedLanguages)
        {
            if (lang.Code == "en") continue; // English is the fallback, skip in detection
            if (uiCulture.Name.StartsWith(lang.Code, StringComparison.OrdinalIgnoreCase))
                return lang.Code;
        }
        return "en";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
