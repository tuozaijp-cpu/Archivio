using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace Archivio.Views;

public partial class MainPage
{
    private void MainPage_KeyDown(object sender, KeyRoutedEventArgs e) => _playbackController.HandleKeyDown(e);
    private async void DataGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => await _playbackController.LaunchSelectedFileAsync();
    private async void CoverArtViewToggleButton_Click(object sender, RoutedEventArgs e) => await _playbackController.SetCoverArtViewAsync();
    private async void VideoPlaybackToggleButton_Click(object sender, RoutedEventArgs e) => await _playbackController.SetPlaybackViewAsync();
    private void CaptureFrameButton_Click(object sender, RoutedEventArgs e) => CaptureFrameButton_ClickCore(sender, e);
    private void CreateThumbnailSheetButton_Click(object sender, RoutedEventArgs e) => CreateThumbnailSheetButton_ClickCore(sender, e);
}
