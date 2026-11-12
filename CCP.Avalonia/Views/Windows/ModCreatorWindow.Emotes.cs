using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Mods;
using ConditioningControlPanel.Avalonia.Views.Dialogs;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class ModCreatorWindow
{
    // WPF ModCreatorWindow.Emotes.cs:55-264, 487-570.
    private readonly List<EmoteRow> _emoteRows = new();
    private StackPanel? _emoteRowsPanel;
    private bool _revertingEmoteSet;
    private sealed class EmoteRow
    {
        public Border Frame = new();
        public ComboBox Set = new();
        public TextBlock Folder = new(), Stats = new(), Issues = new();
        public string? Path;
        public bool Valid;
        public int Previous;
    }

    private void BuildEmotesSection()
    {
        var stack = new StackPanel();
        CreateSectionPanel("emotes").Child = stack;
        stack.Children.Add(CreateSectionHeader(Loc.Get("mod_emote_title")));
        stack.Children.Add(CreateSectionDescription(Loc.Get("mod_emote_help")));
        _emoteRowsPanel = new StackPanel { MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left };
        stack.Children.Add(_emoteRowsPanel);
        var add = new Button { Content = Loc.Get("mod_emote_add") };
        add.Click += (_, _) =>
        {
            var number = Enumerable.Range(1, 7).FirstOrDefault(n => _emoteRows.All(r => r.Set.SelectedIndex + 1 != n));
            if (number == 0) EmoteNotice(Loc.Get("mod_emote_all_used"));
            else AddEmoteRow(number, null);
        };
        stack.Children.Add(add);
    }

    private void EmoteNotice(string text)
    {
        _txtStatus.Text = text;
        if (IsVisible) _ = MessageDialog.ShowAsync(this, Loc.Get("mod_emote_title"), text);
    }

    private void AddEmoteRow(int number, string? folder)
    {
        if (_emoteRowsPanel == null) return;
        var row = new EmoteRow { Previous = number };
        var body = new StackPanel();
        row.Frame = new Border { Child = body, Background = Brush.Parse("#252542"), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8), Margin = new Thickness(0, 0, 0, 6) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Set.ItemsSource = Enumerable.Range(1, 7).Select(n => Loc.GetF("mod_emote_set", n)).ToArray();
        row.Set.SelectedIndex = number - 1;
        row.Set.SelectionChanged += (_, _) =>
        {
            if (_revertingEmoteSet) return;
            var selected = row.Set.SelectedIndex + 1;
            if (_emoteRows.Any(r => r != row && r.Set.SelectedIndex + 1 == selected))
            {
                _revertingEmoteSet = true;
                row.Set.SelectedIndex = row.Previous - 1;
                _revertingEmoteSet = false;
                EmoteNotice(Loc.GetF("mod_emote_taken", selected));
            }
            else row.Previous = selected;
        };
        top.Children.Add(row.Set);
        var import = new Button { Content = Loc.Get("mod_emote_import") };
        import.Click += async (_, _) =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = Loc.Get("mod_emote_picker"), AllowMultiple = false });
            var path = picked.FirstOrDefault()?.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) SetEmoteFolder(row, path);
        };
        top.Children.Add(import);
        row.Folder.Text = Loc.Get("mod_emote_empty");
        row.Folder.VerticalAlignment = VerticalAlignment.Center;
        row.Folder.MaxWidth = 260;
        row.Folder.TextTrimming = TextTrimming.CharacterEllipsis;
        top.Children.Add(row.Folder);
        var remove = new Button { Content = Loc.Get("mod_barks_remove") };
        remove.Click += (_, _) => { _emoteRows.Remove(row); _emoteRowsPanel.Children.Remove(row.Frame); };
        top.Children.Add(remove);
        body.Children.Add(top);
        row.Stats.FontSize = 11;
        row.Issues.FontSize = 11;
        row.Issues.TextWrapping = TextWrapping.Wrap;
        body.Children.Add(row.Stats);
        body.Children.Add(row.Issues);
        _emoteRows.Add(row);
        _emoteRowsPanel.Children.Add(row.Frame);
        if (!string.IsNullOrEmpty(folder)) SetEmoteFolder(row, folder);
    }

    private void SetEmoteFolder(EmoteRow row, string folder)
    {
        row.Path = folder;
        var validation = EmoteSetValidation.Inspect(folder);
        row.Valid = validation.IsValid;
        row.Folder.Text = System.IO.Path.GetFileName(folder.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));
        ToolTip.SetTip(row.Folder, folder);
        row.Stats.Text = Loc.GetF("mod_emote_stats", validation.GifCount, FormatEmoteSize(validation.TotalBytes));
        var issues = validation.Errors.Concat(validation.Warnings).Select(i => Loc.GetF(i.Key, i.Arg ?? "")).ToList();
        if (!row.Valid) issues.Add(Loc.Get("mod_emote_invalid"));
        row.Issues.Text = string.Join("\n", issues);
        row.Issues.Foreground = Brush.Parse(row.Valid ? "#FFA500" : "#FF6464");
    }

    // WPF ModCreatorWindow.Emotes.cs:471-477.
    private static string FormatEmoteSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):0.#} MB";
        if (bytes >= 1024L) return $"{bytes / 1024.0:0.#} KB";
        return $"{bytes} B";
    }

    private void WriteEmotesTo(string resourcesDir)
    {
        foreach (var row in _emoteRows.Where(r => r.Valid && r.Path != null))
        {
            var dest = System.IO.Path.Combine(resourcesDir, "emotes", $"set{row.Set.SelectedIndex + 1}");
            try
            {
                if (Directory.Exists(dest)) Directory.Delete(dest, true);
                CopyEmoteFolder(row.Path!, dest);
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Could not copy emote set {Folder}", row.Path); }
        }
    }

    private static void CopyEmoteFolder(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(src))
        {
            var name = System.IO.Path.GetFileName(file);
            if (name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || name.Equals("emotes.json", StringComparison.OrdinalIgnoreCase) || name.Equals("clip_timing.json", StringComparison.OrdinalIgnoreCase))
                File.Copy(file, System.IO.Path.Combine(dest, name), true);
        }
        foreach (var dir in Directory.EnumerateDirectories(src)) CopyEmoteFolder(dir, System.IO.Path.Combine(dest, System.IO.Path.GetFileName(dir)));
        if (!Directory.EnumerateFileSystemEntries(dest).Any()) Directory.Delete(dest);
    }

    private void LoadEmotesFrom(string resourcesDir)
    {
        ClearEmotesSection();
        var root = System.IO.Path.Combine(resourcesDir, "emotes");
        if (!Directory.Exists(root)) return;
        try
        {
            var sets = Directory.EnumerateDirectories(root, "set*").Select(d => (Dir: d, Name: System.IO.Path.GetFileName(d)))
                .Select(x => (x.Dir, Num: int.TryParse(x.Name.AsSpan(3), out var n) ? n : 0))
                .Where(x => x.Num >= 1 && x.Num <= 7 && File.Exists(System.IO.Path.Combine(x.Dir, "emotes.json")))
                .OrderBy(x => x.Num).ToList();
            foreach (var set in sets) AddEmoteRow(set.Num, set.Dir);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Could not scan emote sets {Folder}", root); }
    }

    private void ClearEmotesSection()
    {
        _emoteRows.Clear();
        _emoteRowsPanel?.Children.Clear();
    }
}
