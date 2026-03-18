namespace PostgresDeployer.Wpf.ViewModels;

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Core.Services;
using PostgresDeployer.Wpf.Services;

public partial class DeployViewModel : ObservableObject
{
    private readonly Func<DeploySettings> _getSettings;
    private readonly Action<string, LogLevel> _logAction;
    private readonly IDialogService _dialog;

    [ObservableProperty] private ObservableCollection<ChangeGroupItem> changeGroups = [];
    [ObservableProperty] private string selectedSql = "";
    [ObservableProperty] private double progressValue;
    [ObservableProperty] private string statusText = LocalizationService.Instance["Status_Ready"];
    [ObservableProperty] private bool isDeploying;
    [ObservableProperty] private bool canDeploy;

    private bool _isAnalyzing;

    private DeployPlan? _currentPlan;
    private CancellationTokenSource? _cts;

    // Maps Core group names (fixed, language-neutral) → ChangeGroupType enum
    private static readonly Dictionary<string, ChangeGroupType> CoreNameToGroupType = new()
    {
        [DeployGroupNames.NewTables] = ChangeGroupType.NewTables,
        [DeployGroupNames.TableChanges] = ChangeGroupType.TableChanges,
        [DeployGroupNames.Indexes] = ChangeGroupType.Indexes,
    };

    // Maps ChangeGroupType → RESX localization key for display name
    private static readonly Dictionary<ChangeGroupType, string> GroupTypeToLocKey = new()
    {
        [ChangeGroupType.NewTables] = "Group_NewTables",
        [ChangeGroupType.TableChanges] = "Group_TableChanges",
        [ChangeGroupType.Indexes] = "Group_Indexes",
        [ChangeGroupType.Cautions] = "Group_Cautions",
    };

    private static ChangeGroupType ResolveGroupType(string coreName) =>
        CoreNameToGroupType.TryGetValue(coreName, out var t) ? t : ChangeGroupType.Other;

    private static string ResolveGroupDisplayName(string coreName, ChangeGroupType groupType)
    {
        if (GroupTypeToLocKey.TryGetValue(groupType, out var key))
            return LocalizationService.Instance[key];
        return coreName; // Non-localized groups (Extensions, Views, etc.) keep Core name
    }

    public DeployViewModel(Func<DeploySettings> getSettings, Action<string, LogLevel> logAction, IDialogService dialog)
    {
        _getSettings = getSettings;
        _logAction = logAction;
        _dialog = dialog;
    }

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (_isAnalyzing || IsDeploying) return;
        _isAnalyzing = true;
        StatusText = LocalizationService.Instance["Status_Analyzing"];
        ProgressValue = 0;
        ChangeGroups.Clear();
        SelectedSql = "";

