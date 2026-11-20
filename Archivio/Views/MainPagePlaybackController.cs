using Archivio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Archivio.Views;

internal sealed class MainPagePlaybackController : IDisposable
{
    private readonly MainPageViewModel _viewModel;
    private readonly Panel _playbackContent;
    private readonly FrameworkElement _coverArtContent;
    private readonly ToggleButton _coverArtToggle;
    private readonly ToggleButton _playbackToggle;
    private readonly Action _updateActionButtonStates;
    private readonly OperationCoordinator _operations = new();
    private MediaPlayerElement? _playerElement;
    private MediaPlayer? _player;

    public MainPagePlaybackController(MainPageViewModel viewModel, Panel playbackContent, FrameworkElement coverArtContent,
        ToggleButton coverArtToggle, ToggleButton playbackToggle, Action updateActionButtonStates)
    {
        _viewModel = viewModel;
        _playbackContent = playbackContent;
        _coverArtContent = coverArtContent;
        _coverArtToggle = coverArtToggle;
        _playbackToggle = playbackToggle;
        _updateActionButtonStates = updateActionButtonStates;
    }

    public bool IsPlaybackView { get; private set; }
    public bool HasSource => _playerElement?.Source is not null && _player is not null;
    public TimeSpan CurrentPosition => _player?.PlaybackSession?.Position ?? TimeSpan.Zero;

    public async Task SetCoverArtViewAsync()
    {
        if (!_viewModel.CanStartVideoFileActions) return;
        IsPlaybackView = false;
        _coverArtToggle.IsChecked = true;
        _playbackToggle.IsChecked = false;
        await UpdateSourceAsync();
    }

    public async Task SetPlaybackViewAsync()
    {
        if (!_viewModel.CanStartVideoFileActions)
        {
            _playbackToggle.IsChecked = false;
            return;
        }

        IsPlaybackView = true;
        _coverArtToggle.IsChecked = false;
        _playbackToggle.IsChecked = true;
        await UpdateSourceAsync();
    }

    public async Task OnSelectedVideoChangedAsync()
    {
        IsPlaybackView = false;
        _coverArtToggle.IsChecked = true;
        _playbackToggle.IsChecked = false;
        await UpdateSourceAsync();
    }

    public void HandleKeyDown(KeyRoutedEventArgs e)
    {
        if (!IsPlaybackView || !HasSource || _player?.PlaybackSession is not { } session) return;
        switch (e.Key)
        {
            case Windows.System.VirtualKey.Left:
                session.Position = Clamp(session.Position - TimeSpan.FromSeconds(5), session.NaturalDuration); e.Handled = true; break;
            case Windows.System.VirtualKey.Right:
                session.Position = Clamp(session.Position + TimeSpan.FromSeconds(5), session.NaturalDuration); e.Handled = true; break;
            case Windows.System.VirtualKey.Up:
                _player.Volume = Math.Min(1.0, _player.Volume + 0.05); e.Handled = true; break;
            case Windows.System.VirtualKey.Down:
                _player.Volume = Math.Max(0.0, _player.Volume - 0.05); e.Handled = true; break;
            case Windows.System.VirtualKey.Space:
                if (session.PlaybackState == MediaPlaybackState.Playing) _player.Pause(); else _player.Play(); e.Handled = true; break;
        }
    }

    public async Task LaunchSelectedFileAsync()
    {
        if (_viewModel.SelectedVideo is null) return;
        var file = await VideoFileStorageService.GetFileAsync(_viewModel.SelectedVideo);
        await Windows.System.Launcher.LaunchFileAsync(file);
    }

    public void Stop(bool removePlayer)
    {
        _operations.Cancel();
        _player?.Pause();
        if (_playerElement is not null) _playerElement.Source = null;
        if (removePlayer)
        {
            _playbackContent.Children.Clear();
            _playerElement = null;
            _player = null;
        }
    }

    public void Dispose()
    {
        Stop(true);
        _operations.Dispose();
    }

    private async Task UpdateSourceAsync()
    {
        var operation = _operations.Start();
        var token = operation.Token;
        _coverArtContent.Visibility = IsPlaybackView ? Visibility.Collapsed : Visibility.Visible;
        _playbackContent.Visibility = IsPlaybackView ? Visibility.Visible : Visibility.Collapsed;
        _updateActionButtonStates();

        if (!IsPlaybackView)
        {
            Stop(true);
            _updateActionButtonStates();
            return;
        }

        if (_playerElement is null)
        {
            _playerElement = new MediaPlayerElement { AreTransportControlsEnabled = true, AutoPlay = true };
            _playbackContent.Children.Add(_playerElement);
            _player = _playerElement.MediaPlayer;
        }
        if (_player is null || _playerElement is null || _viewModel.SelectedVideo is null)
        {
            _updateActionButtonStates();
            return;
        }

        var selectedVideo = _viewModel.SelectedVideo;
        _player.Pause();
        _playerElement.Source = null;
        try
        {
            var file = await VideoFileStorageService.GetFileAsync(selectedVideo);
            token.ThrowIfCancellationRequested();
            if (!operation.IsCurrent || _viewModel.SelectedVideo != selectedVideo || !IsPlaybackView) return;
            _playerElement.Source = MediaSource.CreateFromStorageFile(file);
            _updateActionButtonStates();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            AppLogger.Error("動画の再生準備に失敗しました", ex, selectedVideo.FullPath);
            _viewModel.SetPlaybackError(LanguageManager.GetString("Msg_VideoPlaybackFailed"));
        }
    }

    private static TimeSpan Clamp(TimeSpan position, TimeSpan duration)
        => position < TimeSpan.Zero ? TimeSpan.Zero : duration > TimeSpan.Zero && position > duration ? duration : position;
}
