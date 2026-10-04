using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Archivio.Controls
{
    /// <summary>
    /// 複数の文字列を個別の入力欄で編集し、既存の区切り文字列へ変換するエディター。
    /// </summary>
    public sealed class MultipleTextEditor : UserControl
    {
        private readonly TextBlock _headerTextBlock;
        private readonly StackPanel _itemsPanel;
        private bool _isRebuilding;

        public MultipleTextEditor()
        {
            var root = new StackPanel { Spacing = 6 };
            _headerTextBlock = new TextBlock
            {
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            };
            root.Children.Add(_headerTextBlock);
            _itemsPanel = new StackPanel { Spacing = 4 };
            root.Children.Add(_itemsPanel);

            var addButton = new Button
            {
                Content = "+",
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 4, 12, 4)
            };
            addButton.Click += AddButton_Click;
            root.Children.Add(addButton);
            Content = root;
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(
                nameof(Value), typeof(string), typeof(MultipleTextEditor),
                new PropertyMetadata(string.Empty, OnValueChanged));

        public static readonly DependencyProperty HeaderProperty =
            DependencyProperty.Register(
                nameof(Header), typeof(string), typeof(MultipleTextEditor),
                new PropertyMetadata(string.Empty, OnHeaderChanged));

        public string Value
        {
            get => (string)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public string Header
        {
            get => (string)GetValue(HeaderProperty);
            set => SetValue(HeaderProperty, value);
        }

        private static void OnValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
        {
            if (dependencyObject is MultipleTextEditor editor && !editor._isRebuilding)
            {
                editor.RebuildItems(args.NewValue as string ?? string.Empty);
            }
        }

        private static void OnHeaderChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
        {
            if (dependencyObject is MultipleTextEditor editor)
            {
                editor._headerTextBlock.Text = args.NewValue as string ?? string.Empty;
                editor.UpdateItemPlaceholders();
            }
        }

        private void RebuildItems(string value)
        {
            _isRebuilding = true;
            try
            {
                _itemsPanel.Children.Clear();
                var values = ParseValues(value);
                if (values.Count == 0)
                {
                    values.Add(string.Empty);
                }

                foreach (var item in values)
                {
                    AddItemRow(item);
                }
            }
            finally
            {
                _isRebuilding = false;
            }
        }

        private void AddItemRow(string value)
        {
            var row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textBox = new TextBox
            {
                Text = value,
                PlaceholderText = Header
            };
            textBox.TextChanged += ItemTextBox_TextChanged;
            row.Children.Add(textBox);

            var removeButton = new Button
            {
                Content = "−",
                Padding = new Thickness(10, 4, 10, 4),
                Tag = row
            };
            removeButton.Click += RemoveButton_Click;
            Grid.SetColumn(removeButton, 1);
            row.Children.Add(removeButton);
            _itemsPanel.Children.Add(row);
        }

        private void UpdateItemPlaceholders()
        {
            foreach (var textBox in _itemsPanel.Children
                .OfType<Grid>()
                .Select(row => row.Children.OfType<TextBox>().FirstOrDefault())
                .Where(textBox => textBox is not null))
            {
                textBox!.PlaceholderText = Header;
            }
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            AddItemRow(string.Empty);
            UpdateValueFromItems();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: Grid row })
            {
                _itemsPanel.Children.Remove(row);
                if (_itemsPanel.Children.Count == 0)
                {
                    AddItemRow(string.Empty);
                }

                UpdateValueFromItems();
            }
        }

        private void ItemTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isRebuilding)
            {
                UpdateValueFromItems();
            }
        }

        private void UpdateValueFromItems()
        {
            if (_isRebuilding)
            {
                return;
            }

            var values = _itemsPanel.Children
                .OfType<Grid>()
                .Select(row => row.Children.OfType<TextBox>().FirstOrDefault()?.Text ?? string.Empty)
                .Select(value => value.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value));
            _isRebuilding = true;
            try
            {
                Value = string.Join("; ", values);
            }
            finally
            {
                _isRebuilding = false;
            }
        }

        private static List<string> ParseValues(string value)
        {
            return value
                .Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToList();
        }
    }
}
