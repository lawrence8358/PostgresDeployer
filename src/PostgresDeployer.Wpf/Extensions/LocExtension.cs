namespace PostgresDeployer.Wpf.Extensions;

using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using PostgresDeployer.Wpf.Services;

/// <summary>
/// XAML markup extension for localized strings.
/// Returns a binding to LocalizationService.Instance[Key], which auto-updates
/// when the user switches language.
///
/// Usage:
///   Text="{l:Loc Settings_Title}"
///   Content="{l:Loc Key=Deploy_Execute}"
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public class LocExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    public LocExtension() { }
    public LocExtension(string key) { Key = key; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        // Return a Binding when the target is a DependencyObject (most XAML controls).
        // The Binding connects to LocalizationService.Instance[Key] and refreshes
        // automatically when LocalizationService fires PropertyChanged with IndexerName.
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget pvt
            && pvt.TargetObject is DependencyObject)
        {
            return new Binding($"[{Key}]")
            {
                Source = LocalizationService.Instance,
                Mode = BindingMode.OneWay
            }.ProvideValue(serviceProvider);
        }

        // Fallback for non-DependencyObject targets (e.g. Run inside DataTemplate at design time).
        return LocalizationService.Instance[Key];
    }
}
