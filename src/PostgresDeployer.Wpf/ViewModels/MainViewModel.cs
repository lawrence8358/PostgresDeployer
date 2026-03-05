namespace PostgresDeployer.Wpf.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

public partial class MainViewModel : ObservableObject
{
    public SettingsViewModel Settings { get; }
    public DeployViewModel Deploy { get; }
    public LogViewModel Log { get; }

    public string WindowTitle => "PostgresDeployer";

    public MainViewModel(
        SettingsViewModel settings,
        DeployViewModel deploy,
        LogViewModel log)
    {
        Settings = settings;
        Deploy = deploy;
        Log = log;
    }
}
