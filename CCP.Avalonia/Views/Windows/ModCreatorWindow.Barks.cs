using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Avalonia.Controls;
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
        public StackPanel Panel = new(), Variants = new();
        public TextBox Id = new(), Trigger = new();
        public CheckBox Override = new();
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
        var card = new BarkCard { Json = loaded == null ? new JObject() : (JObject)loaded.DeepClone() };
        card.Id.Text = loaded?.Value<string>("id") ?? "";
        card.Trigger.Text = loaded?.Value<string>("trigger") ?? "";
        card.Override.Content = Loc.Get("mod_barks_override");
        card.Override.IsChecked = loaded != null && loaded["trigger"] == null;
        card.Trigger.IsVisible = card.Override.IsChecked != true;
        card.Override.IsCheckedChanged += (_, _) => card.Trigger.IsVisible = card.Override.IsChecked != true;
        card.Id.Watermark = Loc.Get("mod_barks_id");
        card.Trigger.Watermark = Loc.Get("mod_barks_trigger");
        card.Panel.Children.Add(card.Override);
        card.Panel.Children.Add(card.Id);
        card.Panel.Children.Add(card.Trigger);
        card.Panel.Children.Add(card.Variants);
        card.Panel.Children.Add(BarkButton("mod_barks_variant", () => AddBarkLine(card, new JValue(""))));
        card.Panel.Children.Add(BarkButton("mod_barks_remove", () =>
        {
            _barkCards.Remove(card);
            _barkCardsPanel?.Children.Remove(card.Panel);
        }));
        if (loaded?["variant_pool"] is JArray pool)
            foreach (var line in pool) AddBarkLine(card, line);
        else if (loaded == null) AddBarkLine(card, new JValue(""));
        _barkCards.Add(card);
        _barkCardsPanel?.Children.Add(card.Panel);
    }

    private static void AddBarkLine(BarkCard card, JToken original)
    {
        original = original.DeepClone();
        if (original is JObject legacy)
        {
            // WPF accepts both the current and legacy variant property names.
            if (legacy["text"] == null && legacy["display"] != null) legacy["text"] = legacy["display"]!.DeepClone();
            if (legacy["audio"] == null && legacy["file"] != null) legacy["audio"] = legacy["file"]!.DeepClone();
        }
        var row = new StackPanel();
        var text = new TextBox
        {
            Text = original is JObject obj ? obj.Value<string>("text") ?? "" : original.Value<string>() ?? "",
            Watermark = Loc.Get("mod_barks_line"),
        };
        row.Children.Add(text);
        if (original is JObject voiced && voiced.Value<string>("audio") is string audio)
            row.Children.Add(new TextBlock { Text = audio });
        row.Children.Add(BarkButton("mod_barks_remove", () =>
        {
            card.Lines.RemoveAll(v => v.Row == row);
            card.Variants.Children.Remove(row);
        }));
        card.Lines.Add((text, original.DeepClone(), row));
        card.Variants.Children.Add(row);
    }

    internal void WriteBarksTo(string resourcesDir)
    {
        var rules = new JArray();
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
                    if (text.Length == 0 && copy["audio"] == null) continue;
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
                if (card.Json.Count == 0) rule.RemoveAll();
            }
            else
            {
                var trigger = card.Trigger.Text?.Trim() ?? "";
                if (trigger.Length == 0 || (pool.Count == 0 && string.IsNullOrWhiteSpace(rule.Value<string>("pool_ref")))) continue;
                rule["trigger"] = trigger;
                if (card.Json.Count == 0)
                {
                    rule["repeatable"] = true;
                    rule["scope"] = "session";
                    rule["class"] = "normal";
                }
            }
            rule["id"] = id;
            if (pool.Count > 0) rule["variant_pool"] = pool;
            else rule.Remove("variant_pool");
            rules.Add(rule);
        }
        if (rules.Count == 0) return;
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
        catch (Exception ex) when (ex is JsonException || ex is IOException || ex is UnauthorizedAccessException)
        {
            ClearBarksSection();
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
