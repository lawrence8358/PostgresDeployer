using System.Windows;
using System.Windows.Controls;
using PostgresDeployer.Wpf.Services;
using PostgresDeployer.Wpf.ViewModels;

namespace PostgresDeployer.Wpf;

public partial class MainWindow : HandyControl.Controls.Window
{
    private bool _initializingLanguage = true;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // 預設顯示設定頁
        MainContent.Content = MainContent.Resources["SettingsPage"];

        // 初始化語系下拉選單，選中目前語系
        var currentCode = LocalizationService.Instance.CurrentLanguageCode;
        LanguageComboBox.SelectedItem = LocalizationService.SupportedLanguages
            .FirstOrDefault(l => l.Code == currentCode);
        _initializingLanguage = false;
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && MainContent != null)
        {
            var key = rb.Tag?.ToString();
            MainContent.Content = key switch
            {
                "Settings" => MainContent.Resources["SettingsPage"],
                "Deploy"   => MainContent.Resources["DeployPage"],
                "Log"      => MainContent.Resources["LogPage"],
                _          => MainContent.Resources["SettingsPage"]
            };
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingLanguage) return;
        if (LanguageComboBox.SelectedItem is LocalizationService.Language lang)
            LocalizationService.Instance.SetLanguage(lang.Code);
        // Language preference is persisted to AppSettings on app exit (see App.xaml.cs OnExit)
    }
}
