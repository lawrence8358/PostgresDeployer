namespace PostgresDeployer.Wpf.ViewModels;

using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;
using PostgresDeployer.Wpf.Services;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettingsService _appSettings;
    private readonly IDialogService _dialog;
    private string? _currentConfigPath;

    // ═══ 連線設定 ═══
    [ObservableProperty] private string host = "localhost";
    [ObservableProperty] private int port = 5432;
    [ObservableProperty] private string database = "";
    [ObservableProperty] private string username = "";
    [ObservableProperty] private string password = "";

    // ═══ 路徑設定 ═══
    [ObservableProperty] private string schemaPath = "";
    [ObservableProperty] private string initDataPath = "";

    // ═══ Extensions ═══
    [ObservableProperty] private ObservableCollection<string> extensions = new(["pgcrypto", "postgis"]);
    [ObservableProperty] private string newExtension = "";

    // ═══ 選項 ═══

    [ObservableProperty] private bool executeSeedData = true;
    [ObservableProperty] private bool stopOnError = true;

    // ═══ 狀態 ═══
    [ObservableProperty] private string connectionStatus = "";
    [ObservableProperty] private bool isTestingConnection;
    [ObservableProperty] private ObservableCollection<AppSettingsService.RecentFile> recentFiles = [];

    public SettingsViewModel(AppSettingsService appSettings, IDialogService dialog)
    {
        _appSettings = appSettings;
        _dialog = dialog;
        var settings = _appSettings.Load();
        RecentFiles = new ObservableCollection<AppSettingsService.RecentFile>(settings.RecentFiles);
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsTestingConnection = true;
        ConnectionStatus = LocalizationService.Instance["Status_TestingConn"];
        try
        {
            var connSettings = new ConnectionSettings
            {
                Host = Host,
                Port = Port,
                Database = Database,
                Username = Username,
                Password = Password
            };
            var introspector = new SchemaIntrospector(connSettings.ToConnectionString());
            if (await introspector.TestConnectionAsync())
            {
                ConnectionStatus = LocalizationService.Instance["Status_ConnSuccess"];
            }
            else
            {
                ConnectionStatus = LocalizationService.Instance["Status_ConnFailed"];
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = string.Format(
                LocalizationService.Instance["Status_ConnFailedWith"], ex.Message);
        }
        finally
        {
            IsTestingConnection = false;
        }
    }

    [RelayCommand]
    private void LoadSettings()
    {
        var path = _dialog.ShowOpenFileDialog(
            LocalizationService.Instance["Dialog_JsonFilter"],
            LocalizationService.Instance["Dialog_LoadSettings"]);
        if (path == null) return;

        try
        {
            var settings = DeploySettings.LoadFromFile(path);
            FromSettings(settings);
            _currentConfigPath = path;
            _appSettings.AddRecentFile(path);
            RefreshRecentFiles();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(
                string.Format(LocalizationService.Instance["Error_LoadSettings"], ex.Message),
                LocalizationService.Instance["Common_Error"]);
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        if (_currentConfigPath == null)
        {
            SaveSettingsAs();
            return;
        }
        try
        {
            ToSettingsForSave().SaveToFile(_currentConfigPath);
        }
        catch (Exception ex)
        {
            _dialog.ShowError(
                string.Format(LocalizationService.Instance["Error_SaveSettings"], ex.Message),
                LocalizationService.Instance["Common_Error"]);
        }
    }

    [RelayCommand]
    private void SaveSettingsAs()
    {
        var path = _dialog.ShowSaveFileDialog(
            LocalizationService.Instance["Dialog_JsonFilterSave"],
            LocalizationService.Instance["Dialog_SaveSettings"],
            "PostgresDeployer.json");
        if (path == null) return;

        try
        {
            ToSettingsForSave().SaveToFile(path);
            _currentConfigPath = path;
            _appSettings.AddRecentFile(path);
            RefreshRecentFiles();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(
                string.Format(LocalizationService.Instance["Error_SaveSettings"], ex.Message),
                LocalizationService.Instance["Common_Error"]);
        }
    }

    [RelayCommand]
    private void BrowseSchemaPath()
    {
        var folder = _dialog.ShowOpenFolderDialog(
            LocalizationService.Instance["Dialog_SelectSchemaFolder"]);
        if (folder != null)
            SchemaPath = folder;
    }

    [RelayCommand]
    private void BrowseInitDataPath()
    {
        var folder = _dialog.ShowOpenFolderDialog(
            LocalizationService.Instance["Dialog_SelectInitDataFolder"]);
        if (folder != null)
            InitDataPath = folder;
    }

    [RelayCommand]
    private void AddExtension()
    {
        if (string.IsNullOrWhiteSpace(NewExtension)) return;
        if (!Extensions.Contains(NewExtension.Trim()))
            Extensions.Add(NewExtension.Trim());
        NewExtension = "";
    }

    [RelayCommand]
    private void RemoveExtension(string ext)
    {
        Extensions.Remove(ext);
    }

    [RelayCommand]
    private void LoadRecentFile(AppSettingsService.RecentFile recent)
    {
        if (!File.Exists(recent.Path))
        {
            _dialog.ShowMessage(
                string.Format(LocalizationService.Instance["Error_FileNotFound"], recent.Path),
                LocalizationService.Instance["Common_Error"],
                DialogIcon.Warning);
            return;
        }

        try
        {
            var settings = DeploySettings.LoadFromFile(recent.Path);
            FromSettings(settings);
            _currentConfigPath = recent.Path;
            _appSettings.AddRecentFile(recent.Path);
            RefreshRecentFiles();
        }
        catch (Exception ex)
        {
            _dialog.ShowError(
                string.Format(LocalizationService.Instance["Error_LoadSettings"], ex.Message),
                LocalizationService.Instance["Common_Error"]);
        }
    }

    private void RefreshRecentFiles()
    {
        var settings = _appSettings.Load();
        RecentFiles = new ObservableCollection<AppSettingsService.RecentFile>(settings.RecentFiles);
    }

    public DeploySettings ToSettings()
    {
        return new DeploySettings
        {
            Connection = new ConnectionSettings
            {
                Host = Host,
                Port = Port,
                Database = Database,
                Username = Username,
                Password = Password
            },
            Paths = new PathSettings
            {
                Schema = SchemaPath,
                InitData = InitDataPath
            },
            Extensions = Extensions.ToList(),
            Options = new DeployOptions
            {
                ExecuteSeedData = ExecuteSeedData,
                StopOnError = StopOnError
            }
        };
    }

    private DeploySettings ToSettingsForSave()
    {
        // 密碼儲存至 Windows 認證管理員，不寫入 JSON 設定檔
        var key = WindowsCredentialManager.CredentialKey(Host, Port, Database, Username);
        WindowsCredentialManager.Save(key, Password);
        var settings = ToSettings();
        settings.Connection.Password = "";
        return settings;
    }

    public void FromSettings(DeploySettings settings)
    {
        Host = settings.Connection.Host;
        Port = settings.Connection.Port;
        Database = settings.Connection.Database;
        Username = settings.Connection.Username;

        // 從 Windows 認證管理員讀取密碼
        var key = WindowsCredentialManager.CredentialKey(Host, Port, Database, Username);
        Password = WindowsCredentialManager.Load(key);
        SchemaPath = settings.Paths.GetFullPath(settings.Paths.Schema);
        InitDataPath = settings.Paths.GetFullPath(settings.Paths.InitData);
        Extensions = new ObservableCollection<string>(settings.Extensions);
        ExecuteSeedData = settings.Options.ExecuteSeedData;
        StopOnError = settings.Options.StopOnError;
    }
}
