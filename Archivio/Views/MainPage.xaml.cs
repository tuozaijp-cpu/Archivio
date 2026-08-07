using System;
using System.Collections.Generic;
using System.Linq;
using Archivio.Models;
using Archivio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using CommunityToolkit.WinUI.UI.Controls;

namespace Archivio.Views
{
    public partial class MainPage : Page
    {
        private Flyout? _activeFlyout;

        public MainPage()
        {
            this.InitializeComponent();
            DataContext = new MainPageViewModel();
            ReleaseDatePicker.MinYear = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
            RestoreLayoutSettings();
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
            if (ViewModel.SelectedVideo?.File is not null)
            {
                await Windows.System.Launcher.LaunchFileAsync(ViewModel.SelectedVideo.File);
            }
        }

        private async void CoverArt_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            if (ViewModel.SelectedVideo?.File is not null)
            {
                await Windows.System.Launcher.LaunchFileAsync(ViewModel.SelectedVideo.File);
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
                    args.Handled = true;
                }
            }
            else
            {
                // Keyboard trigger: only allow if there is an item selected
                if (VideoListDataGrid.SelectedItem == null)
                {
                    args.Handled = true;
                }
            }
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
                Title = "ファイル名の変更",
                Content = textBox,
                PrimaryButtonText = "変更",
                CloseButtonText = "キャンセル",
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
                Title = "ファイルの削除確認",
                Content = $"本当に「{selectedVideo.FileName}」を削除しますか？\nこの操作は取り消せません。",
                PrimaryButtonText = "削除",
                CloseButtonText = "キャンセル",
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
    }
}
