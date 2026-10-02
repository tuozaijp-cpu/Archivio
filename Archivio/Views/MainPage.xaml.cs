using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Archivio.Models;
using Archivio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Media.Core;
using Windows.Media.Playback;
using CommunityToolkit.WinUI.UI.Controls;

namespace Archivio.Views
{
    public partial class MainPage : Page
    {
        private Flyout? _activeFlyout;
        private CancellationTokenSource? _playbackLoadCts;
        private MediaPlayerElement? _videoPlayerElement;
        private MediaPlayer? _playbackPlayer;
        private bool _isPlaybackView;

        public MainPage()
        {
            this.InitializeComponent();
            DataContext = new MainPageViewModel();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            Unloaded += MainPage_Unloaded;
            VideoThumbnailGridView.ContextFlyout = VideoListDataGrid.ContextFlyout;
            ReleaseDatePicker.MinYear = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
            RestoreLayoutSettings();
            ApplyLocalization();
            InitializeLanguageMenu();
        }

        private void ApplyLocalization()
        {
            try
            {
                // Headers and TextBlocks
                VideoListHeader.Text = LanguageManager.GetString("VideoList_Title");
                DetailsHeader.Text = LanguageManager.GetString("VideoDetails_Title");
                NoCoverArtText.Text = LanguageManager.GetString("Placeholder_NoCoverArt");

                // Buttons
                ChooseFolderButton.Content = LanguageManager.GetString("Button_ChooseFolder");
                RefreshButton.Content = LanguageManager.GetString("Button_Refresh");
                ExportCsvButton.Content = LanguageManager.GetString("Button_ExportCsv");
                CancelRefreshButton.Content = LanguageManager.GetString("Button_Cancel");

                AddImageButton.Content = LanguageManager.GetString("Button_AddImage");
                ReplaceImageButton.Content = LanguageManager.GetString("Button_ReplaceImage");
                DeleteImageButton.Content = LanguageManager.GetString("Button_DeleteImage");
                SaveImageButton.Content = LanguageManager.GetString("Button_SaveImage");

                // Subfolders toggle switch
                ToolTipService.SetToolTip(SubfoldersToggle, LanguageManager.GetString("Subfolders_Search"));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SubfoldersToggle, LanguageManager.GetString("Subfolders_Search"));

                // Context Menu
                RenameMenuItem.Text = LanguageManager.GetString("Menu_Rename");
                DeleteMenuItem.Text = LanguageManager.GetString("Menu_Delete");
                ReMuxMenuItem.Text = LanguageManager.GetString("Menu_ReMux");
                ClearCacheMenuItem.Text = LanguageManager.GetString("Menu_ClearCache");
                LanguageMenu.Text = LanguageManager.GetString("Menu_Language");
                ReadmeMenuItem.Text = LanguageManager.GetString("Menu_Readme");
                AboutMenuItem.Text = LanguageManager.GetString("Menu_About");

                // Details Form Fields Headers
                TitleTextBox.Header = LanguageManager.GetString("Prop_Title");
                ParticipantsTextBox.Header = LanguageManager.GetString("Prop_Participants");
                ReleaseDatePicker.Header = LanguageManager.GetString("Prop_ReleaseDate");
                CatalogNumberTextBox.Header = LanguageManager.GetString("Prop_CatalogNumber");
                RatingComboBox.Header = LanguageManager.GetString("Prop_Rating");
                PublisherTextBox.Header = LanguageManager.GetString("Prop_Publisher");
                ContentDistributorTextBox.Header = LanguageManager.GetString("Prop_ContentDistributor");
                CategoryTextBox.Header = LanguageManager.GetString("Prop_Category");
                CommentTextBox.Header = LanguageManager.GetString("Prop_Comment");
                SaveMetadataButton.Content = LanguageManager.GetString("Button_Save");

                // Technical Panel Fields Headers
                DurationTextBox.Header = LanguageManager.GetString("Col_Duration");
                FrameWidthTextBox.Header = LanguageManager.GetString("Col_FrameWidth");
                FrameHeightTextBox.Header = LanguageManager.GetString("Col_FrameHeight");
                FrameRateTextBox.Header = LanguageManager.GetString("Col_FrameRate");
                VideoBitrateTextBox.Header = LanguageManager.GetString("Col_VideoBitrate");
                VideoCompressionTextBox.Header = LanguageManager.GetString("Col_VideoCompression");
                AudioSampleRateTextBox.Header = LanguageManager.GetString("Col_AudioSampleRate");
                AudioBitrateTextBox.Header = LanguageManager.GetString("Col_AudioBitrate");
                AudioFormatTextBox.Header = LanguageManager.GetString("Col_AudioFormat");

                // Rating items
                if (RatingComboBox.Items.Count >= 6)
                {
                    ((ComboBoxItem)RatingComboBox.Items[0]).Content = LanguageManager.GetString("Rating_0");
                    ((ComboBoxItem)RatingComboBox.Items[1]).Content = LanguageManager.GetString("Rating_1");
                    ((ComboBoxItem)RatingComboBox.Items[2]).Content = LanguageManager.GetString("Rating_2");
                    ((ComboBoxItem)RatingComboBox.Items[3]).Content = LanguageManager.GetString("Rating_3");
                    ((ComboBoxItem)RatingComboBox.Items[4]).Content = LanguageManager.GetString("Rating_4");
                    ((ComboBoxItem)RatingComboBox.Items[5]).Content = LanguageManager.GetString("Rating_5");
                }

                // Localize DataGrid columns
                foreach (var column in VideoListDataGrid.Columns)
                {
                    var tag = column.Tag as string;
                    if (!string.IsNullOrEmpty(tag))
                    {
                        column.Header = LanguageManager.GetString($"Col_{tag}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("UIのローカライズ適用に失敗しました", ex);
            }
        }

        private void InitializeLanguageMenu()
        {
            try
            {
                LanguageMenu.Items.Clear();
                var localeDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Locale");
                if (Directory.Exists(localeDir))
                {
                    var files = Directory.GetFiles(localeDir, "*.json");
                    foreach (var file in files)
                    {
                        var langCode = Path.GetFileNameWithoutExtension(file);
                        string langName = langCode;
                        try
                        {
                            var json = File.ReadAllText(file);
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("LanguageName", out var prop))
                            {
                                langName = prop.GetString() ?? langCode;
                            }
                        }
                        catch { }

                        var menuItem = new MenuFlyoutItem
                        {
                            Text = langName,
                            Tag = langCode
                        };
                        menuItem.Click += LanguageMenu_Click;
                        LanguageMenu.Items.Add(menuItem);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("言語メニューの初期化に失敗しました", ex);
            }
        }

        private void LanguageMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuFlyoutItem menuItem && menuItem.Tag is string langCode)
            {
                var settings = SettingsManager.LoadSettings();
                settings.Language = langCode;
                SettingsManager.SaveSettings(settings);

                LanguageManager.LoadLanguage(langCode);

                ApplyLocalization();
                InitializeLanguageMenu();

                // Refresh videos list so column contents and statuses refresh
                _ = ViewModel.RefreshFilesAsync();
            }
        }

        private void RestoreLayoutSettings()
        {
            try
            {
                var settings = SettingsManager.LoadSettings();
                if (settings.FileListRowHeight > 0)
                {
                    FileListRow.Height = new GridLength(settings.FileListRowHeight, GridUnitType.Pixel);
                }
                if (settings.DetailsRowHeight > 0)
                {
                    DetailsRow.Height = new GridLength(settings.DetailsRowHeight, GridUnitType.Pixel);
                }
                if (settings.CoverArtColumnWidth > 0)
                {
                    CoverArtColumn.Width = new GridLength(settings.CoverArtColumnWidth, GridUnitType.Pixel);
                }
                if (settings.PropertiesColumnWidth > 0)
                {
                    PropertiesColumn.Width = new GridLength(settings.PropertiesColumnWidth, GridUnitType.Pixel);
                }
                if (settings.TechnicalColumnWidth > 0)
                {
                    TechnicalColumn.Width = new GridLength(settings.TechnicalColumnWidth, GridUnitType.Pixel);
                }

                // Restore column order safely
                if (settings.ColumnOrder != null && settings.ColumnOrder.Count > 0)
                {
                    var columnsToRestore = VideoListDataGrid.Columns
                        .Select(c => new { Column = c, Tag = c.Tag as string })
                        .Where(item => !string.IsNullOrEmpty(item.Tag) && settings.ColumnOrder.Contains(item.Tag))
                        .Select(item => new { item.Column, TargetIndex = settings.ColumnOrder.IndexOf(item.Tag!) })
                        .OrderBy(item => item.TargetIndex)
                        .ToList();

                    foreach (var item in columnsToRestore)
                    {
                        int safeIndex = Math.Max(0, Math.Min(item.TargetIndex, VideoListDataGrid.Columns.Count - 1));
                        try
                        {
                            item.Column.DisplayIndex = safeIndex;
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Error($"列 {item.Column.Tag} の表示順の復元に失敗しました", ex);
                        }
                    }
                }

                // Restore column widths
                if (settings.ColumnWidths != null)
                {
                    foreach (var column in VideoListDataGrid.Columns)
                    {
                        var propertyName = column.Tag as string;
                        if (!string.IsNullOrWhiteSpace(propertyName) && settings.ColumnWidths.TryGetValue(propertyName, out var width))
                        {
                            column.Width = new CommunityToolkit.WinUI.UI.Controls.DataGridLength(width);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("レイアウト設定の復元に失敗しました", ex);
            }
        }

        public void SaveLayoutSettings(AppSettings settings)
        {
            try
            {
                settings.FileListRowHeight = FileListRow.Height.Value;
                settings.DetailsRowHeight = DetailsRow.Height.Value;
                settings.CoverArtColumnWidth = CoverArtColumn.Width.Value;
                settings.PropertiesColumnWidth = PropertiesColumn.Width.Value;
                settings.TechnicalColumnWidth = TechnicalColumn.Width.Value;

                // Save column order
                settings.ColumnOrder = VideoListDataGrid.Columns
                    .OrderBy(c => c.DisplayIndex)
                    .Select(c => c.Tag as string)
                    .Where(tag => !string.IsNullOrEmpty(tag))
                    .ToList()!;

                // Save column widths
                settings.ColumnWidths.Clear();
                foreach (var column in VideoListDataGrid.Columns)
                {
                    var propertyName = column.Tag as string;
                    if (!string.IsNullOrWhiteSpace(propertyName))
                    {
                        settings.ColumnWidths[propertyName] = column.ActualWidth;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("レイアウト設定の保存に失敗しました", ex);
            }
        }

        public MainPageViewModel ViewModel => (MainPageViewModel)DataContext;

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _playbackLoadCts?.Cancel();
            _playbackLoadCts?.Dispose();
            _playbackLoadCts = null;
            StopPlayback(removePlayer: true);
        }

        private void StopPlayback(bool removePlayer)
        {
            _playbackPlayer?.Pause();

            if (_videoPlayerElement is not null)
            {
                _videoPlayerElement.Source = null;
            }

            if (removePlayer)
            {
                VideoPlaybackContent.Children.Clear();
                _videoPlayerElement = null;
                _playbackPlayer = null;
            }
        }

        private void DetailViewToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsThumbnailView = false;
            DetailViewToggleButton.IsChecked = true;
            ThumbnailViewToggleButton.IsChecked = false;
        }

        private void ThumbnailViewToggleButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.IsThumbnailView = true;
            DetailViewToggleButton.IsChecked = false;
            ThumbnailViewToggleButton.IsChecked = true;
        }

        private async void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            await ViewModel.Details.SaveMetadataAsync();
        }

        private void ReleaseDatePicker_DateChanged(object sender, DatePickerValueChangedEventArgs args)
        {
            if (ViewModel.SelectedVideo is null)
            {
                return;
            }

            ViewModel.SelectedVideo.ReleaseDate = args.NewDate;
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            await ViewModel.RefreshFilesAsync();
        }

        private async void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.ChooseFolderAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.RefreshFilesAsync();
        }

        private void CancelRefreshButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.CancelRefresh();
        }

