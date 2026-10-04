using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Archivio.Controls;
using Archivio.Models;
using Archivio.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
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
        private bool _isRefreshingCustomFields;
        private static MetadataFieldDefinition[] ListFieldDefinitions => MetadataFieldCatalog.ListFields.ToArray();
        private static MetadataFieldDefinition[] DetailFieldDefinitions => MetadataFieldCatalog.DetailFields.ToArray();
        private readonly Dictionary<string, FrameworkElement> _detailControls = new(StringComparer.Ordinal);

        public MainPage()
        {
            this.InitializeComponent();
            DataContext = new MainPageViewModel();
            BuildDynamicEditableFields();
            RegisterDetailControls();
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            Unloaded += MainPage_Unloaded;
            VideoThumbnailGridView.ContextFlyout = VideoListDataGrid.ContextFlyout;
            BuildVideoListColumns();
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
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(
                    VideoThumbnailGridView,
                    LanguageManager.GetString("ThumbnailList_Name"));

                // Buttons
                ChooseFolderButton.Content = LanguageManager.GetString("Button_ChooseFolder");
                RefreshButton.Content = LanguageManager.GetString("Button_Refresh");
                ExportCsvButton.Content = LanguageManager.GetString("Button_ExportCsv");
                CancelRefreshButton.Content = LanguageManager.GetString("Button_Cancel");
                ConfigureColumnsButton.Content = LanguageManager.GetString("Button_ChooseColumns");
                ConfigureDetailsButton.Content = LanguageManager.GetString("Button_ChooseDetails");
                ConfigureCustomFieldsButton.Content = LanguageManager.GetString("Button_CustomFields");
                DetailViewToggleButton.Content = LanguageManager.GetString("Button_DetailView");
                ThumbnailViewToggleButton.Content = LanguageManager.GetString("Button_ThumbnailView");
                VideoPlaybackToggleButton.Content = LanguageManager.GetString("Button_Play");
                CoverArtViewToggleButton.Content = LanguageManager.GetString("Button_CoverArtView");
                ToolTipService.SetToolTip(ThumbnailTileSizeComboBox, LanguageManager.GetString("ThumbnailTileSize"));
                SearchTextBox.PlaceholderText = LanguageManager.GetString("Search_Placeholder");
                ToolTipService.SetToolTip(SearchTextBox, LanguageManager.GetString("Search_Tooltip"));

                if (ThumbnailTileSizeComboBox.Items.Count >= 4)
                {
                    ((ComboBoxItem)ThumbnailTileSizeComboBox.Items[0]).Content = LanguageManager.GetString("ThumbnailSize_Small");
                    ((ComboBoxItem)ThumbnailTileSizeComboBox.Items[1]).Content = LanguageManager.GetString("ThumbnailSize_Standard");
                    ((ComboBoxItem)ThumbnailTileSizeComboBox.Items[2]).Content = LanguageManager.GetString("ThumbnailSize_Large");
                    ((ComboBoxItem)ThumbnailTileSizeComboBox.Items[3]).Content = LanguageManager.GetString("ThumbnailSize_ExtraLarge");
                }

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

                // Details form field headers are derived from MetadataFieldCatalog.
                ApplyDetailControlLocalization();
                SaveMetadataButton.Content = LanguageManager.GetString("Button_Save");

                // Technical Panel Fields Headers
                SetControlHeader(DurationTextBox, "Col_Duration");
                SetControlHeader(FrameWidthTextBox, "Col_FrameWidth");
                SetControlHeader(FrameHeightTextBox, "Col_FrameHeight");
                SetControlHeader(FrameRateTextBox, "Col_FrameRate");
                SetControlHeader(VideoBitrateTextBox, "Col_VideoBitrate");
                SetControlHeader(VideoCompressionTextBox, "Col_VideoCompression");
                SetControlHeader(AudioSampleRateTextBox, "Col_AudioSampleRate");
                SetControlHeader(AudioBitrateTextBox, "Col_AudioBitrate");
                SetControlHeader(AudioFormatTextBox, "Col_AudioFormat");

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
                    if (column.Tag is string fieldId
                        && MetadataFieldCatalog.FindById(fieldId) is { } field)
                    {
                        column.Header = field.IsCustom ? field.CustomDisplayName : LanguageManager.GetString(field.LocalizationKey);
                    }
                }

                if (_activeFlyout?.Content is StackPanel filterPanel)
                {
                    LocalizeFilterFlyout(filterPanel);
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

        private void RegisterDetailControls()
        {
            foreach (var field in DetailFieldDefinitions)
            {
                if (field.DetailsElementName is not null
                    && FindName(field.DetailsElementName) is FrameworkElement control)
                {
                    _detailControls[field.Id] = control;
                }
            }
        }

        private void BuildDynamicEditableFields()
        {
            var dynamicFields = DetailFieldDefinitions
                .Where(field => (field.IsEditable || field.IsCustom)
                    && field.Category != MetadataFieldCategory.Artwork
                    && (field.IsCustom || field.ValueType is MetadataFieldValueType.Text
                        or MetadataFieldValueType.MultipleText
                        or MetadataFieldValueType.Identifier
                        or MetadataFieldValueType.Date))
                .ToArray();

            foreach (var field in dynamicFields)
            {
                if (field.IsCustom)
                {
                    var customControl = CreateCustomDetailControl(field);
                    _detailControls[field.Id] = customControl;
                    EditableMetadataFieldsPanel.Children.Insert(
                        Math.Max(0, EditableMetadataFieldsPanel.Children.Count - 2), customControl);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(field.DetailsBindingPath)
                    || _detailControls.ContainsKey(field.Id))
                {
                    continue;
                }

                FrameworkElement control;
                var bindingPath = new PropertyPath($"SelectedVideo.{field.DetailsBindingPath}");
                if (field.ValueType == MetadataFieldValueType.Date)
                {
                    var datePicker = new DatePicker
                    {
                        MinYear = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero),
                        Tag = field.Id
                    };
                    datePicker.SetBinding(DatePicker.DateProperty, new Binding
                    {
                        Path = bindingPath,
                        Mode = BindingMode.OneWay
                    });
                    datePicker.DateChanged += ReleaseDatePicker_DateChanged;
                    control = datePicker;
                }
                else if (field.ValueType == MetadataFieldValueType.MultipleText)
                {
                    var multipleTextEditor = new MultipleTextEditor();
                    multipleTextEditor.SetBinding(MultipleTextEditor.ValueProperty, new Binding
                    {
                        Path = bindingPath,
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    });
                    control = multipleTextEditor;
                }
                else
                {
                    var textBox = new TextBox
                    {
                        Tag = field.Id
                    };
                    textBox.SetBinding(TextBox.TextProperty, new Binding
                    {
                        Path = bindingPath,
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    });
                    control = textBox;
                }

                _detailControls[field.Id] = control;
                EditableMetadataFieldsPanel.Children.Insert(
                    Math.Max(0, EditableMetadataFieldsPanel.Children.Count - 2),
                    control);
            }
        }

        private FrameworkElement CreateCustomDetailControl(MetadataFieldDefinition field)
        {
            if (field.ValueType == MetadataFieldValueType.Date)
            {
                var picker = new DatePicker { Tag = field.Id, MinYear = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero), IsEnabled = field.IsEditable };
                if (field.IsEditable) picker.DateChanged += (_, args) => SetCustomFieldValue(field, args.NewDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                return picker;
            }

            if (field.ValueType is MetadataFieldValueType.Boolean or MetadataFieldValueType.Rating)
            {
                var combo = new ComboBox { Tag = field.Id, MinWidth = 180, IsEnabled = field.IsEditable };
                if (field.ValueType == MetadataFieldValueType.Boolean)
                {
                    combo.Items.Add(new ComboBoxItem { Content = "—", Tag = null });
                    combo.Items.Add(new ComboBoxItem { Content = "True", Tag = true });
                    combo.Items.Add(new ComboBoxItem { Content = "False", Tag = false });
                }
                else
                {
                    for (var rating = 0; rating <= 5; rating++) combo.Items.Add(new ComboBoxItem { Content = rating.ToString(), Tag = rating });
                }
                if (field.IsEditable) combo.SelectionChanged += (_, _) =>
                    { if (combo.SelectedItem is ComboBoxItem selected) SetCustomFieldValue(field, selected.Tag?.ToString() ?? string.Empty); };
                return combo;
            }

            var textBox = new TextBox { Tag = field.Id, AcceptsReturn = field.ValueType is MetadataFieldValueType.MultipleText, IsReadOnly = !field.IsEditable };
            if (field.IsEditable) textBox.TextChanged += (_, _) => SetCustomFieldValue(field, textBox.Text);
            return textBox;
        }

        private void SetCustomFieldValue(MetadataFieldDefinition field, string value)
        {
            if (_isRefreshingCustomFields) return;
            var item = ViewModel.SelectedVideo;
            if (item is null || item.StructuredMetadata is null) return;
            var id = field.CustomFieldId ?? field.Id;
            if (!item.StructuredMetadata.CustomFields.TryGetValue(id, out var metadataValue))
            {
                metadataValue = new MetadataValue();
                item.StructuredMetadata.CustomFields[id] = metadataValue;
            }

            metadataValue.Kind = field.ValueType switch
            {
                MetadataFieldValueType.MultipleText => MetadataValueKind.MultipleText,
                MetadataFieldValueType.Date => MetadataValueKind.Date,
                MetadataFieldValueType.Integer => MetadataValueKind.Integer,
                MetadataFieldValueType.Decimal => MetadataValueKind.Decimal,
                MetadataFieldValueType.Boolean => MetadataValueKind.Boolean,
                MetadataFieldValueType.Rating => MetadataValueKind.Rating,
                _ => MetadataValueKind.Text
            };
            metadataValue.Text = value;
            metadataValue.TextValues = field.ValueType == MetadataFieldValueType.MultipleText
                ? value.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(entry => entry.Trim()).ToList()
                : new List<string>();
            if (field.ValueType == MetadataFieldValueType.Integer && long.TryParse(value, out var integer)) metadataValue.Integer = integer;
            if (field.ValueType == MetadataFieldValueType.Decimal && decimal.TryParse(value, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var decimalValue)) metadataValue.Decimal = decimalValue;
            if (field.ValueType == MetadataFieldValueType.Rating && decimal.TryParse(value, out var rating)) metadataValue.Rating = rating;
            if (field.ValueType == MetadataFieldValueType.Boolean && bool.TryParse(value, out var boolean)) metadataValue.Boolean = boolean;
            if (field.ValueType == MetadataFieldValueType.Date && DateTimeOffset.TryParse(value, out var date)) metadataValue.Date = date;
            item.MarkCustomMetadataFieldChanged(id);
        }

        private void RefreshCustomDetailControls()
        {
            var item = ViewModel.SelectedVideo;
            _isRefreshingCustomFields = true;
            try
            {
                foreach (var field in DetailFieldDefinitions.Where(field => field.IsCustom))
                {
                    if (!_detailControls.TryGetValue(field.Id, out var control)) continue;
                    MetadataValue? value = null;
                    if (item is not null) item.StructuredMetadata.CustomFields.TryGetValue(field.Id, out value);
                    var text = value?.Text ?? value?.Date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                        ?? value?.Integer?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        ?? value?.Decimal?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        ?? value?.Rating?.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        ?? value?.Boolean?.ToString() ?? string.Join(Environment.NewLine, value?.TextValues ?? new List<string>());
                    switch (control)
                    {
                        case TextBox textBox when textBox.Text != text:
                            textBox.Text = text;
                            break;
                        case DatePicker picker:
                            picker.Date = value?.Date ?? DateTimeOffset.Now;
                            break;
                        case ComboBox combo when field.ValueType == MetadataFieldValueType.Boolean:
                            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(option => string.Equals(option.Tag?.ToString(), value?.Boolean?.ToString(), StringComparison.OrdinalIgnoreCase));
                            break;
                        case ComboBox combo when field.ValueType == MetadataFieldValueType.Rating:
                            combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(option => option.Tag?.ToString() == text);
                            break;
                    }
                }
            }
            finally { _isRefreshingCustomFields = false; }
        }

        private void ApplyDetailControlLocalization()
        {
            foreach (var field in DetailFieldDefinitions)
            {
                if (_detailControls.TryGetValue(field.Id, out var control))
                {
                    SetControlHeader(control, field.LocalizationKey);
                }
            }
        }

        private static void SetControlHeader(FrameworkElement control, string localizationKey)
        {
            var header = LanguageManager.GetString(localizationKey);
            switch (control)
            {
                case TextBox textBox:
                    textBox.Header = header;
                    break;
                case DatePicker datePicker:
                    datePicker.Header = header;
                    break;
                case ComboBox comboBox:
                    comboBox.Header = header;
                    break;
                case MultipleTextEditor multipleTextEditor:
                    multipleTextEditor.Header = header;
                    break;
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
                ViewModel.RefreshLocalizedText();

                // Refresh videos list so column contents and statuses refresh
                _ = ViewModel.RefreshFilesAsync();
            }
        }

        private void RestoreLayoutSettings()
        {
            try
            {
                var settings = SettingsManager.LoadSettings();
                settings.ColumnWidths ??= new Dictionary<string, double>(StringComparer.Ordinal);
                settings.ColumnOrder ??= new List<string>();
                settings.ListFieldWidths ??= new Dictionary<string, double>(StringComparer.Ordinal);
                settings.ListFieldOrder ??= new List<string>();
                settings.DetailFieldOrder ??= new List<string>();
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

                var visibleFieldIds = NormalizeVisibleListFieldIds(settings.VisibleListFieldIds);

                foreach (var column in VideoListDataGrid.Columns)
                {
                    if (column.Tag is not string fieldId
                        || MetadataFieldCatalog.FindById(fieldId) is not { } field)
                    {
                        continue;
                    }

                    column.Visibility = visibleFieldIds.Contains(fieldId) ? Visibility.Visible : Visibility.Collapsed;
                    if (settings.ListFieldWidths.TryGetValue(fieldId, out var savedWidth)
                        && double.IsFinite(savedWidth)
                        && savedWidth > 0)
                    {
                        column.Width = new CommunityToolkit.WinUI.UI.Controls.DataGridLength(savedWidth);
                    }
                    else if (!string.IsNullOrWhiteSpace(field.ExistingColumnTag)
                        && settings.ColumnWidths.TryGetValue(field.ExistingColumnTag, out var legacyWidth)
                        && double.IsFinite(legacyWidth)
                        && legacyWidth > 0)
                    {
                        column.Width = new CommunityToolkit.WinUI.UI.Controls.DataGridLength(legacyWidth);
                    }
                }

                var savedOrder = settings.ListFieldOrder;
                if (savedOrder.Count == 0 && settings.ColumnOrder.Count > 0)
                {
                    var legacyIds = ListFieldDefinitions
                        .Where(field => field.ExistingColumnTag is not null)
                        .ToDictionary(field => field.ExistingColumnTag!, field => field.Id, StringComparer.Ordinal);
                    savedOrder = settings.ColumnOrder
                        .Where(legacyIds.ContainsKey)
                        .Select(tag => legacyIds[tag])
                        .ToList();
                }

                var normalizedOrder = NormalizeColumnOrder(savedOrder);
                for (var index = 0; index < normalizedOrder.Count; index++)
                {
                    var column = VideoListDataGrid.Columns.FirstOrDefault(candidate =>
                        string.Equals(candidate.Tag as string, normalizedOrder[index], StringComparison.Ordinal));
                    if (column is null)
                    {
                        continue;
                    }

                    try
                    {
                        column.DisplayIndex = index;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Error($"列 {column.Tag} の表示順の復元に失敗しました", ex);
                    }
                }

                ApplyDetailFieldSettings(settings);
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
                if (CoverArtPane.Visibility == Visibility.Visible
                    && CoverArtColumn.Width.GridUnitType == GridUnitType.Pixel)
                {
                    settings.CoverArtColumnWidth = CoverArtColumn.Width.Value;
                }
                settings.PropertiesColumnWidth = PropertiesColumn.Width.Value;
                settings.TechnicalColumnWidth = TechnicalColumn.Width.Value;

                settings.ListFieldOrder = NormalizeColumnOrder(VideoListDataGrid.Columns
                    .OrderBy(c => c.DisplayIndex)
                    .Select(c => c.Tag as string)
                    .Where(tag => !string.IsNullOrEmpty(tag))
                    .ToList()!);

                var visibleFieldIds = VideoListDataGrid.Columns
                    .Where(column => column.Visibility == Visibility.Visible)
                    .Select(column => column.Tag as string)
                    .Where(fieldId => !string.IsNullOrEmpty(fieldId))
                    .Distinct(StringComparer.Ordinal)
                    .ToList()!;
                settings.VisibleListFieldIds = NormalizeVisibleListFieldIds(visibleFieldIds).ToList();

                settings.ListFieldWidths ??= new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var column in VideoListDataGrid.Columns)
                {
                    if (column.Tag is string fieldId
                        && MetadataFieldCatalog.FindById(fieldId) is { } field)
                    {
                        var previousWidth = settings.ListFieldWidths.TryGetValue(fieldId, out var width)
                            ? width
                            : field.DefaultListWidth;
                        settings.ListFieldWidths[fieldId] = column.ActualWidth > 0
                            ? column.ActualWidth
                            : previousWidth;
                    }
                }

                settings.DetailFieldOrder = GetDisplayedDetailFieldOrder();
                settings.VisibleDetailFieldIds = GetVisibleDetailFieldIds();
            }
            catch (Exception ex)
            {
                AppLogger.Error("レイアウト設定の保存に失敗しました", ex);
            }
        }

        private void BuildVideoListColumns()
        {
            VideoListDataGrid.Columns.Clear();
            var filterStyle = Resources["FilteringColumnHeaderStyle"] as Style;
            foreach (var field in ListFieldDefinitions)
            {
                var column = new CommunityToolkit.WinUI.UI.Controls.DataGridTextColumn
                {
                    Binding = new Binding
                    {
                        Path = new PropertyPath(field.IsCustom ? "." : field.ListBindingPath),
                        Mode = BindingMode.OneWay,
                        Converter = field.IsCustom ? CustomMetadataListValueConverter.Instance : null,
                        ConverterParameter = field.CustomFieldId
                    },
                    Header = field.IsCustom ? field.CustomDisplayName : LanguageManager.GetString(field.LocalizationKey),
                    IsReadOnly = true,
                    Width = new CommunityToolkit.WinUI.UI.Controls.DataGridLength(field.DefaultListWidth),
                    Tag = field.Id,
                    Visibility = field.IsInListByDefault ? Visibility.Visible : Visibility.Collapsed
                };
                if (field.IsFilterable)
                {
                    column.HeaderStyle = filterStyle;
                }

                VideoListDataGrid.Columns.Add(column);
            }
        }

        private static HashSet<string> NormalizeVisibleListFieldIds(IEnumerable<string?>? requestedIds)
        {
            var knownIds = ListFieldDefinitions.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
            var normalized = (requestedIds ?? Array.Empty<string>())
                .Where(fieldId => fieldId is not null && knownIds.Contains(fieldId))
                .Select(fieldId => fieldId!)
                .ToHashSet(StringComparer.Ordinal);

            if (normalized.Count > 0)
            {
                return normalized;
            }

            normalized = ListFieldDefinitions
                .Where(field => field.IsInListByDefault)
                .Select(field => field.Id)
                .ToHashSet(StringComparer.Ordinal);

            if (normalized.Count == 0 && ListFieldDefinitions.Length > 0)
            {
                normalized.Add(ListFieldDefinitions[0].Id);
            }

            return normalized;
        }

        private static List<string> NormalizeColumnOrder(IEnumerable<string>? requestedOrder)
        {
            var normalized = new List<string>();
            var knownIds = ListFieldDefinitions.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var fieldId in requestedOrder ?? Array.Empty<string>())
            {
                if (knownIds.Contains(fieldId) && seen.Add(fieldId))
                {
                    normalized.Add(fieldId);
                }
            }

            foreach (var field in ListFieldDefinitions)
            {
                if (seen.Add(field.Id))
                {
                    normalized.Add(field.Id);
                }
            }

            return normalized;
        }

        private static List<string> NormalizeDetailFieldOrder(IEnumerable<string>? requestedOrder)
        {
            var normalized = new List<string>();
            var knownIds = DetailFieldDefinitions.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var fieldId in requestedOrder ?? Array.Empty<string>())
            {
                if (knownIds.Contains(fieldId) && seen.Add(fieldId))
                {
                    normalized.Add(fieldId);
                }
            }

            foreach (var field in DetailFieldDefinitions)
            {
                if (seen.Add(field.Id))
                {
                    normalized.Add(field.Id);
                }
            }

            return normalized;
        }

        private static HashSet<string> NormalizeVisibleDetailFieldIds(IEnumerable<string?>? requestedIds)
        {
            var knownIds = DetailFieldDefinitions.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
            var normalized = (requestedIds ?? Array.Empty<string?>())
                .Where(fieldId => fieldId is not null && knownIds.Contains(fieldId))
                .Select(fieldId => fieldId!)
                .ToHashSet(StringComparer.Ordinal);

            if (normalized.Count == 0)
            {
                normalized = DetailFieldDefinitions
                    .Where(field => field.IsInDetailsByDefault)
                    .Select(field => field.Id)
                    .ToHashSet(StringComparer.Ordinal);
            }

            if (normalized.Count == 0 && DetailFieldDefinitions.Length > 0)
            {
                normalized.Add(DetailFieldDefinitions[0].Id);
            }

            return normalized;
        }

        private void ApplyDetailFieldSettings(AppSettings settings)
        {
            var order = NormalizeDetailFieldOrder(settings.DetailFieldOrder);
            var visibleIds = NormalizeVisibleDetailFieldIds(settings.VisibleDetailFieldIds);

            var editableFields = DetailFieldDefinitions
                .Where(field => (field.IsEditable || field.IsCustom) && field.Category != MetadataFieldCategory.Artwork)
                .ToArray();
            var technicalFields = DetailFieldDefinitions
                .Where(field => field.Category == MetadataFieldCategory.Technical)
                .ToArray();
            ApplyDetailFieldOrder(EditableMetadataFieldsPanel, editableFields, order, visibleIds);
            ApplyDetailFieldOrder(TechnicalMetadataFieldsPanel, technicalFields, order, visibleIds);

            var coverField = DetailFieldDefinitions.First(field => field.Category == MetadataFieldCategory.Artwork);
            var showCoverArt = visibleIds.Contains(coverField.Id);
            CoverArtPane.Visibility = showCoverArt ? Visibility.Visible : Visibility.Collapsed;
            CoverArtGridSplitter.Visibility = showCoverArt ? Visibility.Visible : Visibility.Collapsed;
            if (showCoverArt)
            {
                CoverArtSplitterColumn.Width = new GridLength(12);
                CoverArtColumn.Width = settings.CoverArtColumnWidth > 0 && double.IsFinite(settings.CoverArtColumnWidth)
                    ? new GridLength(settings.CoverArtColumnWidth, GridUnitType.Pixel)
                    : new GridLength(6, GridUnitType.Star);
            }
            else
            {
                CoverArtColumn.Width = new GridLength(0);
                CoverArtSplitterColumn.Width = new GridLength(0);
            }

            settings.DetailFieldOrder = order;
            settings.VisibleDetailFieldIds = visibleIds.ToList();
        }

        private void ApplyDetailFieldOrder(
            StackPanel panel,
            IReadOnlyCollection<MetadataFieldDefinition> fields,
            IReadOnlyList<string> order,
            HashSet<string> visibleIds)
        {
            var fieldIds = fields.Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
            var controls = fields
                .Where(field => _detailControls.ContainsKey(field.Id))
                .ToDictionary(field => field.Id, field => _detailControls[field.Id], StringComparer.Ordinal);
            var controlSet = controls.Values.Cast<UIElement>().ToHashSet();
            var otherChildren = panel.Children
                .Where(child => !controlSet.Contains(child))
                .ToArray();

            panel.Children.Clear();
            foreach (var fieldId in order.Where(fieldIds.Contains))
            {
                if (!controls.TryGetValue(fieldId, out var control))
                {
                    continue;
                }

                control.Tag = fieldId;
                control.Visibility = visibleIds.Contains(fieldId) ? Visibility.Visible : Visibility.Collapsed;
                panel.Children.Add(control);
            }

            foreach (var child in otherChildren)
            {
                panel.Children.Add(child);
            }
        }

        private List<string> GetDisplayedDetailFieldOrder()
        {
            return GetDetailPanelOrder(EditableMetadataFieldsPanel)
                .Concat(GetDetailPanelOrder(TechnicalMetadataFieldsPanel))
                .Concat(DetailFieldDefinitions
                    .Where(field => field.Category == MetadataFieldCategory.Artwork)
                    .Select(field => field.Id))
                .ToList();
        }

        private List<string> GetVisibleDetailFieldIds()
        {
            var visibleIds = new List<string>();
            foreach (var field in DetailFieldDefinitions)
            {
                var isVisible = field.Category == MetadataFieldCategory.Artwork
                    ? CoverArtPane.Visibility == Visibility.Visible
                    : _detailControls.TryGetValue(field.Id, out var control) && control.Visibility == Visibility.Visible;
                if (isVisible)
                {
                    visibleIds.Add(field.Id);
                }
            }

            return visibleIds;
        }

        private static IEnumerable<string> GetDetailPanelOrder(StackPanel panel)
        {
            return panel.Children
                .OfType<FrameworkElement>()
                .Select(element => element.Tag as string)
                .Where(fieldId => !string.IsNullOrWhiteSpace(fieldId))!;
        }

        private async void ConfigureDetailsButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsManager.LoadSettings();
            var detailOrder = NormalizeDetailFieldOrder(settings.DetailFieldOrder);
            var visibleIds = NormalizeVisibleDetailFieldIds(GetVisibleDetailFieldIds());
            var fieldGroup = CreateDetailFieldGroup(DetailFieldDefinitions, visibleIds, detailOrder);

            var validationText = new TextBlock
            {
                Text = LanguageManager.GetString("Dialog_DetailAtLeastOne"),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(fieldGroup);
            content.Children.Add(validationText);

            var dialog = new ContentDialog
            {
                Title = LanguageManager.GetString("Dialog_ChooseDetails_Title"),
                Content = new ScrollViewer { MaxHeight = 560, Content = content },
                PrimaryButtonText = LanguageManager.GetString("Button_Save"),
                CloseButtonText = LanguageManager.GetString("Button_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (GetCheckedDetailOptionIds(fieldGroup).Count > 0)
                {
                    return;
                }

                validationText.Visibility = Visibility.Visible;
                args.Cancel = true;
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            settings.VisibleDetailFieldIds = NormalizeVisibleDetailFieldIds(
                GetCheckedDetailOptionIds(fieldGroup)).ToList();
            settings.DetailFieldOrder = GetDetailOptionIds(fieldGroup);
            ApplyDetailFieldSettings(settings);
            SaveLayoutSettings(settings);
            SettingsManager.SaveSettings(settings);
        }

        private static StackPanel CreateDetailFieldGroup(
            IReadOnlyCollection<MetadataFieldDefinition> fields,
            HashSet<string> visibleIds,
            IReadOnlyList<string> detailOrder)
        {
            var group = new StackPanel { Spacing = 4 };
            var rows = new StackPanel { Spacing = 2 };
            group.Tag = rows;
            var orderIndex = detailOrder
                .Select((fieldId, index) => (fieldId, index))
                .ToDictionary(item => item.fieldId, item => item.index, StringComparer.Ordinal);
            foreach (var field in fields.OrderBy(field => orderIndex.TryGetValue(field.Id, out var index) ? index : int.MaxValue))
            {
                var row = new Grid { Tag = field.Id };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(new CheckBox
                {
                    Content = GetDetailFieldDisplayName(field),
                    IsChecked = visibleIds.Contains(field.Id),
                    VerticalAlignment = VerticalAlignment.Center
                });

                var moveUpButton = new Button { Content = "↑", Width = 36, Height = 32, Padding = new Thickness(0) };
                ToolTipService.SetToolTip(moveUpButton, LanguageManager.GetString("Button_MoveUp"));
                moveUpButton.Click += (_, _) => MoveDetailOption(rows, row, -1);
                Grid.SetColumn(moveUpButton, 1);
                row.Children.Add(moveUpButton);

                var moveDownButton = new Button { Content = "↓", Width = 36, Height = 32, Padding = new Thickness(0) };
                ToolTipService.SetToolTip(moveDownButton, LanguageManager.GetString("Button_MoveDown"));
                moveDownButton.Click += (_, _) => MoveDetailOption(rows, row, 1);
                Grid.SetColumn(moveDownButton, 2);
                row.Children.Add(moveDownButton);
                rows.Children.Add(row);
            }

            group.Children.Add(rows);
            return group;
        }

        private static string GetDetailFieldDisplayName(MetadataFieldDefinition field)
        {
            var label = field.IsCustom ? field.CustomDisplayName ?? field.Id : LanguageManager.GetString(field.LocalizationKey);
            var suffix = field.IsAutomaticallyCollected
                    ? LanguageManager.GetString("Dialog_DetailAutomaticSuffix")
                    : !field.IsEditable
                        ? LanguageManager.GetString("Dialog_DetailReadOnlySuffix")
                        : string.Empty;
            return string.IsNullOrWhiteSpace(suffix) ? label : $"{label} {suffix}";
        }

        private static void MoveDetailOption(StackPanel rows, Grid row, int offset)
        {
            var currentIndex = rows.Children.IndexOf(row);
            var targetIndex = currentIndex + offset;
            if (currentIndex < 0 || targetIndex < 0 || targetIndex >= rows.Children.Count)
            {
                return;
            }

            rows.Children.RemoveAt(currentIndex);
            rows.Children.Insert(targetIndex, row);
        }

        private static List<string> GetDetailOptionIds(StackPanel group)
        {
            return GetDetailOptionRows(group)
                .Select(row => row.Tag as string)
                .Where(fieldId => !string.IsNullOrWhiteSpace(fieldId))
                .ToList()!;
        }

        private static List<string> GetCheckedDetailOptionIds(StackPanel group)
        {
            return GetDetailOptionRows(group)
                .Where(row => row.Children.OfType<CheckBox>().FirstOrDefault()?.IsChecked == true)
                .Select(row => row.Tag as string)
                .Where(fieldId => !string.IsNullOrWhiteSpace(fieldId))
                .ToList()!;
        }

        private static IEnumerable<Grid> GetDetailOptionRows(StackPanel group)
        {
            return (group.Tag as StackPanel)?.Children.OfType<Grid>() ?? Enumerable.Empty<Grid>();
        }

        private async void ConfigureColumnsButton_Click(object sender, RoutedEventArgs e)
        {
            var visibleIds = NormalizeVisibleListFieldIds(VideoListDataGrid.Columns
                .Where(column => column.Visibility == Visibility.Visible)
                .Select(column => column.Tag as string)
                .Where(fieldId => !string.IsNullOrEmpty(fieldId)));
            var checkBoxes = new List<(string FieldId, CheckBox CheckBox)>();
            var fieldsPanel = new StackPanel { Spacing = 4 };
            foreach (var categoryGroup in ListFieldDefinitions.GroupBy(field => field.Category))
            {
                fieldsPanel.Children.Add(new TextBlock
                {
                    Text = GetListCategoryDisplayName(categoryGroup.Key),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Margin = new Thickness(0, 8, 0, 2)
                });

                foreach (var field in categoryGroup)
                {
                    var checkBox = new CheckBox
                    {
                        Content = field.IsCustom ? field.CustomDisplayName : LanguageManager.GetString(field.LocalizationKey),
                        IsChecked = visibleIds.Contains(field.Id),
                        Tag = field.Id
                    };
                    checkBoxes.Add((field.Id, checkBox));
                    fieldsPanel.Children.Add(checkBox);
                }
            }

            var validationText = new TextBlock
            {
                Text = LanguageManager.GetString("Dialog_ColumnsAtLeastOne"),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            var dialogContent = new StackPanel { Spacing = 8 };
            dialogContent.Children.Add(new ScrollViewer { MaxHeight = 480, Content = fieldsPanel });
            dialogContent.Children.Add(validationText);

            var dialog = new ContentDialog
            {
                Title = LanguageManager.GetString("Dialog_ChooseColumns_Title"),
                Content = dialogContent,
                PrimaryButtonText = LanguageManager.GetString("Button_Save"),
                CloseButtonText = LanguageManager.GetString("Button_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (checkBoxes.Any(option => option.CheckBox.IsChecked == true))
                {
                    return;
                }

                validationText.Visibility = Visibility.Visible;
                args.Cancel = true;
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var selectedIds = checkBoxes
                .Where(option => option.CheckBox.IsChecked == true)
                .Select(option => option.FieldId)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var column in VideoListDataGrid.Columns)
            {
                if (column.Tag is string fieldId)
                {
                    column.Visibility = selectedIds.Contains(fieldId) ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            var settings = SettingsManager.LoadSettings();
            SaveLayoutSettings(settings);
            SettingsManager.SaveSettings(settings);
        }

        private async void ConfigureCustomFieldsButton_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsManager.LoadSettings();
            settings.CustomMetadataFields ??= new List<CustomMetadataFieldDefinition>();
            var idBox = new TextBox { Header = "Stable ID (custom.example)", PlaceholderText = "custom.director-note" };
            var nameBox = new TextBox { Header = "項目名" };
            var typeBox = new ComboBox { Header = "値型", SelectedIndex = 0 };
            foreach (var type in new[] { "Text", "MultipleText", "Date", "Integer", "Decimal", "Boolean", "Rating" }) typeBox.Items.Add(type);
            var categoryBox = new ComboBox { Header = "カテゴリ", SelectedIndex = 1 };
            foreach (var category in Enum.GetNames<MetadataFieldCategory>()) categoryBox.Items.Add(category);
            var listDefault = new CheckBox { Content = "一覧に初期表示" };
            var detailDefault = new CheckBox { Content = "詳細に初期表示", IsChecked = true };
            var editable = new CheckBox { Content = "編集可能", IsChecked = true };
            var definitionsList = new ListView { MaxHeight = 150, ItemsSource = settings.CustomMetadataFields, DisplayMemberPath = nameof(CustomMetadataFieldDefinition.DisplayName) };
            definitionsList.SelectionChanged += (_, _) =>
            {
                if (definitionsList.SelectedItem is not CustomMetadataFieldDefinition selected) return;
                idBox.Text = selected.Id;
                nameBox.Text = selected.DisplayName;
                typeBox.SelectedItem = selected.ValueType;
                categoryBox.SelectedItem = selected.Category;
                listDefault.IsChecked = selected.IsInListByDefault;
                detailDefault.IsChecked = selected.IsInDetailsByDefault;
                editable.IsChecked = selected.IsEditable;
            };
            var removeButton = new Button { Content = "選択項目を削除" };
            var validation = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            removeButton.Click += (_, _) =>
            {
                if (definitionsList.SelectedItem is CustomMetadataFieldDefinition selected)
                {
                    settings.CustomMetadataFields.Remove(selected);
                    definitionsList.ItemsSource = null;
                    definitionsList.ItemsSource = settings.CustomMetadataFields.ToList();
                }
            };
            var newButton = new Button { Content = "新規項目" };
            newButton.Click += (_, _) =>
            {
                definitionsList.SelectedItem = null;
                idBox.Text = string.Empty;
                nameBox.Text = string.Empty;
                typeBox.SelectedIndex = 0;
                categoryBox.SelectedIndex = 1;
                listDefault.IsChecked = false;
                detailDefault.IsChecked = true;
                editable.IsChecked = true;
                validation.Visibility = Visibility.Collapsed;
            };
            var panel = new StackPanel { Spacing = 8 };
            panel.Children.Add(definitionsList);
            panel.Children.Add(newButton);
            panel.Children.Add(removeButton);
            panel.Children.Add(idBox);
            panel.Children.Add(nameBox);
            panel.Children.Add(typeBox);
            panel.Children.Add(categoryBox);
            panel.Children.Add(listDefault);
            panel.Children.Add(detailDefault);
            panel.Children.Add(editable);
            panel.Children.Add(validation);
            var dialog = new ContentDialog
            {
                Title = "カスタム項目の定義",
                Content = new ScrollViewer { Content = panel, MaxHeight = 620 },
                PrimaryButtonText = LanguageManager.GetString("Button_Save"),
                CloseButtonText = LanguageManager.GetString("Button_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };
            dialog.PrimaryButtonClick += (_, args) =>
            {
                var definition = new CustomMetadataFieldDefinition
                {
                    Id = idBox.Text.Trim(),
                    DisplayName = nameBox.Text.Trim(),
                    ValueType = typeBox.SelectedItem?.ToString() ?? string.Empty,
                    Category = categoryBox.SelectedItem?.ToString() ?? string.Empty,
                    IsInListByDefault = listDefault.IsChecked == true,
                    IsInDetailsByDefault = detailDefault.IsChecked == true,
                    IsEditable = editable.IsChecked == true
                };
                var selectedDefinition = definitionsList.SelectedItem as CustomMetadataFieldDefinition;
                if (selectedDefinition is not null && !string.Equals(selectedDefinition.Id, definition.Id, StringComparison.Ordinal))
                {
                    validation.Text = "既存項目のIDは変更できません。新しいIDで項目を追加してください。";
                    validation.Visibility = Visibility.Visible;
                    args.Cancel = true;
                    return;
                }

                var seen = MetadataFieldCatalog.All.Where(field => !field.IsCustom).Select(field => field.Id).ToHashSet(StringComparer.Ordinal);
                if (!MetadataFieldCatalog.TryCreateCustomField(definition, seen, out var validatedField))
                {
                    validation.Text = "IDはcustom.で始め、英数字・.・-・_のみを使ってください。IDと項目名、値型、カテゴリを確認してください。";
                    validation.Visibility = Visibility.Visible;
                    args.Cancel = true;
                    return;
                }

                var existing = settings.CustomMetadataFields.FindIndex(field => field.Id == definition.Id);
                if (existing >= 0) settings.CustomMetadataFields[existing] = definition;
                else
                {
                    settings.CustomMetadataFields.Add(definition);
                    settings.VisibleListFieldIds ??= ListFieldDefinitions.Where(field => field.IsInListByDefault).Select(field => field.Id).ToList();
                    settings.VisibleDetailFieldIds ??= DetailFieldDefinitions.Where(field => field.IsInDetailsByDefault).Select(field => field.Id).ToList();
                    settings.ListFieldOrder ??= new List<string>();
                    settings.DetailFieldOrder ??= new List<string>();
                    if (definition.IsInListByDefault && !settings.VisibleListFieldIds.Contains(definition.Id)) settings.VisibleListFieldIds.Add(definition.Id);
                    if (definition.IsInDetailsByDefault && !settings.VisibleDetailFieldIds.Contains(definition.Id)) settings.VisibleDetailFieldIds.Add(definition.Id);
                    if (!settings.ListFieldOrder.Contains(definition.Id)) settings.ListFieldOrder.Add(definition.Id);
                    if (!settings.DetailFieldOrder.Contains(definition.Id)) settings.DetailFieldOrder.Add(definition.Id);
                }
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            SettingsManager.SaveSettings(settings);
            MetadataFieldCatalog.RefreshCustomFields(settings.CustomMetadataFields);
            RebuildCustomFieldUi();
        }

        private void RebuildCustomFieldUi()
        {
            foreach (var fieldId in _detailControls.Keys.Where(id => id.StartsWith("custom.", StringComparison.Ordinal)).ToArray())
            {
                if (_detailControls[fieldId].Parent is Panel panel) panel.Children.Remove(_detailControls[fieldId]);
                _detailControls.Remove(fieldId);
            }
            BuildDynamicEditableFields();
            BuildVideoListColumns();
            RestoreLayoutSettings();
            ApplyDetailControlLocalization();
            RefreshCustomDetailControls();
        }

        private static string GetListCategoryDisplayName(MetadataFieldCategory category)
        {
            var key = category switch
            {
                MetadataFieldCategory.File => "Dialog_ColumnCategory_File",
                MetadataFieldCategory.Work => "Dialog_ColumnCategory_Work",
                MetadataFieldCategory.PeopleAndStaff => "Dialog_ColumnCategory_PeopleAndStaff",
                MetadataFieldCategory.Series => "Dialog_ColumnCategory_Series",
                MetadataFieldCategory.Publication => "Dialog_ColumnCategory_Publication",
                MetadataFieldCategory.Dates => "Dialog_ColumnCategory_Dates",
                MetadataFieldCategory.Evaluation => "Dialog_ColumnCategory_Evaluation",
                MetadataFieldCategory.Identifiers => "Dialog_ColumnCategory_Identifiers",
                MetadataFieldCategory.Acquisition => "Dialog_ColumnCategory_Acquisition",
                MetadataFieldCategory.Rights => "Dialog_ColumnCategory_Rights",
                MetadataFieldCategory.Location => "Dialog_ColumnCategory_Location",
                MetadataFieldCategory.Artwork => "Dialog_ColumnCategory_Artwork",
                MetadataFieldCategory.Technical => "Dialog_ColumnCategory_Technical",
                _ => category.ToString()
            };

            return LanguageManager.GetString(key);
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

        private void ReleaseDatePicker_DateChanged(object? sender, DatePickerValueChangedEventArgs args)
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
                RefreshCustomDetailControls();
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
                ViewModel.SetPlaybackError(LanguageManager.GetString("Msg_VideoPlaybackFailed"));
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
                Text = LanguageManager.GetString("Dialog_About_AppTitle"),
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
                Text = LanguageManager.GetString("Dialog_About_SupportMessage"),
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
            linkPanel.Children.Add(new TextBlock
            {
                Text = LanguageManager.GetString("Dialog_About_GitHubLabel"),
                VerticalAlignment = VerticalAlignment.Center
            });
            
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
            return typeof(App).Assembly.GetName().Version?.ToString(3) ?? LanguageManager.GetString("Value_Unknown");
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

    public sealed class CustomMetadataListValueConverter : IValueConverter
    {
        public static CustomMetadataListValueConverter Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is not VideoFileItem item || parameter is not string fieldId
                || !item.StructuredMetadata.CustomFields.TryGetValue(fieldId, out var fieldValue)) return string.Empty;
            return fieldValue.Text
                ?? fieldValue.Date?.ToString("d", System.Globalization.CultureInfo.CurrentUICulture)
                ?? fieldValue.Integer?.ToString(System.Globalization.CultureInfo.CurrentCulture)
                ?? fieldValue.Decimal?.ToString(System.Globalization.CultureInfo.CurrentCulture)
                ?? fieldValue.Rating?.ToString(System.Globalization.CultureInfo.CurrentCulture)
                ?? fieldValue.Boolean?.ToString()
                ?? string.Join("; ", fieldValue.TextValues);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
    }
}
