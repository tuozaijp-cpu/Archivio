using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Archivio.Views;

public partial class MainPage
{
    // XAML-facing metadata commands stay in this partial. The implementation remains
    // in the existing page core until the metadata service extraction is completed.
    private void OnSaveClicked(object sender, RoutedEventArgs e) => OnSaveClickedCore(sender, e);

    private void DirectFileWriteModeCheckBox_Click(object sender, RoutedEventArgs e)
        => DirectFileWriteModeCheckBox_ClickCore(sender, e);
}
