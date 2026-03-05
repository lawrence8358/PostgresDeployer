using System.Windows.Controls;
using PostgresDeployer.Wpf.ViewModels;

namespace PostgresDeployer.Wpf.Views;

public partial class DeployView : UserControl
{
    public DeployView()
    {
        InitializeComponent();
    }

    private void TreeView_SelectedItemChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is DeployViewModel vm)
        {
            vm.SelectChange(e.NewValue as ChangeItem);
        }
    }
}