        try
        {
            var settings = _getSettings();

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new WpfLoggerProvider(_logAction));
                builder.SetMinimumLevel(LogLevel.Information);
            });

            var logger = loggerFactory.CreateLogger<DeployOrchestrator>();
            var orchestrator = new DeployOrchestrator(logger, loggerFactory);

            _currentPlan = await Task.Run(() => orchestrator.AnalyzeAsync(settings));

            // 轉換為 TreeView 結構
            foreach (var group in _currentPlan.Groups)
            {
                if (group.Changes.Count == 0 && group.Statements.Count == 0) continue;

                var groupType = ResolveGroupType(group.Name);
                var groupItem = new ChangeGroupItem
                {
                    GroupType = groupType,
                    GroupName = ResolveGroupDisplayName(group.Name, groupType),
                    Count = group.Changes.Count > 0 ? group.Changes.Count : group.Statements.Count
                };

                if (group.Changes.Count > 0)
                {
                    foreach (var change in group.Changes)
                    {
                        groupItem.Items.Add(new ChangeItem
                        {
                            Type = change.Type,
                            Description = change.Description,
                            Sql = change.Sql
                        });
                    }
                }
                else
                {
                    // Statement-only 群組（Extensions/Functions/Views/Seed Data）
                    for (int i = 0; i < group.Statements.Count; i++)
                    {
                        var label = i < group.StatementLabels.Count
                            ? group.StatementLabels[i]
                            : string.Format(LocalizationService.Instance["Group_Statement"], i + 1);
                        groupItem.Items.Add(new ChangeItem
                        {
                            Description = label,
                            Sql = group.Statements[i]
                        });
                    }
                }

                ChangeGroups.Add(groupItem);
            }

            // 注意事項群組
            if (_currentPlan.Cautions.Count > 0)
            {
                var cautionGroup = new ChangeGroupItem
                {
                    GroupType = ChangeGroupType.Cautions,
                    GroupName = LocalizationService.Instance["Group_Cautions"],
                    Count = _currentPlan.Cautions.Count
                };
                foreach (var caution in _currentPlan.Cautions)
                {
                    cautionGroup.Items.Add(new ChangeItem
                    {
                        Type = caution.Type,
                        Description = $"⚠ {caution.CautionMessage}",
                        Sql = caution.Sql
                    });
                }
                ChangeGroups.Add(cautionGroup);
            }

            CanDeploy = _currentPlan.HasChanges;
            StatusText = string.Format(
                LocalizationService.Instance["Status_AnalyzeComplete"],
                _currentPlan.TotalStatements, _currentPlan.Cautions.Count);
        }
        catch (Exception ex)
        {
            StatusText = string.Format(LocalizationService.Instance["Status_AnalyzeFailed"], ex.Message);
            _logAction($"[{DateTime.Now:HH:mm:ss}] [ERROR] {StatusText}", LogLevel.Error);
        }
        finally
        {
            _isAnalyzing = false;
        }
    }

    [RelayCommand]
    private async Task DeployAsync()
    {
        if (_currentPlan == null || !CanDeploy || IsDeploying) return;

        var confirmed = _dialog.Confirm(
            LocalizationService.Instance["Dialog_ConfirmDeploy"],
            LocalizationService.Instance["Dialog_ConfirmDeployTitle"]);
        if (!confirmed) return;

        IsDeploying = true;
        CanDeploy = false;
        ProgressValue = 0;
        _cts = new CancellationTokenSource();

        try
        {
            var settings = _getSettings();
            var totalGroups = _currentPlan.Groups.Count(g => g.Statements.Count > 0);
            var currentGroup = 0;

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddProvider(new WpfLoggerProvider(_logAction));
                builder.SetMinimumLevel(LogLevel.Information);
            });

            var logger = loggerFactory.CreateLogger<DeployOrchestrator>();
            var orchestrator = new DeployOrchestrator(logger, loggerFactory);

            // 若啟用自動建立資料庫，在執行前確保 DB 存在（分析時不建立，執行時才建立）
            if (settings.Options.CreateDatabaseIfNotExists)
            {
                var dispatcher2 = Application.Current.Dispatcher;
                var dbProgress = new Progress<string>(msg =>
                    dispatcher2.Invoke(() => StatusText = msg));
                await Task.Run(() => Core.Services.DatabaseInitializer.EnsureDatabaseExistsAsync(
                    settings.Connection, dbProgress, _cts.Token));
            }

            var dispatcher = Application.Current.Dispatcher;
            var progress = new Progress<string>(msg =>
            {
                dispatcher.Invoke(() =>
                {
                    StatusText = msg;
                    if (msg.StartsWith('[') && totalGroups > 0)
                    {
                        currentGroup++;
                        ProgressValue = (double)currentGroup / totalGroups * 100;
                    }
                });
            });

            var results = await Task.Run(() =>
                orchestrator.ExecuteAsync(settings, _currentPlan, progress, _cts.Token));

            var allSuccess = results.All(r => r.Success);
            var successCount = results.Count(r => r.Success);
            var failCount = results.Count(r => !r.Success);

            ProgressValue = 100;
            StatusText = allSuccess
                ? string.Format(LocalizationService.Instance["Status_DeployAllOk"], successCount)
                : string.Format(LocalizationService.Instance["Status_DeployPartial"], successCount, failCount);

            // 寫入 RunScript
            try
            {
                var runScriptWriter = new RunScriptWriter();
                var hostInfo = $"{settings.Connection.Host}:{settings.Connection.Port}/{settings.Connection.Database}";
                var exeDir = AppContext.BaseDirectory;
                var scriptPath = await Task.Run(() => runScriptWriter.Write(_currentPlan, hostInfo, exeDir));
                _logAction($"[{DateTime.Now:HH:mm:ss}] RunScript saved: {scriptPath}", LogLevel.Information);
            }
            catch (Exception ex)
            {
                _logAction($"[{DateTime.Now:HH:mm:ss}] [WARNING] Failed to write RunScript: {ex.Message}", LogLevel.Warning);
            }

            _dialog.ShowMessage(StatusText,
                LocalizationService.Instance["Dialog_DeployResult"],
                allSuccess ? DialogIcon.Information : DialogIcon.Warning);
        }
        catch (OperationCanceledException)
        {
            StatusText = LocalizationService.Instance["Status_Canceled"];
        }
        catch (Exception ex)
        {
            StatusText = string.Format(LocalizationService.Instance["Status_DeployFailed"], ex.Message);
            _logAction($"[{DateTime.Now:HH:mm:ss}] [ERROR] {StatusText}", LogLevel.Error);
        }
        finally
        {
            IsDeploying = false;
            Interlocked.Exchange(ref _cts, null)?.Dispose();
        }
    }

    [RelayCommand]
    private void CancelDeploy()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException) { }
        StatusText = LocalizationService.Instance["Status_Canceling"];
    }

    public void SelectChange(ChangeItem? item)
    {
        SelectedSql = item?.Sql ?? "";
    }
}

public enum ChangeGroupType
{
    NewTables,
    TableChanges,
    Indexes,
    Cautions,
    Other
}

public class ChangeGroupItem
{
    public ChangeGroupType GroupType { get; set; }
    public string GroupName { get; set; } = "";
    public int Count { get; set; }
    public ObservableCollection<ChangeItem> Items { get; set; } = [];
}

public class ChangeItem
{
    public ChangeType Type { get; set; }
    public string Description { get; set; } = "";
    public string? Sql { get; set; }
}
