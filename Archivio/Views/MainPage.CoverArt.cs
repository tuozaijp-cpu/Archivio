using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Archivio.Views;

public partial class MainPage
{
    private void AddImageButton_Click(object sender, RoutedEventArgs e) => AddImageButton_ClickCore(sender, e);
    private void CoverArt_DragOver(object sender, DragEventArgs e) => CoverArt_DragOverCore(sender, e);
    private void CoverArt_Drop(object sender, DragEventArgs e) => CoverArt_DropCore(sender, e);
    private void ReplaceImageButton_Click(object sender, RoutedEventArgs e) => ReplaceImageButton_ClickCore(sender, e);
    private void DeleteImageButton_Click(object sender, RoutedEventArgs e) => DeleteImageButton_ClickCore(sender, e);
    private void CoverArt_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => CoverArt_DoubleTappedCore(sender, e);
    private void CoverArtFlipView_SelectionChanged(object sender, SelectionChangedEventArgs e) => CoverArtFlipView_SelectionChangedCore(sender, e);
    private void SaveImageButton_Click(object sender, RoutedEventArgs e) => SaveImageButton_ClickCore(sender, e);
}
