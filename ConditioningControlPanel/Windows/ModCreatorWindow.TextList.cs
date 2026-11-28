using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Services;
using Microsoft.Win32;

namespace ConditioningControlPanel
{
    /// <summary>
    /// Text Replacements: the browsable list of original texts (<see cref="ModTextCatalog"/>).
    /// Clicking an entry fills the "original text" box of a row and puts the caret in its
    /// "new text" box; typing a pair by hand works exactly as before. The list never touches the
    /// manifest itself, so loading and exporting a .ccpmod are unchanged.
    /// </summary>
    public partial class ModCreatorWindow
    {
        private IReadOnlyList<ModTextEntry>? _originalTexts;

        private void AddOriginalTextBrowser(Panel stack)
        {
            try
            {
                _originalTexts = ModTextCatalog.Build();
                stack.Children.Add(CreateOriginalTextBrowser(
                    _originalTexts,
                    TryFindResource("DarkTextBox") as Style,
                    TryFindResource("SecondaryButton") as Style,
                    PickOriginalText,
                    ExportOriginalTexts));
            }
            catch (Exception ex)
            {
                // The list is a helper: the section keeps working by hand without it.
                App.Logger?.Warning("Mod Creator: original text list unavailable: {E}", ex.Message);
            }
        }

        /// <summary>
        /// Fills the replacement row for <paramref name="text"/>: the existing row for that text if
        /// there is one (a key can only appear once), else the last empty row, else a new row.
        /// </summary>
        private void PickOriginalText(string text)
        {
            var row = _textReplacements.FirstOrDefault(r => r.From.Text == text);
            if (row.From == null)
            {
                row = _textReplacements.LastOrDefault(r => string.IsNullOrWhiteSpace(r.From.Text)
                                                           && string.IsNullOrWhiteSpace(r.To.Text));
                if (row.From != null)
                    row.From.Text = text;
                else
                {
                    AddReplacementRow(text, "");
                    if (_textReplacements.Count == 0) return;
                    row = _textReplacements[^1];
                }
            }

            var to = row.To;
            // After the click has finished, or the ListBox takes focus back on mouse up.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                to.BringIntoView();
                to.Focus();
                Keyboard.Focus(to);
                to.CaretIndex = to.Text.Length;
            }));
        }

        private void ExportOriginalTexts()
        {
            var entries = _originalTexts ?? ModTextCatalog.Build();
            var sfd = new SaveFileDialog
            {
                Title = "Export original texts",
                Filter = "Text files (*.txt)|*.txt",
                FileName = "ccp-original-texts.txt",
            };
            if (sfd.ShowDialog(this) != true) return;

            try
            {
                File.WriteAllText(sfd.FileName, ModTextCatalog.ToExportText(entries, UpdateService.AppVersion),
                    new UTF8Encoding(false));
                TxtStatus.Text = $"Exported {entries.Count} original texts to {Path.GetFileName(sfd.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not save the list: " + ex.Message, "Export list",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// The browser itself: search box, Export button and a grouped list. Static and built from
        /// plain inputs so it can be rendered on its own.
        /// </summary>
        internal static FrameworkElement CreateOriginalTextBrowser(
            IReadOnlyList<ModTextEntry> entries, Style? textBoxStyle, Style? buttonStyle,
            Action<string> onPick, Action onExport)
        {
            var white = Brushes.White;
            var grey = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0xB8));
            var pink = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4));

            var root = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x3A)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x35, 0x35, 0x50)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 4, 0, 12),
            };
            var stack = new StackPanel();
            root.Child = stack;

            var title = new TextBlock
            {
                Foreground = white, FontSize = 13, FontWeight = FontWeights.SemiBold,
                Text = $"Original texts ({entries.Count})",
            };
            stack.Children.Add(title);
            stack.Children.Add(new TextBlock
            {
                Foreground = grey, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 8),
                Text = "Click a text to use it as the original, then type the new wording. Searching a skill shows its effect and flavour with it. You can still type any other text by hand.",
            });

            // Search box with a placeholder, and the Export button beside it.
            var bar = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var search = new TextBox { FontSize = 12, Padding = new Thickness(6, 4, 6, 4) };
            if (textBoxStyle != null) search.Style = textBoxStyle;
            AutomationName(search, "Search original texts");
            var placeholder = new TextBlock
            {
                Text = "Search (skill, quest, Pink...)", Foreground = grey, FontSize = 12, IsHitTestVisible = false,
                Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            bar.Children.Add(search);
            bar.Children.Add(placeholder);
            var export = new Button
            {
                Content = "Export list", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(8, 0, 0, 0),
                ToolTip = "Save every original text as a .txt file for modders",
            };
            if (buttonStyle != null) export.Style = buttonStyle;
            export.Click += (_, _) => onExport();
            Grid.SetColumn(export, 1);
            bar.Children.Add(export);
            stack.Children.Add(bar);

            // A search rebuilds the view from ModTextCatalog.Search rather than filtering it: a
            // skill whose name matches brings its effect and flavour along, grouped under it.
            static ListCollectionView ViewOf(IReadOnlyList<ModTextEntry> shown)
            {
                var v = new ListCollectionView(shown.ToList());
                v.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ModTextEntry.Group)));
                return v;
            }

            var list = new ListBox
            {
                ItemsSource = ViewOf(entries),
                IsSynchronizedWithCurrentItem = false,
                Height = 240,
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x2E)),
                Foreground = white,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ItemTemplate = ItemTemplate(white, grey),
            };
            AutomationName(list, "Original texts");
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            VirtualizingPanel.SetIsVirtualizing(list, true);
            VirtualizingPanel.SetIsVirtualizingWhenGrouping(list, true);
            VirtualizingPanel.SetScrollUnit(list, ScrollUnit.Pixel);
            list.GroupStyle.Add(new GroupStyle { HeaderTemplate = GroupHeaderTemplate(pink) });
            stack.Children.Add(list);

            search.TextChanged += (_, _) =>
            {
                placeholder.Visibility = string.IsNullOrEmpty(search.Text) ? Visibility.Visible : Visibility.Collapsed;
                var shown = ModTextCatalog.Search(entries, search.Text);
                list.ItemsSource = ViewOf(shown);
                title.Text = string.IsNullOrWhiteSpace(search.Text)
                    ? $"Original texts ({entries.Count})"
                    : $"Original texts ({shown.Count} of {entries.Count})";
            };

            // Mouse up, not SelectionChanged: arrow keys must move through the list without
            // filling a row on every step. Enter picks the selected entry.
            list.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject src
                    && ItemsControl.ContainerFromElement(list, src) is ListBoxItem item
                    && item.Content is ModTextEntry entry)
                    onPick(entry.Text);
            };
            list.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && list.SelectedItem is ModTextEntry entry)
                {
                    onPick(entry.Text);
                    e.Handled = true;
                }
            };

            return root;
        }

        private static void AutomationName(DependencyObject o, string name) =>
            System.Windows.Automation.AutomationProperties.SetName(o, name);

        private static DataTemplate ItemTemplate(Brush text, Brush source)
        {
            var grid = new FrameworkElementFactory(typeof(Grid));
            var c0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            c0.SetValue(ColumnDefinition.WidthProperty, new GridLength(1, GridUnitType.Star));
            var c1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            c1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            grid.AppendChild(c0);
            grid.AppendChild(c1);
            grid.SetValue(FrameworkElement.ToolTipProperty, new Binding(nameof(ModTextEntry.Text)));

            var main = new FrameworkElementFactory(typeof(TextBlock));
            main.SetBinding(TextBlock.TextProperty, new Binding(nameof(ModTextEntry.Text)));
            main.SetValue(TextBlock.ForegroundProperty, text);
            main.SetValue(TextBlock.FontSizeProperty, 12.0);
            main.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            grid.AppendChild(main);

            var src = new FrameworkElementFactory(typeof(TextBlock));
            src.SetBinding(TextBlock.TextProperty, new Binding(nameof(ModTextEntry.Source)));
            src.SetValue(TextBlock.ForegroundProperty, source);
            src.SetValue(TextBlock.FontSizeProperty, 10.5);
            src.SetValue(FrameworkElement.MarginProperty, new Thickness(10, 0, 4, 0));
            src.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            src.SetValue(Grid.ColumnProperty, 1);
            grid.AppendChild(src);

            return new DataTemplate { VisualTree = grid };
        }

        private static DataTemplate GroupHeaderTemplate(Brush accent)
        {
            var header = new FrameworkElementFactory(typeof(TextBlock));
            header.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            header.SetValue(TextBlock.ForegroundProperty, accent);
            header.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            header.SetValue(TextBlock.FontSizeProperty, 12.0);
            header.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 8, 0, 2));
            return new DataTemplate { VisualTree = header };
        }
    }
}
