namespace PostgresDeployer.Wpf.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using PostgresDeployer.Wpf.Services;

public partial class LogViewModel : ObservableObject
{
    private readonly IDialogService _dialog;

    [ObservableProperty]
    private ObservableCollection<LogEntry> logEntries = [];

    // Internal code used for filtering ("ALL" is the "show everything" sentinel)
    private const string FilterAll = "ALL";

    [ObservableProperty]
    private string selectedFilter = FilterAll;

    /// <summary>
    /// Filter options exposed to the ComboBox.
    /// The first entry is localized ("All"/"全部"); INFO/WARN/ERROR are universal technical terms.
    /// </summary>
    public string[] FilterOptions =>
    [
        LocalizationService.Instance["Log_FilterAll"],
        "INFO",
        "WARN",
        "ERROR",
    ];

    public ICollectionView FilteredEntries { get; }

    public LogViewModel(IDialogService dialog)
    {
        _dialog = dialog;
        FilteredEntries = CollectionViewSource.GetDefaultView(logEntries);
        FilteredEntries.Filter = FilterLogEntry;

        // When language changes, refresh the FilterOptions binding and keep the selected filter valid
        LocalizationService.Instance.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(FilterOptions));
            // If the "All" option was selected, update SelectedFilter to the new localized text
            if (SelectedFilter != "INFO" && SelectedFilter != "WARN" && SelectedFilter != "ERROR")
                SelectedFilter = FilterOptions[0];
        };

        // Initialize SelectedFilter to the localized "All" text
        SelectedFilter = FilterOptions[0];
    }

    private bool FilterLogEntry(object obj)
    {
        // The "All" option is whichever string is currently at FilterOptions[0]
        if (SelectedFilter == FilterOptions[0]) return true;
        return obj is LogEntry entry && entry.Level == SelectedFilter;
    }

    partial void OnSelectedFilterChanged(string value)
    {
        FilteredEntries.Refresh();
    }

    public void AddLog(string message, LogLevel level)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level switch
            {
                LogLevel.Warning  => "WARN",
                LogLevel.Error    => "ERROR",
                LogLevel.Critical => "ERROR",
                _                 => "INFO"
            },
            Message = message
        };

        if (Application.Current?.Dispatcher.CheckAccess() == true)
        {
            LogEntries.Add(entry);
        }
        else
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                LogEntries.Add(entry);
            });
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogEntries.Clear();
    }

    [RelayCommand]
    private void CopyLog()
    {
        var entries = FilteredEntries.Cast<LogEntry>();
        var text = string.Join(Environment.NewLine,
            entries.Select(e => $"{e.Timestamp:HH:mm:ss} [{e.Level}] {e.Message}"));
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    [RelayCommand]
    private void ExportLog()
    {
        var path = _dialog.ShowSaveFileDialog(
            LocalizationService.Instance["Dialog_TxtFilter"],
            LocalizationService.Instance["Dialog_ExportLog"],
            $"PostgresDeployer-log-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        if (path == null) return;

        try
        {
            var entries = FilteredEntries.Cast<LogEntry>();
            var lines = entries.Select(e =>
                $"{e.Timestamp:yyyy-MM-dd HH:mm:ss} [{e.Level}] {e.Message}");
            File.WriteAllLines(path, lines);
        }
        catch (Exception ex)
        {
            _dialog.ShowError(
                string.Format(LocalizationService.Instance["Error_ExportLog"], ex.Message),
                LocalizationService.Instance["Common_Error"]);
        }
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
}
