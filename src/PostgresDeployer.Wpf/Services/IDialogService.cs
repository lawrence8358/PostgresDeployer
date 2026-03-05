namespace PostgresDeployer.Wpf.Services;

public interface IDialogService
{
    void ShowMessage(string message, string title, DialogIcon icon = DialogIcon.Information);
    void ShowError(string message, string title);
    bool Confirm(string message, string title);
    string? ShowOpenFileDialog(string filter, string title);
    string? ShowSaveFileDialog(string filter, string title, string? defaultFileName = null);
    string? ShowOpenFolderDialog(string title);
}

public enum DialogIcon
{
    Information,
    Warning,
    Error
}
