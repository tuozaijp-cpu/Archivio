using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using CommunityToolkit.WinUI.UI.Controls;

namespace Archivio.Views;

public partial class MainPage
{
    private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e) => FilterTextBox_TextChangedCore(sender, e);
    private void VideoListDataGrid_Sorting(object sender, DataGridColumnEventArgs e) => VideoListDataGrid_SortingCore(sender, e);
    private void FilterButton_PointerPressed(object sender, PointerRoutedEventArgs e) => FilterButton_PointerPressedCore(sender, e);
    private void FilterFlyout_Opening(object sender, object e) => FilterFlyout_OpeningCore(sender, e);
    private void SearchValuesTextBox_TextChanged(object sender, TextChangedEventArgs e) => SearchValuesTextBox_TextChangedCore(sender, e);
    private void SelectAllValues_Click(object sender, RoutedEventArgs e) => SelectAllValues_ClickCore(sender, e);
    private void ClearAllValues_Click(object sender, RoutedEventArgs e) => ClearAllValues_ClickCore(sender, e);
    private void ApplyFilter_Click(object sender, RoutedEventArgs e) => ApplyFilter_ClickCore(sender, e);
    private void ClearFilter_Click(object sender, RoutedEventArgs e) => ClearFilter_ClickCore(sender, e);
}
