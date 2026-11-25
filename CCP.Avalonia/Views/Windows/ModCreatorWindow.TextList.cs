using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Text.RegularExpressions;
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
                var rows = new ListBox { Height = 220 };
                rows.AddHandler(KeyDownEvent, (_, e) =>
                {
                    if (e.Key == Key.Enter && rows.SelectedItem is ListBoxItem { Tag: ModTextEntry entry })
                    { PickOriginalText(entry.Text); e.Handled = true; }
                }, RoutingStrategies.Tunnel);
                var export = new Button { Content = Loc.Get("mod_text_export"), HorizontalAlignment = HorizontalAlignment.Left };
                export.Click += async (_, _) => await ExportOriginalTexts(entries);
                browser.Children.Add(search);
                browser.Children.Add(count);
                browser.Children.Add(export);
                browser.Children.Add(rows);
                void Refresh()
                {
                    var matches = ModTextCatalog.Search(entries, search.Text);
                    count.Text = Loc.GetF("mod_text_count", matches.Count, entries.Count);
                    rows.Items.Clear();
                    string? group = null;
                    foreach (var entry in matches)
                    {
                        if (entry.Group != group)
                        {
                            group = entry.Group;
                            rows.Items.Add(new ListBoxItem { IsEnabled = false, Content = new TextBlock { Text = LocalizeTextGroup(group), FontWeight = FontWeight.Bold } });
                        }
                        var item = new ListBoxItem { Tag = entry, Content = new TextBlock { Text = entry.Text + "   (" + LocalizeTextSource(entry.Source) + ")", TextWrapping = TextWrapping.Wrap } };
                        item.AddHandler(PointerReleasedEvent, (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) PickOriginalText(entry.Text); }, RoutingStrategies.Bubble, true);
                        rows.Items.Add(item);
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

        internal static string LocalizeTextGroup(string group) => group.StartsWith("Skill: ", StringComparison.Ordinal)
            ? Loc.GetF("mod_text_skill_group", group.Substring(7)) : Loc.Get("mod_text_meta_" + MetadataKey(group));

        internal static string LocalizeTextSource(string source) => source.StartsWith("rank title, level ", StringComparison.Ordinal)
            ? Loc.GetF("mod_text_rank_source", source.Substring(18).TrimEnd('+')) : Loc.Get("mod_text_meta_" + MetadataKey(source));

        private static string MetadataKey(string value) => Regex.Replace(value.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');

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
                await WriteOriginalTexts(file, entries);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Mod Creator original text export failed");
                await MessageDialog.ShowAsync(this, Loc.Get("mod_text_export"), Loc.Get("mod_text_export_error"));
            }
        }
        internal async Task WriteOriginalTexts(IStorageFile file, IReadOnlyList<ModTextEntry> entries)
        {
            await using (var stream = await file.OpenWriteAsync())
            await using (var writer = new StreamWriter(stream))
                await writer.WriteAsync(ModTextCatalog.ToExportText(entries, CoreReleaseContent.AppVersion, Loc.Get, LocalizeTextGroup));
            _txtStatus.Text = Loc.GetF("mod_text_export_success", entries.Count, file.Name);
        }
    }
}
