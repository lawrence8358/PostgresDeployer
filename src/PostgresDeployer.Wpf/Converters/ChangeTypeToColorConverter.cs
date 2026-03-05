namespace PostgresDeployer.Wpf.Converters;

using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PostgresDeployer.Core.Models;
using PostgresDeployer.Wpf.ViewModels;

public class ChangeTypeToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // ChangeType → color (for individual change items in the tree)
        if (value is ChangeType type)
        {
            return type switch
            {
                ChangeType.CreateTable          => Brushes.Green,
                ChangeType.AddColumn            => Brushes.Orange,
                ChangeType.AlterColumnType      => Brushes.Orange,
                ChangeType.AlterColumnNullable  => Brushes.Orange,
                ChangeType.AlterColumnDefault   => Brushes.Orange,
                ChangeType.RecreatePrimaryKey   => Brushes.Orange,
                ChangeType.CreateIndex          => Brushes.DodgerBlue,
                ChangeType.RecreateIndex        => Brushes.DodgerBlue,
                ChangeType.DropIndex            => Brushes.Red,
                ChangeType.DropColumn           => Brushes.Red,
                _ => Brushes.Gray
            };
        }

        // ChangeGroupType → color (for group header rows in the tree)
        if (value is ChangeGroupType groupType)
        {
            return groupType switch
            {
                ChangeGroupType.NewTables    => Brushes.Green,
                ChangeGroupType.TableChanges => Brushes.Orange,
                ChangeGroupType.Indexes      => Brushes.DodgerBlue,
                ChangeGroupType.Cautions     => Brushes.Red,
                _ => Brushes.Gray
            };
        }

        return Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