        private async void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.ExportVideosToCsvAsync();
        }

        private async void AddImageButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.Details.AddCoverArtImageAsync();
        }

        private async void ReplaceImageButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.Details.ReplaceCoverArtImageAsync();
        }

        private void DeleteImageButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Details.DeleteCoverArtImage();
        }

        private async void DataGrid_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (ViewModel.SelectedVideo is not null)
            {
                var file = await ViewModel.SelectedVideo.GetFileAsync();
                await Windows.System.Launcher.LaunchFileAsync(file);
            }
        }

        private async void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainPageViewModel.SelectedVideo))
            {
                // 別ファイルを選択したら、現在の動画と標準コントロールを終了する。
                _isPlaybackView = false;
                CoverArtViewToggleButton.IsChecked = true;
                VideoPlaybackToggleButton.IsChecked = false;
                await UpdatePlaybackSourceAsync();
            }
        }

        private async void CoverArtViewToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isPlaybackView = false;
            CoverArtViewToggleButton.IsChecked = true;
            VideoPlaybackToggleButton.IsChecked = false;
            await UpdatePlaybackSourceAsync();
        }

        private async void VideoPlaybackToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isPlaybackView = true;
            CoverArtViewToggleButton.IsChecked = false;
            VideoPlaybackToggleButton.IsChecked = true;
            await UpdatePlaybackSourceAsync();
        }

        private async Task UpdatePlaybackSourceAsync()
        {
            _playbackLoadCts?.Cancel();
            _playbackLoadCts?.Dispose();
            _playbackLoadCts = new CancellationTokenSource();
            var cancellationToken = _playbackLoadCts.Token;

            // 表示の切り替えはプレイヤーの生成状態に依存させない。
            CoverArtContent.Visibility = _isPlaybackView ? Visibility.Collapsed : Visibility.Visible;
            VideoPlaybackContent.Visibility = _isPlaybackView ? Visibility.Visible : Visibility.Collapsed;

            if (!_isPlaybackView)
            {
                StopPlayback(removePlayer: true);
                return;
            }

            if (_isPlaybackView && _videoPlayerElement is null)
            {
                _videoPlayerElement = new MediaPlayerElement
                {
                    AreTransportControlsEnabled = true,
                    AutoPlay = true
                };
                VideoPlaybackContent.Children.Add(_videoPlayerElement);
                _playbackPlayer = _videoPlayerElement.MediaPlayer;
            }

            if (_playbackPlayer is null || _videoPlayerElement is null)
            {
                return;
            }

            _playbackPlayer.Pause();
            _videoPlayerElement.Source = null;

            if (ViewModel.SelectedVideo is null)
            {
                return;
            }

            try
            {
                var selectedVideo = ViewModel.SelectedVideo;
                var file = await selectedVideo.GetFileAsync();
                cancellationToken.ThrowIfCancellationRequested();

                if (ViewModel.SelectedVideo != selectedVideo || !_isPlaybackView)
                {
                    return;
                }

                _videoPlayerElement.Source = MediaSource.CreateFromStorageFile(file);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                AppLogger.Error("動画の再生準備に失敗しました", ex, ViewModel.SelectedVideo?.FullPath);
                ViewModel.SetPlaybackError("動画を再生できませんでした。");
            }
        }

        private void VideoThumbnailGridView_ContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
        {
            if (args.TryGetPosition(sender, out Windows.Foundation.Point point))
            {
                var elements = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(point, sender);
                var item = elements.OfType<GridViewItem>().FirstOrDefault();
                VideoThumbnailGridView.SelectedItem = item?.DataContext as VideoFileItem;
            }

            UpdateContextMenuState();
        }

        private async void CoverArt_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (ViewModel.SelectedVideo is not null)
            {
                var file = await ViewModel.SelectedVideo.GetFileAsync();
                await Windows.System.Launcher.LaunchFileAsync(file);
            }
        }

        private void CoverArtFlipView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ViewModel.SelectedVideo is not null && sender is FlipView flipView)
            {
                ViewModel.SelectedVideo.SelectedCoverArtIndex = flipView.SelectedIndex;
            }
        }

        private async void SaveImageButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.Details.SaveCoverArtAsync();
        }

        private void FilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                ViewModel.FilterText = textBox.Text;
            }
        }

        private void VideoListDataGrid_Sorting(object sender, CommunityToolkit.WinUI.UI.Controls.DataGridColumnEventArgs e)
        {
            var dataGrid = (CommunityToolkit.WinUI.UI.Controls.DataGrid)sender;
            var column = e.Column;
            var propertyName = column.Tag as string;

            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return;
            }

            // Determine sort direction
            bool ascending = true;
            if (column.SortDirection == null || column.SortDirection == CommunityToolkit.WinUI.UI.Controls.DataGridSortDirection.Descending)
            {
                ascending = true;
            }
            else
            {
                ascending = false;
            }

            // Clear existing sort directions on other columns
            foreach (var col in dataGrid.Columns)
            {
                col.SortDirection = null;
            }

            // Set sort direction on clicked column
            column.SortDirection = ascending 
                ? CommunityToolkit.WinUI.UI.Controls.DataGridSortDirection.Ascending 
                : CommunityToolkit.WinUI.UI.Controls.DataGridSortDirection.Descending;

            // Perform sort in ViewModel
            ViewModel.Sort(propertyName, ascending);
        }

        private void FilterButton_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            e.Handled = true; // Prevents the parent DataGridColumnHeader from swallowing the click and sorting!
        }

        private void FilterFlyout_Opening(object sender, object e)
        {
            var flyout = sender as Flyout;
            if (flyout == null) return;
            _activeFlyout = flyout;

            var header = FindVisualParent<CommunityToolkit.WinUI.UI.Controls.Primitives.DataGridColumnHeader>(flyout.Target);
            if (header == null || header.Content == null) return;

            var column = VideoListDataGrid.Columns.FirstOrDefault(c => c.Header != null && c.Header.ToString() == header.Content.ToString());
            if (column == null) return;

            var propertyName = column.Tag as string;
            if (string.IsNullOrWhiteSpace(propertyName)) return;

            var stackPanel = flyout.Content as StackPanel;
            if (stackPanel == null) return;

            // Save the property name on the StackPanel Tag so we can find it later
            stackPanel.Tag = propertyName;

            // Reset search textbox inside the flyout
            var searchBox = stackPanel.Children.OfType<TextBox>().FirstOrDefault(t => t.Name == "SearchValuesTextBox");
            if (searchBox != null)
            {
                searchBox.Text = string.Empty;
            }

            // Get unique values for this column from ViewModel
            var uniqueValues = ViewModel.GetUniqueValuesForProperty(propertyName);

            // Get currently active checked values for this column
            var activeFilterValues = ViewModel.GetColumnFilterValues(propertyName);

            var scrollViewer = stackPanel.Children.OfType<ScrollViewer>().FirstOrDefault();
            var itemsControl = scrollViewer?.Content as ItemsControl;
            if (itemsControl == null) return;

            var list = new List<FilterValueItem>();
            foreach (var val in uniqueValues)
            {
                // If there's no active filter, default to checked. Otherwise, check if it's in the active filter list
                bool isChecked = activeFilterValues == null || activeFilterValues.Contains(val);
                list.Add(new FilterValueItem { Value = val, IsChecked = isChecked });
            }

            // Bind to ItemsControl
            itemsControl.ItemsSource = list;
            
            // Save the master list on the itemsControl's Tag for search filtering
            itemsControl.Tag = list;

            // Apply dynamic flyout localization
            LocalizeFilterFlyout(stackPanel);
        }

        private void LocalizeFilterFlyout(StackPanel stackPanel)
        {
            try
            {
                var titleText = stackPanel.Children.OfType<TextBlock>().FirstOrDefault();
                if (titleText != null)
                {
                    titleText.Text = LanguageManager.GetString("Filter_Title");
                }

                var searchBox = stackPanel.Children.OfType<TextBox>().FirstOrDefault(t => t.Name == "SearchValuesTextBox");
                if (searchBox != null)
                {
                    searchBox.PlaceholderText = LanguageManager.GetString("Filter_Search");
                }

                var btnPanel = stackPanel.Children.OfType<StackPanel>().FirstOrDefault();
                if (btnPanel != null)
                {
                    var buttons = btnPanel.Children.OfType<Button>().ToList();
                    if (buttons.Count >= 2)
                    {
                        buttons[0].Content = LanguageManager.GetString("Filter_SelectAll");
                        buttons[1].Content = LanguageManager.GetString("Filter_ClearAll");
                    }
                }

                var grid = stackPanel.Children.OfType<Grid>().FirstOrDefault();
                if (grid != null)
                {
                    var buttons = grid.Children.OfType<Button>().ToList();
                    if (buttons.Count >= 2)
                    {
                        buttons[0].Content = LanguageManager.GetString("Filter_Apply");
                        buttons[1].Content = LanguageManager.GetString("Filter_Clear");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("フィルターフライアウトのローカライズ適用に失敗しました", ex);
            }
        }

        private void SearchValuesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null) return;

            var presenter = FindVisualParent<FlyoutPresenter>(textBox);
            if (presenter == null) return;

            var itemsControl = FindVisualChild<ItemsControl>(presenter);
            if (itemsControl == null) return;

            var masterList = itemsControl.Tag as List<FilterValueItem>;
            if (masterList == null) return;

            var search = textBox.Text.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(search))
            {
                itemsControl.ItemsSource = masterList;
            }
            else
            {
                itemsControl.ItemsSource = masterList.Where(i => i.Value.ToLowerInvariant().Contains(search)).ToList();
            }
        }

        private void SelectAllValues_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            var presenter = FindVisualParent<FlyoutPresenter>(button);
            if (presenter == null) return;

            var itemsControl = FindVisualChild<ItemsControl>(presenter);
            if (itemsControl == null) return;

            var list = itemsControl.ItemsSource as IEnumerable<FilterValueItem>;
            if (list != null)
            {
                foreach (var item in list)
                {
                    item.IsChecked = true;
                }
            }
        }

        private void ClearAllValues_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;

            var presenter = FindVisualParent<FlyoutPresenter>(button);
            if (presenter == null) return;

            var itemsControl = FindVisualChild<ItemsControl>(presenter);
            if (itemsControl == null) return;

            var list = itemsControl.ItemsSource as IEnumerable<FilterValueItem>;
            if (list != null)
            {
                foreach (var item in list)
                {
                    item.IsChecked = false;
                }
            }
        }

        private void ApplyFilter_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var stackPanel = FindVisualParent<StackPanel>(button);
            if (stackPanel == null) return;

            var scrollViewer = stackPanel.Children.OfType<ScrollViewer>().FirstOrDefault();
            var itemsControl = scrollViewer?.Content as ItemsControl;
            if (itemsControl == null) return;

            var masterList = itemsControl.Tag as List<FilterValueItem>;
            if (masterList == null) return;

            var propertyName = stackPanel.Tag as string;
            if (string.IsNullOrWhiteSpace(propertyName)) return;

            // Get all checked values
            var checkedValues = masterList.Where(i => i.IsChecked).Select(i => i.Value).ToList();

            // Apply filter in ViewModel
            ViewModel.SetColumnFilterValues(propertyName, checkedValues);

            // Hide the active flyout
            _activeFlyout?.Hide();
        }

        private void ClearFilter_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            var stackPanel = FindVisualParent<StackPanel>(button);
            if (stackPanel == null) return;

            var propertyName = stackPanel.Tag as string;
            if (string.IsNullOrWhiteSpace(propertyName)) return;

            // Clear filter in ViewModel
            ViewModel.ClearColumnFilter(propertyName);

            // Hide the active flyout
            _activeFlyout?.Hide();
        }

        private static T? FindVisualParent<T>(DependencyObject? element) where T : DependencyObject
        {
            if (element is null)
            {
                return null;
            }

            var parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
            while (parent != null)
            {
                if (parent is T parentOfT)
                {
                    return parentOfT;
                }
                parent = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private static T? FindVisualChild<T>(DependencyObject element) where T : DependencyObject
        {
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element); i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, i);
                if (child is T childOfT)
                {
                    return childOfT;
                }
                var nestedChild = FindVisualChild<T>(child);
                if (nestedChild != null)
                {
                    return nestedChild;
                }
            }
            return null;
        }

        private void VideoListDataGrid_ContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
        {
            if (args.TryGetPosition(sender, out Windows.Foundation.Point point))
            {
                var elements = Microsoft.UI.Xaml.Media.VisualTreeHelper.FindElementsInHostCoordinates(point, sender);
                var row = elements.OfType<DataGridRow>().FirstOrDefault();
                if (row != null)
                {
                    VideoListDataGrid.SelectedItem = row.DataContext;
                }
                else
                {
                    // Clicked on empty space: clear selection
                    VideoListDataGrid.SelectedItem = null;
                }
            }

            UpdateContextMenuState();
        }

        private void UpdateContextMenuState()
        {
            // Dynamically enable/disable menu items
            bool isItemSelected = (VideoListDataGrid.SelectedItem != null);
            RenameMenuItem.IsEnabled = isItemSelected;
            DeleteMenuItem.IsEnabled = isItemSelected;
            ReMuxMenuItem.IsEnabled = isItemSelected;

            bool isFolderSelected = !string.IsNullOrWhiteSpace(ViewModel.FolderPath);
            ClearCacheMenuItem.IsEnabled = isFolderSelected;
        }

        private async void RenameMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedVideo = ViewModel.SelectedVideo;
            if (selectedVideo == null) return;

            var textBox = new TextBox
            {
                Text = selectedVideo.FileName,
                SelectionStart = 0,
                SelectionLength = selectedVideo.FileName.Length,
                Width = 300
            };

            var dialog = new ContentDialog
            {
                Title = LanguageManager.GetString("Dialog_Rename_Title"),
                Content = textBox,
                PrimaryButtonText = LanguageManager.GetString("Button_Save"),
                CloseButtonText = LanguageManager.GetString("Button_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                var newName = textBox.Text;
                if (!string.IsNullOrWhiteSpace(newName) && newName != selectedVideo.FileName)
                {
                    await ViewModel.RenameVideoAsync(selectedVideo, newName);
                }
            }
        }

        private async void DeleteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedVideo = ViewModel.SelectedVideo;
            if (selectedVideo == null) return;

            var dialog = new ContentDialog
            {
                Title = LanguageManager.GetString("Dialog_Delete_Title"),
                Content = LanguageManager.GetString("Dialog_Delete_Message", selectedVideo.FileName),
                PrimaryButtonText = LanguageManager.GetString("Button_Delete"),
                CloseButtonText = LanguageManager.GetString("Button_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.DeleteVideoAsync(selectedVideo);
            }
        }

        private async void ReMuxMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedVideo = ViewModel.SelectedVideo;
            if (selectedVideo == null) return;

            await ViewModel.ReMuxVideoAsync(selectedVideo);
        }

        private async void ClearCacheMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new ContentDialog
            {
                Title = LanguageManager.GetString("Dialog_ClearCache_Title"),
                Content = LanguageManager.GetString("Dialog_ClearCache_Message"),
                PrimaryButtonText = LanguageManager.GetString("Button_Clear"),
                CloseButtonText = LanguageManager.GetString("Button_Cancel"),
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                await ViewModel.ClearCurrentFolderCacheAsync();
            }
        }

        private async void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var stackPanel = new StackPanel { Spacing = 12 };
            
            stackPanel.Children.Add(new TextBlock 
            { 
                Text = "Archivio (アルキーヴィオ)", 
                FontSize = 18, 
                FontWeight = Microsoft.UI.Text.FontWeights.Bold 
            });

            stackPanel.Children.Add(new TextBlock 
            { 
                Text = $"{LanguageManager.GetString("Menu_About")} v{GetApplicationVersion()}",
                FontSize = 14 
            });

            stackPanel.Children.Add(new TextBlock 
            { 
                Text = LanguageManager.GetString("Dialog_About_Description"), 
                TextWrapping = TextWrapping.WrapWholeWords,
                FontSize = 13 
            });

            stackPanel.Children.Add(new TextBlock
            {
                Text = "Archivioは無料で利用できます。もしArchivioがお役に立ちましたら今後の開発を応援していただけると嬉しいです。",
                TextWrapping = TextWrapping.WrapWholeWords,
                FontSize = 13
            });

            var supportLink = new HyperlinkButton
            {
                Content = "https://ofuse.me/8679942e",
                NavigateUri = new Uri("https://ofuse.me/8679942e"),
                Padding = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            stackPanel.Children.Add(supportLink);

            var linkPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            linkPanel.Children.Add(new TextBlock { Text = "GitHub:", VerticalAlignment = VerticalAlignment.Center });
            
            var hyperlink = new HyperlinkButton
            {
                Content = "https://github.com/tuozaijp-cpu/Archivio/",
                NavigateUri = new Uri("https://github.com/tuozaijp-cpu/Archivio/"),
                Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center
            };
            linkPanel.Children.Add(hyperlink);
            
            stackPanel.Children.Add(linkPanel);

            var dialog = new ContentDialog
            {
                Title = LanguageManager.GetString("Dialog_About_Title"),
                Content = stackPanel,
                CloseButtonText = LanguageManager.GetString("Button_Close"),
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }

        private static string GetApplicationVersion()
        {
            return typeof(App).Assembly.GetName().Version?.ToString(3) ?? "不明";
        }

        private async void ReadmeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var readmePath = Path.Combine(AppContext.BaseDirectory, "README.md");

            try
            {
                var readmeFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(readmePath);
                await Windows.System.Launcher.LaunchFileAsync(readmeFile);
            }
            catch (Exception)
            {
                var dialog = new ContentDialog
                {
                    Title = LanguageManager.GetString("Dialog_Readme_NotFound_Title"),
                    Content = LanguageManager.GetString("Dialog_Readme_NotFound_Message"),
                    CloseButtonText = LanguageManager.GetString("Button_Close"),
                    XamlRoot = this.XamlRoot
                };

                await dialog.ShowAsync();
            }
        }
    }
}
