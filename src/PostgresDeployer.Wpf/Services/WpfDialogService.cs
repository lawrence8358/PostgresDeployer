namespace PostgresDeployer.Wpf.Services;

using Microsoft.Win32;
using System.Windows;

public class WpfDialogService : IDialogService
{
    public void ShowMessage(string message, string title, DialogIcon icon = DialogIcon.Information)
    {
        var image = icon switch
        {
            DialogIcon.Warning => MessageBoxImage.Warning,
            DialogIcon.Error => MessageBoxImage.Error,
            _ => MessageBoxImage.Information
        };
        MessageBox.Show(message, title, MessageBoxButton.OK, image);
    }

    public void ShowError(string message, string title)
        => ShowMessage(message, title, DialogIcon.Error);

    public bool Confirm(string message, string title)
        => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning)
           == MessageBoxResult.Yes;

    public string? ShowOpenFileDialog(string filter, string title)
    {
        var dialog = new OpenFileDialog { Filter = filter, Title = title };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowSaveFileDialog(string filter, string title, string? defaultFileName = null)
    {
        var dialog = new SaveFileDialog { Filter = filter, Title = title };
        if (defaultFileName != null) dialog.FileName = defaultFileName;
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowOpenFolderDialog(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
