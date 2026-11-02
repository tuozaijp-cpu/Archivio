using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Archivio.Views;

public partial class MainPage
{
    private void DetailFieldColumnsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => DetailFieldColumnsComboBox_SelectionChangedCore(sender, e);

    private void ConfigureDetailsButton_Click(object sender, RoutedEventArgs e)
        => ConfigureDetailsButton_ClickCore(sender, e);

    private void ConfigureColumnsButton_Click(object sender, RoutedEventArgs e)
        => ConfigureColumnsButton_ClickCore(sender, e);

    private void ConfigureCustomFieldsButton_Click(object sender, RoutedEventArgs e)
        => ConfigureCustomFieldsButton_ClickCore(sender, e);
}
