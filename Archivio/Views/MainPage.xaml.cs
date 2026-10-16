using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
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
using Windows.ApplicationModel.DataTransfer;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;
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
        private bool _isRestoringLayoutSettings;
        private bool _isThumbnailSheetGenerating;
        private string? _ffmpegPath;
        private static MetadataFieldDefinition[] ListFieldDefinitions => MetadataFieldCatalog.ListFields.ToArray();
        private static MetadataFieldDefinition[] DetailFieldDefinitions => MetadataFieldCatalog.DetailFields.ToArray();
        private readonly Dictionary<string, FrameworkElement> _detailControls = new(StringComparer.Ordinal);

        public MainPage()
        {
            this.InitializeComponent();
            DataContext = new MainPageViewModel();
            BuildDynamicEditableFields();
            RegisterDetailControls();
            BuildDynamicTechnicalFields();
            DirectFileWriteModeCheckBox.IsChecked = SettingsManager.LoadSettings().UseDirectFileWriteMode;
            ViewModel.PropertyChanged += ViewModel_PropertyChanged;
            Unloaded += MainPage_Unloaded;
            VideoThumbnailGridView.ContextFlyout = VideoListDataGrid.ContextFlyout;
            BuildVideoListColumns();
            RestoreLayoutSettings();
            ApplyLocalization();
            InitializeLanguageMenu();
            UpdateActionButtonStates();
            _ = InitializeFfmpegAsync();
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
                DirectFileWriteModeCheckBox.Content = LanguageManager.GetString("DirectFileWriteMode_Label");
                ToolTipService.SetToolTip(DirectFileWriteModeCheckBox, LanguageManager.GetString("DirectFileWriteMode_Tooltip"));
                DetailViewToggleButton.Content = LanguageManager.GetString("Button_DetailView");
                ThumbnailViewToggleButton.Content = LanguageManager.GetString("Button_ThumbnailView");
                VideoPlaybackToggleButton.Content = LanguageManager.GetString("Button_Play");
                CoverArtViewToggleButton.Content = LanguageManager.GetString("Button_CoverArtView");
                CaptureFrameButton.Content = LanguageManager.GetString("Button_CaptureFrame");
                CreateThumbnailSheetButton.Content = LanguageManager.GetString("Button_CreateThumbnailSheet");
                ToolTipService.SetToolTip(CreateThumbnailSheetButton, LanguageManager.GetString("Msg_FFmpegMissing"));
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
                DetailFieldColumnsComboBox.Items[0] = new ComboBoxItem { Content = LanguageManager.GetString("DetailFieldColumns_One") };
                DetailFieldColumnsComboBox.Items[1] = new ComboBoxItem { Content = LanguageManager.GetString("DetailFieldColumns_Two") };
                ToolTipService.SetToolTip(DetailFieldColumnsComboBox, LanguageManager.GetString("DetailFieldColumns"));

                // Subfolders toggle switch
                ToolTipService.SetToolTip(SubfoldersToggle, LanguageManager.GetString("Subfolders_Search"));
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SubfoldersToggle, LanguageManager.GetString("Subfolders_Search"));

                // Context Menu
                RenameMenuItem.Text = LanguageManager.GetString("Menu_Rename");
                RenameFromMetadataMenuItem.Text = LanguageManager.GetString("Menu_RenameFromMetadata");
                DeleteMenuItem.Text = LanguageManager.GetString("Menu_Delete");
                ReMuxMenuItem.Text = LanguageManager.GetString("Menu_ReMux");
                ClearCacheMenuItem.Text = LanguageManager.GetString("Menu_ClearCache");
                LanguageMenu.Text = LanguageManager.GetString("Menu_Language");
                ReadmeMenuItem.Text = LanguageManager.GetString("Menu_Readme");
                AboutMenuItem.Text = LanguageManager.GetString("Menu_About");

                // Details form field headers are derived from MetadataFieldCatalog.
                ApplyDetailControlLocalization();
                SaveMetadataButton.Content = LanguageManager.GetString("Button_SaveMetadata");

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

        private void BuildDynamicTechnicalFields()
        {
            foreach (var field in DetailFieldDefinitions.Where(field =>
                         field.Category == MetadataFieldCategory.Technical
                         && field.DetailsElementName is null
                         && !string.IsNullOrWhiteSpace(field.DetailsBindingPath)))
            {
                var textBox = new TextBox
                {
                    Header = LanguageManager.GetString(field.LocalizationKey),
                    IsReadOnly = true,
                    Tag = field.Id
                };
                textBox.SetBinding(TextBox.TextProperty, new Binding
                {
                    Path = new PropertyPath($"SelectedVideo.{field.DetailsBindingPath}"),
                    Mode = BindingMode.OneWay
                });
                _detailControls[field.Id] = textBox;
                TechnicalMetadataFieldsPanel.Children.Add(textBox);
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
                        or MetadataFieldValueType.Date
                        or MetadataFieldValueType.Integer
                        or MetadataFieldValueType.Decimal
                        or MetadataFieldValueType.Currency
                        or MetadataFieldValueType.Url))
                .ToArray();

            foreach (var field in dynamicFields)
            {
                if (field.IsCustom)
                {
                    var customControl = CreateCustomDetailControl(field);
                    _detailControls[field.Id] = customControl;
                    EditableMetadataFieldsPanel.Children.Insert(
                        Math.Max(0, EditableMetadataFieldsPanel.Children.Count - 1), customControl);
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
                    var datePicker = new CalendarDatePicker
                    {
                        DateFormat = "{year.full}/{month.integer(2)}/{day.integer(2)}",
                        Width = 150,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        HorizontalContentAlignment = HorizontalAlignment.Left,
                        Tag = field.Id,
                        Header = LanguageManager.GetString(field.LocalizationKey)
                    };
                    datePicker.SetBinding(CalendarDatePicker.DateProperty, new Binding
                    {
                        Path = bindingPath,
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    });
                    datePicker.DateChanged += (_, args) =>
                    {
                        if (field.Id == "dates.release") ReleaseDatePicker_DateChanged(_, args);
                        else ViewModel.SelectedVideo?.MarkCustomMetadataFieldChanged(field.Id);
                    };
                    control = datePicker;
                }
                else if (field.ValueType == MetadataFieldValueType.MultipleText)
                {
                    var multipleTextEditor = new MultipleTextEditor { Header = LanguageManager.GetString(field.LocalizationKey) };
                    multipleTextEditor.SetBinding(MultipleTextEditor.ValueProperty, new Binding
                    {
                        Path = bindingPath,
                        Mode = BindingMode.TwoWay,
                        Converter = StringListValueConverter.Instance,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    });
                    multipleTextEditor.RegisterPropertyChangedCallback(MultipleTextEditor.ValueProperty,
                        (_, _) => ViewModel.SelectedVideo?.MarkCustomMetadataFieldChanged(field.Id));
                    control = multipleTextEditor;
                }
                else
                {
                    var textBox = new TextBox
                    {
                        Tag = field.Id,
                        Header = LanguageManager.GetString(field.LocalizationKey)
                    };
                    textBox.SetBinding(TextBox.TextProperty, new Binding
                    {
                        Path = bindingPath,
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
                    });
                    textBox.TextChanged += (_, _) => ViewModel.SelectedVideo?.MarkCustomMetadataFieldChanged(field.Id);
                    control = textBox;
                }

                _detailControls[field.Id] = control;
                EditableMetadataFieldsPanel.Children.Insert(
                    Math.Max(0, EditableMetadataFieldsPanel.Children.Count - 1),
                    control);
            }
        }

        private FrameworkElement CreateCustomDetailControl(MetadataFieldDefinition field)
        {
            if (field.ValueType == MetadataFieldValueType.Date)
            {
                var picker = new CalendarDatePicker
                {
                    Tag = field.Id,
                    DateFormat = "{year.full}/{month.integer(2)}/{day.integer(2)}",
                    Width = 150,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    IsEnabled = field.IsEditable
                };
                if (field.IsEditable) picker.DateChanged += (_, args) => SetCustomFieldValue(field, args.NewDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
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
                        case CalendarDatePicker picker:
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
                case CalendarDatePicker datePicker:
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
            _isRestoringLayoutSettings = true;
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
                    // 旧設定の固定ピクセル値は、ウィンドウを拡大しても詳細領域が伸びないため復元しない。
                    // 詳細領域は既定の Star 配分でウィンドウサイズに追従させる。
                    DetailsRow.Height = new GridLength(2, GridUnitType.Star);
                }
                settings.DetailFieldColumnCount = settings.DetailFieldColumnCount is 2 ? 2 : 1;
                DetailFieldColumnsComboBox.SelectedIndex = settings.DetailFieldColumnCount - 1;
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
            finally
            {
                _isRestoringLayoutSettings = false;
            }
        }

        public void SaveLayoutSettings(AppSettings settings)
        {
            try
            {
                settings.FileListRowHeight = FileListRow.Height.Value;
                settings.DetailsRowHeight = DetailsRow.Height.Value;
            settings.DetailFieldColumnCount = DetailFieldColumnsComboBox.SelectedIndex == 1 ? 2 : 1;
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
                CommunityToolkit.WinUI.UI.Controls.DataGridColumn column = field.Id == "artwork.thumbnail"
                    ? new CommunityToolkit.WinUI.UI.Controls.DataGridTemplateColumn
                    {
                        CellTemplate = Resources["ListThumbnailCellTemplate"] as DataTemplate
                    }
                    : new CommunityToolkit.WinUI.UI.Controls.DataGridTextColumn
                {
                    Binding = new Binding
                    {
                        Path = new PropertyPath(field.IsCustom ? "." : field.ListBindingPath ?? field.DetailsBindingPath),
                        Mode = BindingMode.OneWay,
                        Converter = field.IsCustom
                            ? CustomMetadataListValueConverter.Instance
                            : field.IsMultiple ? StringListValueConverter.Instance : null,
                        ConverterParameter = field.CustomFieldId
                    }
                };
                column.Header = field.IsCustom ? field.CustomDisplayName : LanguageManager.GetString(field.LocalizationKey);
                column.IsReadOnly = true;
                column.Width = new CommunityToolkit.WinUI.UI.Controls.DataGridLength(
                    field.Id == "artwork.thumbnail" ? 60 : field.DefaultListWidth);
                column.Tag = field.Id;
                column.Visibility = field.IsInListByDefault ? Visibility.Visible : Visibility.Collapsed;
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
            Panel panel,
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

            if (ReferenceEquals(panel, EditableMetadataFieldsPanel))
            {
                ApplyEditableDetailFieldLayout();
            }
        }

        private void ApplyEditableDetailFieldLayout()
        {
            var columnCount = DetailFieldColumnsComboBox.SelectedIndex == 1 ? 2 : 1;
            EditableMetadataFieldsPanel.ColumnDefinitions.Clear();
            for (var column = 0; column < columnCount; column++)
            {
                EditableMetadataFieldsPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            var visibleIndex = 0;
            foreach (var control in EditableMetadataFieldsPanel.Children.OfType<FrameworkElement>())
            {
                if (control.Visibility != Visibility.Visible)
                {
                    Grid.SetRow(control, 0);
                    Grid.SetColumn(control, 0);
                    continue;
                }

                Grid.SetRow(control, visibleIndex / columnCount);
                Grid.SetColumn(control, visibleIndex % columnCount);
                control.HorizontalAlignment = control is CalendarDatePicker
                    ? HorizontalAlignment.Left
                    : HorizontalAlignment.Stretch;
                visibleIndex++;
            }

            EditableMetadataFieldsPanel.RowDefinitions.Clear();
            var rowCount = Math.Max(1, (int)Math.Ceiling(visibleIndex / (double)columnCount));
            for (var row = 0; row < rowCount; row++)
            {
                EditableMetadataFieldsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
        }

        private void DetailFieldColumnsComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRestoringLayoutSettings || !IsLoaded)
            {
                return;
            }

            ApplyEditableDetailFieldLayout();
            var settings = SettingsManager.LoadSettings();
            settings.DetailFieldColumnCount = DetailFieldColumnsComboBox.SelectedIndex == 1 ? 2 : 1;
            SettingsManager.SaveSettings(settings);
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

        private static IEnumerable<string> GetDetailPanelOrder(Panel panel)
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
            Grid? draggingRow = null;
            var accumulatedDrag = 0d;
            group.Tag = rows;
            var orderIndex = detailOrder
                .Select((fieldId, index) => (fieldId, index))
                .ToDictionary(item => item.fieldId, item => item.index, StringComparer.Ordinal);
            foreach (var field in fields.OrderBy(field => orderIndex.TryGetValue(field.Id, out var index) ? index : int.MaxValue))
            {
                var row = new Grid
                {
                    Tag = field.Id,
                    // 背景がないPanelは空き領域がヒットテスト対象にならないため、
                    // ドラッグハンドルを含む行全体を確実にポインター対象にする。
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0))
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var dragHandle = new Border
                {
                    Child = new TextBlock { Text = "☷", FontSize = 20 },
                    Width = 32,
                    Height = 32,
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.7,
                    IsHitTestVisible = true,
                    ManipulationMode = ManipulationModes.TranslateY
                };
                dragHandle.ManipulationStarted += (_, args) =>
                {
                    draggingRow = row;
                    accumulatedDrag = 0;
                    args.Handled = true;
                };
                dragHandle.ManipulationDelta += (_, args) =>
                {
                    if (draggingRow != row) return;
                    accumulatedDrag += args.Delta.Translation.Y;
                    args.Handled = true;
                };
                dragHandle.ManipulationCompleted += (_, args) =>
                {
                    if (draggingRow == row)
                    {
                        var sourceIndex = rows.Children.IndexOf(row);
                        var targetIndex = sourceIndex;
                        var remaining = accumulatedDrag;
                        if (sourceIndex >= 0)
                        {
                            while (remaining > 0 && targetIndex < rows.Children.Count - 1)
                            {
                                var next = rows.Children[targetIndex + 1] as Grid;
                                var height = Math.Max(next?.ActualHeight ?? row.ActualHeight, 1);
                                if (remaining < Math.Max((row.ActualHeight + height) / 2, 1)) break;
                                remaining -= height;
                                targetIndex++;
                            }
                            while (remaining < 0 && targetIndex > 0)
                            {
                                var previous = rows.Children[targetIndex - 1] as Grid;
                                var height = Math.Max(previous?.ActualHeight ?? row.ActualHeight, 1);
                                if (-remaining < Math.Max((row.ActualHeight + height) / 2, 1)) break;
                                remaining += height;
                                targetIndex--;
                            }
                            if (targetIndex != sourceIndex)
                            {
                                rows.Children.RemoveAt(sourceIndex);
                                rows.Children.Insert(targetIndex, row);
                            }
                        }
                        draggingRow = null;
                        accumulatedDrag = 0;
                    }
                    args.Handled = true;
                };
                Grid.SetColumn(dragHandle, 0);
                row.Children.Add(dragHandle);
                var checkBox = new CheckBox
                {
                    Content = GetDetailFieldDisplayName(field),
                    IsChecked = visibleIds.Contains(field.Id),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(checkBox, 1);
                row.Children.Add(checkBox);

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

        private async void DirectFileWriteModeCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsManager.LoadSettings();
            var requested = DirectFileWriteModeCheckBox.IsChecked == true;
            if (requested && !settings.UseDirectFileWriteMode)
            {
                var dialog = new ContentDialog
                {
                    Title = LanguageManager.GetString("DirectFileWriteMode_WarningTitle"),
                    Content = LanguageManager.GetString("DirectFileWriteMode_WarningMessage"),
                    PrimaryButtonText = LanguageManager.GetString("DirectFileWriteMode_Enable"),
                    CloseButtonText = LanguageManager.GetString("Button_Cancel"),
                    XamlRoot = Content.XamlRoot
                };

                if (await dialog.ShowAsync() != ContentDialogResult.Primary)
                {
                    DirectFileWriteModeCheckBox.IsChecked = false;
                    return;
                }
            }

            settings.UseDirectFileWriteMode = requested;
            SettingsManager.SaveSettings(settings);
        }

        private void ReleaseDatePicker_DateChanged(object? sender, CalendarDatePickerDateChangedEventArgs args)
        {
            if (ViewModel.SelectedVideo is null || args.NewDate is not { } newDate)
            {
                return;
            }

            ViewModel.SelectedVideo.ReleaseDate = newDate;
            if (ViewModel.SelectedVideo.HasChanges(nameof(VideoFileItem.ReleaseDate)))
            {
                ViewModel.SelectedVideo.MarkCustomMetadataFieldChanged("dates.release");
            }
        }

        private sealed class StringListValueConverter : IValueConverter
        {
            public static StringListValueConverter Instance { get; } = new();

            public object Convert(object value, Type targetType, object parameter, string language)
            {
                return value switch
                {
                    IEnumerable<string> values => string.Join("; ", values),
                    _ => value?.ToString() ?? string.Empty
                };
            }

            public object ConvertBack(object value, Type targetType, object parameter, string language)
            {
                var values = (value?.ToString() ?? string.Empty)
                    .Split(new[] { ';', '\r', '\n', ',', '，', '；' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Where(item => item.Length > 0)
                    .ToList();

                // Category/Participantsなどは区切り文字付きのstring、
                // 追加項目のAlternateTitlesなどはList<string>で保持する。
                return targetType == typeof(string)
                    ? string.Join("; ", values)
                    : values;
            }
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

        private void CoverArt_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems)
                ? DataPackageOperation.Copy
                : DataPackageOperation.None;
            e.Handled = true;
        }

        private async void CoverArt_Drop(object sender, DragEventArgs e)
        {
            if (ViewModel.SelectedVideo is null
                || !e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                return;
            }

            var items = await e.DataView.GetStorageItemsAsync();
            var imageFiles = items
                .OfType<StorageFile>()
                .Where(file => file.FileType.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                    || file.FileType.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                    || file.FileType.Equals(".png", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (imageFiles.Count == 0)
            {
                return;
            }

            var replace = false;
            if (ViewModel.SelectedVideo.HasCoverArt
                || ViewModel.SelectedVideo.CoverArtImages.Count > 0)
            {
                var dialog = new ContentDialog
                {
                    Title = LanguageManager.GetString("CoverArtDrop_Title"),
                    Content = LanguageManager.GetString("CoverArtDrop_Message"),
                    PrimaryButtonText = LanguageManager.GetString("CoverArtDrop_Add"),
                    SecondaryButtonText = LanguageManager.GetString("CoverArtDrop_Replace"),
                    CloseButtonText = LanguageManager.GetString("Button_Cancel"),
                    XamlRoot = Content.XamlRoot
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.None)
                {
                    return;
                }

                replace = result == ContentDialogResult.Secondary;
            }

            for (var index = 0; index < imageFiles.Count; index++)
            {
                await ViewModel.Details.ImportCoverArtImageAsync(
                    imageFiles[index],
                    replace && index == 0);
            }

            e.Handled = true;
        }

        private async void CaptureFrameButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isPlaybackView || _videoPlayerElement?.Source is null || _playbackPlayer is null
                || ViewModel.SelectedVideo is null)
            {
                ViewModel.SetPlaybackError(LanguageManager.GetString("Msg_CaptureFramePlaybackRequired"));
                return;
            }

            var ffmpegPath = await GetFfmpegPathAsync();
            if (ffmpegPath is null)
            {
                ViewModel.SetPlaybackError(LanguageManager.GetString("Msg_FFmpegMissing"));
                return;
            }

            string? outputPath = null;
            try
            {
                var file = await ViewModel.SelectedVideo.GetFileAsync();
                outputPath = Path.Combine(Path.GetTempPath(), $"archivio-frame-{Guid.NewGuid():N}.png");
                var position = _playbackPlayer.PlaybackSession?.Position ?? TimeSpan.Zero;
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("-hide_banner");
                startInfo.ArgumentList.Add("-loglevel");
                startInfo.ArgumentList.Add("error");
                startInfo.ArgumentList.Add("-ss");
                startInfo.ArgumentList.Add(Math.Max(0, position.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture));
                startInfo.ArgumentList.Add("-i");
                startInfo.ArgumentList.Add(file.Path);
                startInfo.ArgumentList.Add("-frames:v");
                startInfo.ArgumentList.Add("1");
                startInfo.ArgumentList.Add("-f");
                startInfo.ArgumentList.Add("image2");
                startInfo.ArgumentList.Add("-c:v");
                startInfo.ArgumentList.Add("png");
                startInfo.ArgumentList.Add("-y");
                startInfo.ArgumentList.Add(outputPath);

                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("FFmpegを起動できませんでした。");
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0 || !File.Exists(outputPath))
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? LanguageManager.GetString("Msg_CaptureFrameUnavailable")
                        : error.Trim());
                }

                var data = await File.ReadAllBytesAsync(outputPath);
                await ViewModel.Details.AddGeneratedCoverArtImageAsync(
                    data,
                    "image/png",
                    $"Archivio capture {position:hh\\:mm\\:ss}");
            }
            catch (Exception ex)
            {
                AppLogger.Error("動画フレームのキャプチャに失敗しました", ex, ViewModel.SelectedVideo?.FullPath);
                ViewModel.SetPlaybackError(LanguageManager.GetString("Msg_CaptureFrameFailed", ex.Message));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    try { if (File.Exists(outputPath)) File.Delete(outputPath); }
                    catch (Exception ex) { AppLogger.Error("一時キャプチャ画像の削除に失敗しました", ex, outputPath); }
                }
            }
        }

        private async void CreateThumbnailSheetButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isThumbnailSheetGenerating || ViewModel.SelectedVideo is null)
            {
                return;
            }

            _isThumbnailSheetGenerating = true;
            UpdateActionButtonStates();
            string? outputPath = null;
            try
            {
                var ffmpegPath = await GetFfmpegPathAsync();
                if (ffmpegPath is null)
                {
                    ViewModel.SetPlaybackError(LanguageManager.GetString("Msg_FFmpegMissing"));
                    return;
                }

                var file = await ViewModel.SelectedVideo.GetFileAsync();
                outputPath = Path.Combine(Path.GetTempPath(), $"archivio-sheet-{Guid.NewGuid():N}.png");
                var duration = ViewModel.SelectedVideo.DurationValue?.TotalSeconds
                    ?? ViewModel.SelectedVideo.TechnicalInfo.Duration?.TotalSeconds
                    ?? 0;
                if (duration <= 0)
                {
                    // 動画を再生していなくても、選択直後の非同期読み込みが完了していない場合に
                    // ここで技術情報を直接取得してサムネイル生成を継続する。
                    var probeResult = await new MediaProbeService().ProbeAsync(file);
                    duration = probeResult.Duration?.TotalSeconds
                        ?? probeResult.Technical.Duration?.TotalSeconds
                        ?? 0;
                }
                if (duration <= 0)
                {
                    throw new InvalidOperationException("動画の再生時間を取得できないため、サムネイルを作成できません。");
                }

                // 動画全体を fps フィルターでデコードすると長時間動画ほど遅くなるため、
                // 各区間の中央へ直接シークして25フレームだけ取得する。
                var filterParts = new List<string>(25);
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("-hide_banner");
                startInfo.ArgumentList.Add("-loglevel");
                startInfo.ArgumentList.Add("error");
                for (var index = 0; index < 25; index++)
                {
                    var timestamp = duration * (index + 0.5) / 25.0;
                    startInfo.ArgumentList.Add("-ss");
                    startInfo.ArgumentList.Add(timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    startInfo.ArgumentList.Add("-i");
                    startInfo.ArgumentList.Add(file.Path);
                    filterParts.Add($"[{index}:v:0]trim=end_frame=1,setpts=PTS-STARTPTS,scale=320:-2[s{index}]");
                }

                var tileInputs = string.Concat(Enumerable.Range(0, 25).Select(index => $"[s{index}]"));
                var filter = string.Join(";", filterParts)
                    + $";{tileInputs}concat=n=25:v=1:a=0[frames];[frames]tile=5x5:padding=4:margin=4:color=black[v]";
                startInfo.ArgumentList.Add("-filter_complex");
                startInfo.ArgumentList.Add(filter);
                startInfo.ArgumentList.Add("-map");
                startInfo.ArgumentList.Add("[v]");
                startInfo.ArgumentList.Add("-frames:v");
                startInfo.ArgumentList.Add("1");
                startInfo.ArgumentList.Add("-an");
                startInfo.ArgumentList.Add("-y");
                startInfo.ArgumentList.Add(outputPath);

                using var process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("FFmpegを起動できませんでした。");
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0 || !File.Exists(outputPath))
                {
                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                        ? "FFmpegでサムネイルリストを作成できませんでした。"
                        : error.Trim());
                }

                var data = await File.ReadAllBytesAsync(outputPath);
                await ViewModel.Details.AddGeneratedCoverArtImageAsync(
                    data,
                    "image/png",
                    "Archivio thumbnail sheet");
            }
            catch (Exception ex)
            {
                AppLogger.Error("サムネイルリストの生成に失敗しました", ex, ViewModel.SelectedVideo?.FullPath);
                ViewModel.SetPlaybackError(LanguageManager.GetString("Msg_ThumbnailSheetFailed", ex.Message));
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(outputPath))
                {
                    try { if (File.Exists(outputPath)) File.Delete(outputPath); }
                    catch (Exception ex) { AppLogger.Error("一時サムネイルの削除に失敗しました", ex, outputPath); }
                }

                _isThumbnailSheetGenerating = false;
                UpdateActionButtonStates();
            }
        }

        private async Task InitializeFfmpegAsync()
        {
            _ffmpegPath = await FindFfmpegAsync();
            UpdateActionButtonStates();
        }

        private async Task<string?> GetFfmpegPathAsync()
        {
            _ffmpegPath ??= await FindFfmpegAsync();
            UpdateActionButtonStates();
            return _ffmpegPath;
        }

        private static async Task<string?> FindFfmpegAsync()
        {
            var configuredFfprobe = SettingsManager.LoadSettings().FfprobePath;
            if (!string.IsNullOrWhiteSpace(configuredFfprobe))
            {
                var configuredPath = configuredFfprobe.Trim().Trim('"');
                if (Path.GetFileName(configuredPath).Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(configuredPath))
                {
                    return configuredPath;
                }

                var directory = Path.GetDirectoryName(configuredPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    var sibling = Path.Combine(directory, "ffmpeg.exe");
                    if (File.Exists(sibling)) return sibling;
                }
            }

            // `where.exe` may not see PATH changes made after the app was started.
            // Check the current process PATH as well as the normal Windows lookup.
            var pathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var directory in pathEntries)
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim('"'), "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException)
                {
                    // Ignore malformed PATH entries.
                }
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "where.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("ffmpeg.exe");
            using var process = Process.Start(startInfo);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) return null;

            return output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim().Trim('"'))
                .FirstOrDefault(File.Exists);
        }

        private async void ReplaceImageButton_Click(object sender, RoutedEventArgs e)
        {
            await ViewModel.Details.ReplaceCoverArtImageAsync();
        }

        private void DeleteImageButton_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.Details.DeleteCoverArtImage();
        }

        private void MainPage_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            if (!_isPlaybackView
                || _videoPlayerElement?.Source is null
                || _playbackPlayer is null)
            {
                return;
            }

            var session = _playbackPlayer.PlaybackSession;
            if (session is null)
            {
                return;
            }

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Left:
                    session.Position = ClampPlaybackPosition(
                        session.Position - TimeSpan.FromSeconds(5),
                        session.NaturalDuration);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Right:
                    session.Position = ClampPlaybackPosition(
                        session.Position + TimeSpan.FromSeconds(5),
                        session.NaturalDuration);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Up:
                    _playbackPlayer.Volume = Math.Min(1.0, _playbackPlayer.Volume + 0.05);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Down:
                    _playbackPlayer.Volume = Math.Max(0.0, _playbackPlayer.Volume - 0.05);
                    e.Handled = true;
                    break;
                case Windows.System.VirtualKey.Space:
                    if (session.PlaybackState == MediaPlaybackState.Playing)
                    {
                        _playbackPlayer.Pause();
                    }
                    else
                    {
                        _playbackPlayer.Play();
                    }

                    e.Handled = true;
                    break;
            }
        }

        private static TimeSpan ClampPlaybackPosition(TimeSpan position, TimeSpan duration)
        {
            if (position < TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            return duration > TimeSpan.Zero && position > duration
                ? duration
                : position;
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
                UpdateActionButtonStates();
                // 別ファイルを選択したら、現在の動画と標準コントロールを終了する。
                _isPlaybackView = false;
                CoverArtViewToggleButton.IsChecked = true;
                VideoPlaybackToggleButton.IsChecked = false;
                await UpdatePlaybackSourceAsync();
            }
        }

        private void UpdateCaptureFrameButtonState()
        {
            CaptureFrameButton.IsEnabled = _isPlaybackView
                && _videoPlayerElement?.Source is not null
                && _playbackPlayer is not null
                && ViewModel.IsVideoSelected;
        }

        private void UpdateActionButtonStates()
        {
            UpdateCaptureFrameButtonState();
            CreateThumbnailSheetButton.IsEnabled = ViewModel.IsVideoSelected
                && _ffmpegPath is not null
                && !_isThumbnailSheetGenerating;
            ThumbnailSheetProgressRing.IsActive = _isThumbnailSheetGenerating;
            ThumbnailSheetProgressRing.Visibility = _isThumbnailSheetGenerating
                ? Visibility.Visible
                : Visibility.Collapsed;
            CreateThumbnailSheetButton.Content = _isThumbnailSheetGenerating
                ? LanguageManager.GetString("Msg_ThumbnailSheetProcessing")
                : LanguageManager.GetString("Button_CreateThumbnailSheet");
            ToolTipService.SetToolTip(
                CreateThumbnailSheetButton,
                _ffmpegPath is null
                    ? LanguageManager.GetString("Msg_FFmpegMissing")
                    : LanguageManager.GetString("Tooltip_CreateThumbnailSheet"));
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
                UpdateActionButtonStates();
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
                UpdateActionButtonStates();
                return;
            }

            _playbackPlayer.Pause();
            _videoPlayerElement.Source = null;

            if (ViewModel.SelectedVideo is null)
            {
                UpdateActionButtonStates();
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
                UpdateActionButtonStates();
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
                list.Add(new FilterValueItem
                {
                    Value = val,
                    DisplayValue = string.IsNullOrEmpty(val)
                        ? LanguageManager.GetString("Filter_Blank")
                        : val,
                    IsChecked = isChecked
                });
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
            bool isItemSelected = ViewModel.IsVideoSelected;
            RenameMenuItem.IsEnabled = isItemSelected;
            RenameFromMetadataMenuItem.IsEnabled = isItemSelected;
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

        private async void RenameFromMetadataMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedVideo = ViewModel.SelectedVideo;
            if (selectedVideo is null)
            {
                return;
            }

            var settings = SettingsManager.LoadSettings();
            var templateBox = new TextBox
            {
                Text = settings.FileNameTemplate,
                PlaceholderText = LanguageManager.GetString("Dialog_RenameFromMetadata_Hint"),
                Width = 520,
                TextWrapping = TextWrapping.Wrap
            };
            var previewText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            };
            void UpdatePreview()
            {
                var result = TryBuildMetadataFileName(selectedVideo, templateBox.Text, out var name, out _);
                previewText.Text = result
                    ? LanguageManager.GetString("Dialog_RenameFromMetadata_Preview", name)
                    : LanguageManager.GetString("Msg_RenameFromMetadata_Empty");
            }
            templateBox.TextChanged += (_, _) => UpdatePreview();
            UpdatePreview();

            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(new TextBlock { Text = LanguageManager.GetString("Dialog_RenameFromMetadata_Template") });
            content.Children.Add(templateBox);
            content.Children.Add(new TextBlock
            {
                Text = LanguageManager.GetString("Dialog_RenameFromMetadata_Hint"),
                Opacity = 0.75
            });
            content.Children.Add(previewText);

            var dialog = new ContentDialog
            {
                Title = LanguageManager.GetString("Dialog_RenameFromMetadata_Title"),
                Content = content,
                PrimaryButtonText = LanguageManager.GetString("Button_Save"),
                CloseButtonText = LanguageManager.GetString("Button_Cancel"),
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = XamlRoot
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var template = templateBox.Text.Trim();
            if (!TryBuildMetadataFileName(selectedVideo, template, out var newName, out var invalidIds))
            {
                var message = invalidIds.Count > 0
                    ? LanguageManager.GetString("Msg_RenameFromMetadata_InvalidTemplate", string.Join(", ", invalidIds))
                    : LanguageManager.GetString("Msg_RenameFromMetadata_Empty");
                ViewModel.SetPlaybackError(message);
                return;
            }

            settings.FileNameTemplate = template;
            SettingsManager.SaveSettings(settings);
            if (!string.Equals(newName, selectedVideo.FileName, StringComparison.OrdinalIgnoreCase))
            {
                await ViewModel.RenameVideoAsync(selectedVideo, newName);
            }
        }

        private static bool TryBuildMetadataFileName(
            VideoFileItem video,
            string template,
            out string fileName,
            out List<string> invalidIds)
        {
            fileName = string.Empty;
            invalidIds = new List<string>();
            if (string.IsNullOrWhiteSpace(template))
            {
                return false;
            }

            var result = new System.Text.StringBuilder();
            for (var index = 0; index < template.Length;)
            {
                if (template[index] == '\\')
                {
                    var end = index + 1;
                    while (end < template.Length && template[end] is not ('[' or ']' or '{' or '}' or '\\')) end++;
                    AppendTokenValue(video, invalidIds, result, template[(index + 1)..end]);
                    index = end;
                    continue;
                }

                if (template[index] == '{')
                {
                    var close = template.IndexOf('}', index + 1);
                    if (close < 0)
                    {
                        result.Append(template[index..]);
                        break;
                    }

                    var token = template[(index + 1)..close].Trim();
                    var preserveBraces = token.StartsWith('\\');
                    if (preserveBraces) token = token[1..];
                    if (TryResolveMetadataToken(video, token, out _))
                    {
                        if (preserveBraces) result.Append('{');
                        AppendTokenValue(video, invalidIds, result, token);
                        if (preserveBraces) result.Append('}');
                    }
                    else
                    {
                        result.Append(template[index..(close + 1)]);
                        if (!string.IsNullOrWhiteSpace(token)) invalidIds.Add(token);
                    }

                    index = close + 1;
                    continue;
                }

                // 角括弧などの記号は通常文字として扱う。例えば
                // [\出演者0]\タイトル[\品番] は角括弧を残して展開する。
                result.Append(template[index]);
                index++;
            }

            var invalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
            fileName = new string(result.ToString()
                .Select(character => invalidFileNameChars.Contains(character) ? '_' : character)
                .ToArray()).Trim().TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(fileName) || fileName is "." or ".." || invalidIds.Count > 0)
            {
                return false;
            }

            fileName += System.IO.Path.GetExtension(video.FileName);
            return true;
        }

        private static void AppendTokenValue(
            VideoFileItem video,
            ICollection<string> invalidIds,
            System.Text.StringBuilder result,
            string id)
        {
            if (TryResolveMetadataToken(video, id, out var value))
            {
                result.Append(value);
            }
            else
            {
                invalidIds.Add(id);
            }
        }

        /// <summary>
        /// ファイル名テンプレートのIDを、表示名ではなくメタデータカタログの安定IDから解決する。
        /// 例: work.subtitle、work.alternate-titles.0、credits.cast.0、custom.my-field。
        /// </summary>
        private static bool TryResolveMetadataToken(VideoFileItem video, string token, out string value)
        {
            value = string.Empty;
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["出演者0"] = "credits.cast.0",
                ["タイトル"] = "work.title",
                ["品番"] = "identifiers.catalog-number"
            };
            token = aliases.TryGetValue(token.Trim(), out var alias) ? alias : token.Trim();

            // 出演者は人物名・役名・主演をテンプレートから個別に指定できる。
            if (TryParseCastToken(token, out var castIndex, out var castPart))
            {
                var cast = video.StructuredMetadata.PeopleAndStaff.Cast
                    .OrderBy(credit => credit.Order)
                    .ToList();
                if (castIndex < 0 || castIndex >= cast.Count)
                {
                    value = string.Empty;
                    return MetadataFieldCatalog.FindById("credits.cast") is not null;
                }

                var credit = cast[castIndex];
                value = castPart switch
                {
                    "character" or "role" => credit.CharacterName,
                    "lead" => credit.IsLead ? "true" : "false",
                    _ => credit.PersonName
                };
                return true;
            }

            var definition = MetadataFieldCatalog.FindById(token);
            var indexed = false;
            var index = -1;
            var baseToken = token;
            if (definition is null)
            {
                var separator = token.LastIndexOf('.');
                if (separator > 0 && int.TryParse(token[(separator + 1)..], out index))
                {
                    baseToken = token[..separator];
                    definition = MetadataFieldCatalog.FindById(baseToken);
                    indexed = definition is not null;
                }
            }

            if (definition is null)
            {
                return false;
            }

            if (definition.IsCustom)
            {
                if (video.StructuredMetadata.CustomFields.TryGetValue(
                        definition.CustomFieldId ?? definition.Id, out var customValue))
                {
                    value = FormatMetadataValue(customValue);
                }
                return true;
            }

            // 画像バイナリや表示専用項目は、ファイル名に埋め込める文字値を持たない。
            var path = definition.DetailsBindingPath ?? definition.ListBindingPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return true;
            }

            if (!TryReadPropertyPath(video, path, out var rawValue))
            {
                return true;
            }

            if (indexed)
            {
                rawValue = GetIndexedValue(rawValue, index);
            }

            value = FormatMetadataValue(rawValue);
            return true;
        }

        private static bool TryParseCastToken(string token, out int index, out string part)
        {
            index = -1;
            part = string.Empty;
            const string prefix = "credits.cast.";
            if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var remainder = token[prefix.Length..];
            var parts = remainder.Split('.', 2);
            if (!int.TryParse(parts[0], out index))
            {
                return false;
            }

            part = parts.Length == 2 ? parts[1].ToLowerInvariant() : "person";
            return true;
        }

        private static bool TryReadPropertyPath(object source, string path, out object? value)
        {
            value = source;
            foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
            {
                if (value is null)
                {
                    return false;
                }

                var property = value.GetType().GetProperty(
                    segment,
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.IgnoreCase);
                if (property is null)
                {
                    value = null;
                    return false;
                }

                value = property.GetValue(value);
            }

            return true;
        }

        private static object? GetIndexedValue(object? value, int index)
        {
            if (index < 0)
            {
                return null;
            }

            if (value is System.Collections.IList list)
            {
                return index >= 0 && index < list.Count ? list[index] : null;
            }

            if (value is IEnumerable<string> strings)
            {
                return strings.Skip(index).FirstOrDefault();
            }

            if (value is string text)
            {
                return text.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .Skip(index)
                    .FirstOrDefault();
            }

            return null;
        }

        private static string FormatMetadataValue(object? value)
        {
            if (value is null)
            {
                return string.Empty;
            }

            if (value is MetadataValue metadataValue)
            {
                return metadataValue.Kind switch
                {
                    MetadataValueKind.Date => metadataValue.Date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    MetadataValueKind.Integer => metadataValue.Integer?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    MetadataValueKind.Decimal => metadataValue.Decimal?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    MetadataValueKind.Rating => metadataValue.Rating?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    MetadataValueKind.Boolean => metadataValue.Boolean?.ToString().ToLowerInvariant() ?? string.Empty,
                    MetadataValueKind.MultipleText => string.Join("; ", metadataValue.TextValues),
                    MetadataValueKind.Image or MetadataValueKind.ImageCollection => string.Empty,
                    _ => string.IsNullOrWhiteSpace(metadataValue.Text)
                        ? string.Join("; ", metadataValue.TextValues)
                        : metadataValue.Text
                };
            }

            if (value is DateTimeOffset dateTimeOffset)
            {
                return dateTimeOffset.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (value is DateTime dateTime)
            {
                return dateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (value is TimeSpan duration)
            {
                return duration.ToString("c", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (value is string textValue)
            {
                return textValue;
            }

            if (value is System.Collections.IEnumerable enumerable)
            {
                return string.Join("; ", enumerable.Cast<object?>().Select(FormatMetadataValue)
                    .Where(item => !string.IsNullOrWhiteSpace(item)));
            }

            return value is IFormattable formattable
                ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
                : value.ToString() ?? string.Empty;
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
