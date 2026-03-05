using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PostgresDeployer.Wpf.ViewModels;

namespace PostgresDeployer.Wpf.Views;

public partial class SettingsView : UserControl
{
    private SettingsViewModel? _vm;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as SettingsViewModel;

        if (_vm != null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            PwdBox.Password = _vm.Password;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.Password))
        {
            if (PwdBox.Password != _vm!.Password)
                PwdBox.Password = _vm.Password;
        }
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_vm != null && sender is PasswordBox pb)
            _vm.Password = pb.Password;
    }

    private void ExtensionTag_Closed(object sender, EventArgs e)
    {
        if (sender is HandyControl.Controls.Tag tag && DataContext is SettingsViewModel vm && tag.Content is string ext)
        {
            vm.RemoveExtensionCommand.Execute(ext);
        }
    }
}
