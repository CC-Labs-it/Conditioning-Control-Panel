using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class ModCreatorWindow
    {
        private void AddOriginalTextBrowser(StackPanel stack)
        {
            try
            {
                var entries = ModTextCatalog.Build();
                var browser = new StackPanel { Spacing = 5, Margin = new global::Avalonia.Thickness(0, 4, 0, 12) };
                browser.Children.Add(new TextBlock { Text = Loc.Get("mod_text_originals") });
                var search = new TextBox { PlaceholderText = Loc.Get("mod_text_search") };
                var count = new TextBlock();
                var rows = new StackPanel();
                var scroller = new ScrollViewer { Content = rows, Height = 220 };
                var export = new Button { Content = Loc.Get("mod_text_export"), HorizontalAlignment = HorizontalAlignment.Left };
                export.Click += async (_, _) => await ExportOriginalTexts(entries);
                browser.Children.Add(search);
                browser.Children.Add(count);
                browser.Children.Add(export);
                browser.Children.Add(scroller);
                void Refresh()
                {
                    var matches = ModTextCatalog.Search(entries, search.Text);
                    count.Text = Loc.GetF("mod_text_count", matches.Count, entries.Count);
                    rows.Children.Clear();
                    string? group = null;
                    foreach (var entry in matches)
                    {
                        if (entry.Group != group)
                        {
                            group = entry.Group;
                            rows.Children.Add(new TextBlock { Text = group, FontWeight = global::Avalonia.Media.FontWeight.Bold });
                        }
                        var button = new Button { Content = new TextBlock { Text = entry.Display, TextWrapping = global::Avalonia.Media.TextWrapping.Wrap }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
                        button.Click += (_, _) => PickOriginalText(entry.Text);
                        rows.Children.Add(button);
                    }
                }
                search.TextChanged += (_, _) => Refresh();
                Refresh();
                stack.Children.Add(browser);
            }
            catch (Exception ex)
            {
                // The manual replacement editor remains available when catalog creation fails.
                Log.Warning(ex, "Mod Creator original text browser unavailable");
            }
        }

        private void PickOriginalText(string text)
        {
            var row = _textReplacements.FirstOrDefault(r => r.From.Text == text);
            if (row.From == null)
            {
                row = _textReplacements.LastOrDefault(r => string.IsNullOrWhiteSpace(r.From.Text) && string.IsNullOrWhiteSpace(r.To.Text));
                if (row.From == null)
                {
                    AddReplacementRow(text, "");
                    row = _textReplacements.Last();
                }
                else row.From.Text = text;
            }
            var target = row.To;
            Dispatcher.UIThread.Post(() => { target.BringIntoView(); target.Focus(); target.CaretIndex = target.Text?.Length ?? 0; });
        }

        private async Task ExportOriginalTexts(IReadOnlyList<ModTextEntry> entries)
        {
            try
            {
                var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = Loc.Get("mod_text_export"), SuggestedFileName = "ccp-original-texts.txt",
                    FileTypeChoices = new[] { new FilePickerFileType("Text") { Patterns = new[] { "*.txt" } } }
                });
                if (file == null) return;
                await using var stream = await file.OpenWriteAsync();
                await using var writer = new StreamWriter(stream);
                await writer.WriteAsync(ModTextCatalog.ToExportText(entries, null));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Mod Creator original text export failed");
                await MessageDialog.ShowAsync(this, Loc.Get("mod_text_export"), Loc.Get("mod_text_export_error"));
            }
        }
    }
}
