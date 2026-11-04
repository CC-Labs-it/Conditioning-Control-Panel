using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class ModCreatorWindow
{
    // Re-cut from cb33de077c20. WPF ModCreatorWindow.Barks.cs:127-350, 482-668.
    // This slice edits identity, trigger and variant text. Other loaded fields stay intact.
    private readonly List<BarkCard> _barkCards = new();
    private StackPanel? _barkCardsPanel;
    private string? _barkAudioDir;
    private sealed class BarkCard
    {
        public JObject Json = new();
        public bool LoadedFromFile;
        public StackPanel Panel = new(), Variants = new();
        public Border Frame = new();
        public TextBox Id = new(), Trigger = new();
        public CheckBox Override = new(), Repeatable = new();
        public TextBox Priority = new(), Cooldown = new(), Chance = new(), Mood = new(), Conditions = new();
        public ComboBox Scope = new(), Class = new();
        public readonly List<(TextBox Text, JToken Original, StackPanel Row)> Lines = new();
    }

    private void BuildBarksSection()
    {
        var stack = new StackPanel();
        CreateSectionPanel("barks").Child = stack;
        stack.Children.Add(CreateSectionHeader("Barks"));
        stack.Children.Add(CreateSectionDescription(Loc.Get("mod_barks_text_help")));
        _barkCardsPanel = new StackPanel();
        stack.Children.Add(_barkCardsPanel);
        stack.Children.Add(BarkButton("mod_barks_add", () => AddBarkCard(null)));
    }

    private static Button BarkButton(string key, Action action)
    {
        var button = new Button { Content = Loc.Get(key) };
        button.Click += (_, _) => action();
        return button;
    }

    private void AddBarkCard(JObject? loaded)
    {
        var card = new BarkCard { Json = loaded == null ? new JObject() : (JObject)loaded.DeepClone(), LoadedFromFile = loaded != null };
        card.Override.Content = Loc.Get("mod_barks_override");
        card.Override.IsChecked = loaded != null && loaded["trigger"] == null;
        var triggerLabel = CreateFieldLabel(Loc.Get("mod_barks_trigger"));
        var settings = new Border();
        void ShowTrigger() => triggerLabel.IsVisible = card.Trigger.IsVisible = settings.IsVisible = card.Override.IsChecked != true;
        ShowTrigger();
        card.Override.IsCheckedChanged += (_, _) => ShowTrigger();
        card.Id.Width = 260; card.Id.HorizontalAlignment = HorizontalAlignment.Left;
        card.Trigger.Width = 240; card.Trigger.HorizontalAlignment = HorizontalAlignment.Left;
        card.Id.Watermark = Loc.Get("mod_barks_id");
        card.Trigger.Watermark = Loc.Get("mod_barks_trigger");
        card.Frame = new Border
        {
            Background = Brush.Parse("#23233F"), BorderBrush = Brush.Parse("#35355A"),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10),
            MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Left, Child = card.Panel,
        };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var removeCard = BarkButton("mod_barks_remove", () =>
        {
            _barkCards.Remove(card);
            _barkCardsPanel?.Children.Remove(card.Frame);
        });
        removeCard.Background = Brushes.Transparent;
        removeCard.Foreground = Brush.Parse("#FF6464");
        removeCard.BorderThickness = new Thickness(0);
        removeCard.FontSize = 11;
        DockPanel.SetDock(removeCard, Dock.Right);
        header.Children.Add(removeCard);
        header.Children.Add(card.Override);
        card.Panel.Children.Add(header);
        card.Panel.Children.Add(CreateFieldLabel(Loc.Get("mod_barks_id")));
        card.Panel.Children.Add(card.Id);
        card.Panel.Children.Add(triggerLabel);
        card.Panel.Children.Add(card.Trigger);
        // WPF ModCreatorWindow.Barks.cs:209-246, 316-326.
        var fields = new StackPanel();
        settings.Child = fields;
        card.Panel.Children.Add(settings);
        var numeric = new StackPanel { Orientation = Orientation.Horizontal };
        AddBarkField(numeric, card.Priority, "mod_barks_priority", "0", 60, null);
        AddBarkField(numeric, card.Cooldown, "mod_barks_cooldown", "0", 80, null);
        AddBarkField(numeric, card.Chance, "mod_barks_chance", "1.0", 60, null);
        fields.Children.Add(numeric);
        var options = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        card.Repeatable.Content = Loc.Get("mod_barks_repeatable");
        card.Repeatable.IsChecked = true;
        options.Children.Add(card.Repeatable);
        AddBarkCombo(options, card.Scope, "mod_barks_scope", 100, null, "session", "tier", "lifetime");
        AddBarkCombo(options, card.Class, "mod_barks_class", 110, null, "normal", "easter_egg", "safety");
        fields.Children.Add(options);
        fields.Children.Add(CreateFieldLabel(Loc.Get("mod_barks_mood")));
        card.Mood.Tag = "mood"; card.Mood.Width = 260; card.Mood.HorizontalAlignment = HorizontalAlignment.Left;
        card.Mood.PlaceholderText = Loc.Get("mod_barks_mood_example");
        fields.Children.Add(card.Mood);
        fields.Children.Add(CreateFieldLabel(Loc.Get("mod_barks_conditions")));
        card.Conditions.Tag = "conditions"; card.Conditions.Width = 400; card.Conditions.Height = 56;
        card.Conditions.HorizontalAlignment = HorizontalAlignment.Left; card.Conditions.AcceptsReturn = true;
        card.Conditions.TextWrapping = TextWrapping.Wrap;
        card.Conditions.Watermark = "{ \"level_gte\": 10 }";
        fields.Children.Add(card.Conditions);
        ShowTrigger();
        card.Panel.Children.Add(CreateSubHeader(Loc.Get("mod_barks_line")));
        card.Panel.Children.Add(card.Variants);
        card.Panel.Children.Add(BarkButton("mod_barks_variant", () => AddBarkLine(card, new JValue(""))));
        // WPF registers the card before populating loaded values (Barks.cs:281-284).
        _barkCards.Add(card);
        _barkCardsPanel?.Children.Add(card.Frame);
        card.Id.Text = loaded?.Value<string>("id") ?? "";
        card.Trigger.Text = loaded?.Value<string>("trigger") ?? "";
        card.Priority.Text = loaded?.Value<int?>("priority")?.ToString(CultureInfo.InvariantCulture) ?? "";
        card.Cooldown.Text = loaded?.Value<int?>("cooldown_ms")?.ToString(CultureInfo.InvariantCulture) ?? "";
        card.Chance.Text = loaded?.Value<double?>("chance")?.ToString(CultureInfo.InvariantCulture) ?? "";
        card.Repeatable.IsChecked = loaded?.Value<bool?>("repeatable") ?? true;
        SelectBarkCombo(card.Scope, loaded?.Value<string>("scope"));
        SelectBarkCombo(card.Class, loaded?.Value<string>("class"));
        card.Mood.Text = loaded?.Value<string>("mood") ?? "";
        card.Conditions.Text = loaded?["conditions"] is JObject conditions ? conditions.ToString(Formatting.Indented) : "";
        if (loaded?["variant_pool"] is JArray pool)
            foreach (var line in pool) AddBarkLine(card, line);
        else if (loaded == null) AddBarkLine(card, new JValue(""));
    }

    private static void AddBarkField(StackPanel row, TextBox box, string key, string watermark, double width, string? value)
    {
        row.Children.Add(new TextBlock { Text = Loc.Get(key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        box.Tag = key; box.PlaceholderText = watermark; box.Text = value ?? ""; box.Width = width;
        box.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(box);
    }

    private static void AddBarkCombo(StackPanel row, ComboBox combo, string key, double width, string? value, params string[] items)
    {
        row.Children.Add(new TextBlock { Text = Loc.Get(key), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) });
        combo.Tag = key; combo.Width = width; combo.ItemsSource = items;
        SelectBarkCombo(combo, value);
        row.Children.Add(combo);
    }

    private static void SelectBarkCombo(ComboBox combo, string? value)
    {
        var items = (string[])combo.ItemsSource!;
        combo.SelectedItem = items.FirstOrDefault(i => string.Equals(i, value?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? items[0];
    }

    private static void SetBarkIntField(JObject rule, string key, TextBox box)
    {
        if (box.Text != box.PlaceholderText && int.TryParse(box.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) rule[key] = value;
        else rule.Remove(key);
    }

    private static void AddBarkLine(BarkCard card, JToken original)
    {
        // WPF ModCreatorWindow.Barks.cs:329-350 ignores all other token kinds.
        if (original.Type != JTokenType.String && original is not JObject) return;
        original = original.DeepClone();
        if (original is JObject legacy)
        {
            // WPF accepts both the current and legacy variant property names.
            if (legacy["text"] == null && legacy["display"] != null) legacy["text"] = legacy["display"]!.DeepClone();
            if (legacy["audio"] == null && legacy["file"] != null) legacy["audio"] = legacy["file"]!.DeepClone();
        }
        // Blank audio is text-only, including null and legacy file aliases (WPF:340-342).
        if (original is JObject line && string.IsNullOrWhiteSpace(line.Value<string>("audio")))
            original = new JValue(line.Value<string>("text") ?? "");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var text = new TextBox
        {
            Text = original is JObject obj ? obj.Value<string>("text") ?? "" : original.Value<string>() ?? "",
            Watermark = Loc.Get("mod_barks_line"), Width = 280, Margin = new Thickness(0, 0, 6, 0),
        };
        row.Children.Add(text);
        if (original is JObject voiced && voiced.Value<string>("audio") is string audio)
            row.Children.Add(new TextBlock { Text = audio, MaxWidth = 180, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(BarkButton("mod_barks_remove", () =>
        {
            card.Lines.RemoveAll(v => v.Row == row);
            card.Variants.Children.Remove(row);
        }));
        card.Lines.Add((text, original.DeepClone(), row));
        card.Variants.Children.Add(row);
    }

    internal List<string> WriteBarksTo(string resourcesDir)
    {
        var rules = new JArray();
        var badConditions = new List<string>();
        foreach (var card in _barkCards)
        {
            var isOverride = card.Override.IsChecked == true;
            var id = card.Id.Text?.Trim() ?? "";
            if (!isOverride) id = Regex.Replace(id.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
            if (id.Length == 0) continue;
            var pool = new JArray();
            foreach (var (box, original, _) in card.Lines)
            {
                var text = box.Text?.Trim() ?? "";
                if (original is JObject voiced)
                {
                    var copy = (JObject)voiced.DeepClone();
                    if (text.Length == 0 && string.IsNullOrWhiteSpace(copy.Value<string>("audio"))) continue;
                    copy["text"] = text;
                    pool.Add(copy);
                }
                else if (text.Length > 0) pool.Add(text);
            }
            var rule = (JObject)card.Json.DeepClone();
            if (isOverride)
            {
                if (pool.Count == 0) continue;
                // Fresh overrides emit only overlay fields. Loaded rules retain their backing fields.
                if (!card.LoadedFromFile) rule.RemoveAll();
            }
            else
            {
                var trigger = card.Trigger.Text?.Trim() ?? "";
                if (trigger.Length == 0 || (pool.Count == 0 && string.IsNullOrWhiteSpace(rule.Value<string>("pool_ref")))) continue;
                rule["trigger"] = trigger;
                // WPF ModCreatorWindow.Barks.cs:513-540: invalid conditions retain the backing value.
                SetBarkIntField(rule, "priority", card.Priority);
                SetBarkIntField(rule, "cooldown_ms", card.Cooldown);
                if (card.Chance.Text != card.Chance.PlaceholderText && double.TryParse(card.Chance.Text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var chance))
                    rule["chance"] = Math.Clamp(chance, 0.0, 1.0);
                else rule.Remove("chance");
                rule["repeatable"] = card.Repeatable.IsChecked == true;
                rule["scope"] = card.Scope.SelectedItem as string ?? "session";
                rule["class"] = card.Class.SelectedItem as string ?? "normal";
                var mood = card.Mood.Text == card.Mood.PlaceholderText ? "" : card.Mood.Text?.Trim() ?? "";
                if (mood.Length > 0) rule["mood"] = mood; else rule.Remove("mood");
                var conditions = card.Conditions.Text == card.Conditions.Watermark ? "" : card.Conditions.Text?.Trim() ?? "";
                if (conditions.Length == 0) rule.Remove("conditions");
                else
                {
                    try { rule["conditions"] = JObject.Parse(conditions); }
                    catch (JsonException) { badConditions.Add(id); }
                }
            }
            rule["id"] = id;
            if (pool.Count > 0) rule["variant_pool"] = pool;
            else rule.Remove("variant_pool");
            card.Json = (JObject)rule.DeepClone();
            rules.Add(rule);
        }
        if (rules.Count == 0) return badConditions;
        var dir = Path.Combine(resourcesDir, "sounds", "companion_audio");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "bark_rules.json"), rules.ToString(Formatting.Indented));
        // Only copy files referenced by exported rules. A package cannot address paths outside its audio folder.
        foreach (var audio in rules.OfType<JObject>().SelectMany(r => (r["variant_pool"] as JArray ?? new JArray())
                     .OfType<JObject>()).Select(v => v.Value<string>("audio")).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (_barkAudioDir == null || audio.Contains('/') || audio.Contains('\\') || audio != Path.GetFileName(audio)) continue;
            var source = Path.Combine(_barkAudioDir, audio);
            var target = Path.Combine(dir, audio);
            try
            {
                if (File.Exists(source) && Path.GetFullPath(source) != Path.GetFullPath(target)) File.Copy(source, target, true);
            }
            catch (IOException) { /* Keep the reference, as WPF does for unreadable clips. */ }
            catch (UnauthorizedAccessException) { }
        }
        return badConditions;
    }

    internal void LoadBarksFrom(string resourcesDir)
    {
        ClearBarksSection();
        _barkAudioDir = Path.Combine(resourcesDir, "sounds", "companion_audio");
        var path = Path.Combine(_barkAudioDir, "bark_rules.json");
        if (!File.Exists(path)) return;
        try
        {
            var rules = JArray.Parse(File.ReadAllText(path));
            foreach (var rule in rules.OfType<JObject>()) AddBarkCard(rule);
        }
        catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException
                                   || ex is FormatException || ex is InvalidCastException || ex is OverflowException || ex is ArgumentException)
        {
            _txtStatus.Text = Loc.Get("mod_barks_load_error");
        }
    }

    internal void ClearBarksSection()
    {
        _barkCards.Clear();
        _barkCardsPanel?.Children.Clear();
        _barkAudioDir = null;
    }
}
