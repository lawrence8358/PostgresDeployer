using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Wpf.Services;
using PostgresDeployer.Wpf.ViewModels;

namespace PostgresDeployer.Wpf;

public partial class App : Application
{
    private IServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        var services = new ServiceCollection();

        // Services
        services.AddSingleton<AppSettingsService>();
        services.AddSingleton<IDialogService, WpfDialogService>();

        // ViewModels
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<DeployViewModel>(sp =>
        {
            var settingsVm = sp.GetRequiredService<SettingsViewModel>();
            var logVm = sp.GetRequiredService<LogViewModel>();
            var dialog = sp.GetRequiredService<IDialogService>();
            return new DeployViewModel(
                () => settingsVm.ToSettings(),
                (msg, level) => logVm.AddLog(msg, level),
                dialog);
        });
        services.AddSingleton<MainViewModel>();

        // Views
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider();

        // 先載入設定並初始化語系，再建立 MainWindow
        // 這樣 MainWindow 建構子設定 ComboBox 選項時已能讀到正確語系
        var appSettings = _serviceProvider.GetRequiredService<AppSettingsService>();
        var settings = appSettings.Load();
        LocalizationService.Instance.InitFromSystem(settings.Language);

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();

        // 還原視窗尺寸、位置、狀態
        mainWindow.Width  = settings.WindowWidth;
        mainWindow.Height = settings.WindowHeight;

        if (settings.WindowLeft.HasValue && settings.WindowTop.HasValue)
        {
            // 確認儲存的位置仍在可見螢幕範圍內，避免多螢幕斷線後視窗跑到看不到的位置
            double left = settings.WindowLeft.Value, top = settings.WindowTop.Value;
            bool onScreen =
                left + settings.WindowWidth  > SystemParameters.VirtualScreenLeft &&
                left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
                top  + settings.WindowHeight > SystemParameters.VirtualScreenTop &&
                top  < SystemParameters.VirtualScreenTop  + SystemParameters.VirtualScreenHeight;

            if (onScreen)
            {
                mainWindow.Left = left;
                mainWindow.Top  = top;
            }
        }

        if (Enum.TryParse<WindowState>(settings.WindowState, out var ws) &&
            ws == WindowState.Maximized)
        {
            mainWindow.WindowState = WindowState.Maximized;
        }

        // 自動載入上次使用的設定檔
        if (!string.IsNullOrEmpty(settings.LastConfigPath) && File.Exists(settings.LastConfigPath))
        {
            var vm = _serviceProvider.GetRequiredService<SettingsViewModel>();
            try
            {
                var deploySettings = DeploySettings.LoadFromFile(settings.LastConfigPath);
                vm.FromSettings(deploySettings);
            }
            catch (Exception) { /* 忽略載入失敗 */ }
        }

        mainWindow.Show();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider != null)
        {
            var appSettings = _serviceProvider.GetRequiredService<AppSettingsService>();
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            var settings = appSettings.Load();

            // 最大化時記錄 RestoreBounds，以便下次還原後能回到正確尺寸
            if (mainWindow.WindowState == WindowState.Normal)
            {
                settings.WindowWidth  = mainWindow.Width;
                settings.WindowHeight = mainWindow.Height;
                settings.WindowLeft   = mainWindow.Left;
                settings.WindowTop    = mainWindow.Top;
            }
            else if (mainWindow.WindowState == WindowState.Maximized)
            {
                settings.WindowWidth  = mainWindow.RestoreBounds.Width;
                settings.WindowHeight = mainWindow.RestoreBounds.Height;
                settings.WindowLeft   = mainWindow.RestoreBounds.Left;
                settings.WindowTop    = mainWindow.RestoreBounds.Top;
            }

            // 最小化時不記錄 Minimized，下次開啟維持 Normal
            settings.WindowState = mainWindow.WindowState == WindowState.Maximized
                ? "Maximized" : "Normal";

            // 儲存使用者選擇的語系偏好
            settings.Language = LocalizationService.Instance.CurrentLanguageCode;

            appSettings.Save(settings);
        }
        base.OnExit(e);

        (_serviceProvider as IDisposable)?.Dispose();
    }
}

