using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Archivio.Views;

public partial class MainPage
{
    private void MainPage_KeyDown(object sender, KeyRoutedEventArgs e) => MainPage_KeyDownCore(sender, e);
    private void DataGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => DataGrid_DoubleTappedCore(sender, e);
    private void CoverArtViewToggleButton_Click(object sender, RoutedEventArgs e) => CoverArtViewToggleButton_ClickCore(sender, e);
    private void VideoPlaybackToggleButton_Click(object sender, RoutedEventArgs e) => VideoPlaybackToggleButton_ClickCore(sender, e);
    private void CaptureFrameButton_Click(object sender, RoutedEventArgs e) => CaptureFrameButton_ClickCore(sender, e);
    private void CreateThumbnailSheetButton_Click(object sender, RoutedEventArgs e) => CreateThumbnailSheetButton_ClickCore(sender, e);
}
