using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ModCreatorBarksTests
{
    private static void Run(Action<ModCreatorWindow, string> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily("fonts:Inter#Inter"),
                FontStyle.Normal, FontWeight.Bold), out _))
            FontManager.Current.AddFontCollection(new global::Avalonia.Fonts.Inter.InterFontCollection());
        var root = Path.Combine(Path.GetTempPath(), "ccp-barks-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var w = new ModCreatorWindow();
        try { body(w, root); }
        finally { w.Close(); Dispatcher.UIThread.RunJobs(); Directory.Delete(root, true); }
    });
    private static StackPanel Cards(ModCreatorWindow w) =>
        (StackPanel)typeof(ModCreatorWindow).GetField("_barkCardsPanel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
    private static StackPanel Card(ModCreatorWindow w) => (StackPanel)((Border)Cards(w).Children[0]).Child!;
    private static StackPanel Variants(StackPanel card) => card.Children.OfType<StackPanel>().Single();
    private static void Click(Control scope, string key) => scope.GetLogicalDescendants().OfType<Button>()
        .First(b => Equals(b.Content, Loc.Get(key))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string FileAt(string root) => Path.Combine(root, "sounds", "companion_audio", "bark_rules.json");
    private static JArray Export(ModCreatorWindow w, string root)
    {
        // Exercise the actual export hook, not just the panel helper.
        typeof(ModCreatorWindow).GetMethod("WriteSideFilesTo", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { root });
        return JArray.Parse(File.ReadAllText(FileAt(root)));
    }

    [Fact]
    public void NewRuleSlugsIdTrimsVariantsAndSkipsIncompleteCards() => Run((w, root) =>
    {
        Click(w, "mod_barks_add"); Click(w, "mod_barks_add");
        var card = Card(w);
        var boxes = card.GetLogicalDescendants().OfType<TextBox>().ToList();
        boxes[0].Text = " My Rule "; boxes[1].Text = " ChaosRunStarted "; Variants(card).GetLogicalDescendants().OfType<TextBox>().Single().Text = " hello ";
        Click(card, "mod_barks_variant"); // blank variant skipped
        var rule = (JObject)Assert.Single(Export(w, root));
        Assert.Equal("my_rule", rule.Value<string>("id"));
        Assert.Equal("ChaosRunStarted", rule.Value<string>("trigger"));
        Assert.Equal("hello", Assert.Single((JArray)rule["variant_pool"]!).Value<string>());
        Assert.True(rule.Value<bool>("repeatable"));
        Assert.Equal("session", rule.Value<string>("scope"));
        Assert.Equal("normal", rule.Value<string>("class"));
    });

    [Fact]
    public void OverrideEmitsOnlyIdAndPoolAndRemoveAndResetClearState() => Run((w, root) =>
    {
        Click(w, "mod_barks_add");
        var card = Card(w);
        card.GetLogicalDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, Loc.Get("mod_barks_override"))).IsChecked = true;
        var boxes = card.GetLogicalDescendants().OfType<TextBox>().ToList();
        boxes[0].Text = "Base.Id"; Variants(card).GetLogicalDescendants().OfType<TextBox>().Single().Text = "replacement";
        Assert.False(boxes[1].IsVisible);
        var rule = (JObject)Assert.Single(Export(w, root));
        Assert.Equal(new[] { "id", "variant_pool" }, rule.Properties().Select(p => p.Name));
        Assert.Equal("Base.Id", rule.Value<string>("id"));
        Click(Variants(card), "mod_barks_remove"); // remove variant
        Assert.Empty(Variants(card).Children);
        Click(card.Children.OfType<DockPanel>().Single(), "mod_barks_remove");
        Assert.Empty(Cards(w).Children);
        Click(w, "mod_barks_add");
        typeof(ModCreatorWindow).GetMethod("ClearSideFileState", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, null);
        Assert.Empty(Cards(w).Children);
        var empty = Path.Combine(root, "empty"); w.WriteBarksTo(empty);
        Assert.False(File.Exists(FileAt(empty)));
    });

    [Fact]
    public void LoadRetainsUnknownFieldsConditionsAudioAndMissingReferencesAndReplacesOldState() => Run((w, root) =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileAt(root))!);
        File.WriteAllText(FileAt(root), "[{id:'loaded',trigger:'Event',pool_ref:'shared',conditions:{x:1},future:42,variant_pool:[{text:'old',audio:'a.mp3'},{display:'missing',file:'missing.mp3'},{text:'outside',audio:'../outside.mp3'}]},{id:'overlay',future:7,variant_pool:['line']}]");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(FileAt(root))!, "a.mp3"), "audio");
        typeof(ModCreatorWindow).GetMethod("LoadSideFilesFrom", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { root });
        var card = Card(w);
        Variants(card).GetLogicalDescendants().OfType<TextBox>().First().Text = "edited";
        File.WriteAllText(Path.Combine(root, "sounds", "outside.mp3"), "outside");
        var output = Path.Combine(root, "out"); var result = Export(w, output);
        Assert.False(File.Exists(Path.Combine(output, "sounds", "outside.mp3")));
        Assert.Equal(2, result.Count);
        Assert.Equal(42, result[0].Value<int>("future"));
        Assert.Equal(1, result[0]["conditions"]!.Value<int>("x"));
        Assert.Equal("shared", result[0].Value<string>("pool_ref"));
        Assert.Equal("edited", result[0]["variant_pool"]![0]!.Value<string>("text"));
        Assert.Equal("missing.mp3", result[0]["variant_pool"]![1]!.Value<string>("audio"));
        Assert.Equal("audio", File.ReadAllText(Path.Combine(Path.GetDirectoryName(FileAt(output))!, "a.mp3")));
        Assert.Equal(7, result[1].Value<int>("future"));
        File.WriteAllText(FileAt(root), "broken"); w.LoadBarksFrom(root);
        Assert.Empty(Cards(w).Children);
        w.LoadBarksFrom(Path.Combine(root, "absent"));
        Assert.Empty(Cards(w).Children);
    });

    [Fact]
    public void MixedVariantTypesAreIgnoredAndBlankAudioBecomesTextOnly() => Run((w, root) =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileAt(root))!);
        File.WriteAllText(FileAt(root), "[{id:'mixed',trigger:'Event',variant_pool:[5,true,null,[],['bad'],'keep',{text:'object'}, {text:'null',audio:null},{text:'empty',audio:''},{text:'spaces',audio:'  '},{display:'legacy',file:'  '},{text:'voiced',audio:'valid.mp3'}]}]");
        w.LoadBarksFrom(root);
        var lines = Variants(Card(w)).GetLogicalDescendants().OfType<TextBox>().ToList();
        Assert.Equal(new[] { "keep", "object", "null", "empty", "spaces", "legacy", "voiced" }, lines.Select(b => b.Text));
        var first = (JArray)Export(w, Path.Combine(root, "before"))[0]["variant_pool"]!;
        Assert.All(first.Take(6), token => Assert.Equal(JTokenType.String, token.Type));
        foreach (var line in lines.Skip(1).Take(5)) line.Text = " ";
        var pool = (JArray)Export(w, Path.Combine(root, "after"))[0]["variant_pool"]!;
        Assert.Equal(2, pool.Count);
        Assert.Equal("keep", pool[0].Value<string>());
        Assert.Equal("valid.mp3", pool[1].Value<string>("audio"));
    });

    [Fact]
    public void CardsKeepLabelsAndSeparateHeaderRemovalFromHorizontalVariantRows() => Run((w, root) =>
    {
        Click(w, "mod_barks_add"); Click(w, "mod_barks_add");
        var card = Card(w);
        var frame = Assert.IsType<Border>(card.Parent);
        Assert.Equal(new Thickness(12), frame.Padding);
        Assert.Equal(new Thickness(1), frame.BorderThickness);
        Assert.Equal(new CornerRadius(6), frame.CornerRadius);
        Assert.Equal(640, frame.MaxWidth);
        Assert.NotNull(frame.Background); Assert.NotNull(frame.BorderBrush);
        var header = card.Children.OfType<DockPanel>().Single();
        Assert.Equal(Dock.Right, DockPanel.GetDock(header.Children.OfType<Button>().Single(b => Equals(b.Content, Loc.Get("mod_barks_remove")))));
        Assert.Equal(new[] { Loc.Get("mod_barks_id"), Loc.Get("mod_barks_trigger"), Loc.Get("mod_barks_line") }, card.Children.OfType<TextBlock>().Select(t => t.Text));
        Assert.All(Variants(card).Children.OfType<StackPanel>(), row => Assert.Equal(Orientation.Horizontal, row.Orientation));
        card.GetLogicalDescendants().OfType<TextBox>().First().Text = "first_rule";
        card.GetLogicalDescendants().OfType<TextBox>().ElementAt(1).Text = "Event";
        Variants(card).GetLogicalDescendants().OfType<TextBox>().Single().Text = "A text-only variant";
        var evidence = Environment.GetEnvironmentVariable("CCP_BARKS_PROOF_DIR");
        if (!string.IsNullOrEmpty(evidence))
        {
            Directory.CreateDirectory(evidence);
            var second = (StackPanel)((Border)Cards(w).Children[1]).Child!;
            second.GetLogicalDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, Loc.Get("mod_barks_override"))).IsChecked = true;
            second.GetLogicalDescendants().OfType<TextBox>().First().Text = "base_rule";
            Variants(second).GetLogicalDescendants().OfType<TextBox>().Single().Text = "Replacement line";
            w.Height = 1100;
            w.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, "barks")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            w.Show(); Dispatcher.UIThread.RunJobs();
            using var image = w.CaptureRenderedFrame();
            Assert.NotNull(image); image!.Save(Path.Combine(evidence, "barks-cards.png"));
        }
    });
    [Fact]
    public void RuleSettingsLoadEditAndExportWithWpfParsing() => Run((w, root) =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileAt(root))!);
        File.WriteAllText(FileAt(root), "[{id:'loaded',trigger:'Event',priority:4,cooldown_ms:20,chance:0.4,repeatable:false,scope:'tier',class:'safety',mood:'soft',conditions:{level_gte:10},future:42,variant_pool:['line']}]");
        w.LoadBarksFrom(root);
        var card = Card(w);
        TextBox Box(string tag) => card.GetLogicalDescendants().OfType<TextBox>().Single(b => Equals(b.Tag, tag));
        ComboBox Combo(string tag) => card.GetLogicalDescendants().OfType<ComboBox>().Single(b => Equals(b.Tag, tag));
        var repeatable = card.GetLogicalDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, Loc.Get("mod_barks_repeatable")));
        Assert.Equal("4", Box("mod_barks_priority").Text);
        Assert.Equal("20", Box("mod_barks_cooldown").Text);
        Assert.Equal("0.4", Box("mod_barks_chance").Text);
        Assert.False(repeatable.IsChecked);
        Assert.Equal("tier", Combo("mod_barks_scope").SelectedItem);
        Assert.Equal("safety", Combo("mod_barks_class").SelectedItem);
        Assert.Equal("soft", Box("mood").Text);
        Assert.Equal(10, JObject.Parse(Box("conditions").Text!).Value<int>("level_gte"));
        Box("mod_barks_priority").Text = "-3"; Box("mod_barks_cooldown").Text = "bad";
        Box("mod_barks_chance").Text = "1.7"; repeatable.IsChecked = true;
        Combo("mod_barks_scope").SelectedItem = "lifetime"; Combo("mod_barks_class").SelectedItem = "easter_egg";
        Box("mood").Text = " teasing "; Box("conditions").Text = "{level_gte:20}";
        var rule = (JObject)Assert.Single(Export(w, Path.Combine(root, "edited")));
        Assert.Equal(-3, rule.Value<int>("priority")); Assert.Null(rule["cooldown_ms"]);
        Assert.Equal(1.0, rule.Value<double>("chance")); Assert.True(rule.Value<bool>("repeatable"));
        Assert.Equal("lifetime", rule.Value<string>("scope")); Assert.Equal("easter_egg", rule.Value<string>("class"));
        Assert.Equal("teasing", rule.Value<string>("mood")); Assert.Equal(20, rule["conditions"]!.Value<int>("level_gte"));
        Assert.Equal(42, rule.Value<int>("future"));
        Box("mod_barks_priority").Text = ""; Box("mod_barks_chance").Text = "-2";
        Box("mood").Text = " "; Box("conditions").Text = "";
        rule = (JObject)Assert.Single(Export(w, Path.Combine(root, "cleared")));
        Assert.Null(rule["priority"]); Assert.Equal(0.0, rule.Value<double>("chance"));
        Assert.Null(rule["mood"]); Assert.Null(rule["conditions"]);
    });

    [Fact]
    public void InvalidConditionsAreReportedAndPreviousObjectIsKept() => Run((w, root) =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileAt(root))!);
        File.WriteAllText(FileAt(root), "[{id:'loaded',trigger:'Event',conditions:{safe:true},variant_pool:['line']}]");
        w.LoadBarksFrom(root);
        var card = Card(w);
        var conditions = card.GetLogicalDescendants().OfType<TextBox>().Single(b => Equals(b.Tag, "conditions"));
        conditions.Text = "{safe:false}";
        Assert.Empty(w.WriteBarksTo(Path.Combine(root, "valid")));
        conditions.Text = "[invalid]";
        var output = Path.Combine(root, "invalid");
        var invalid = w.WriteBarksTo(output);
        Assert.Equal(new[] { "loaded" }, invalid);
        Assert.False(JArray.Parse(File.ReadAllText(FileAt(output)))[0]["conditions"]!.Value<bool>("safe"));
        var overrideBox = card.GetLogicalDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, Loc.Get("mod_barks_override")));
        overrideBox.IsChecked = true;
        Assert.False(((Control)((Control)conditions.Parent!).Parent!).IsVisible);
        Assert.Empty(w.WriteBarksTo(Path.Combine(root, "override")));
    });

    [Fact]
    public void FreshRuleExportedThenChangedToOverrideExportsOnlyOverlayFields() => Run((w, root) =>
    {
        Click(w, "mod_barks_add");
        var card = Card(w);
        var boxes = card.GetLogicalDescendants().OfType<TextBox>().ToList();
        boxes[0].Text = "my_rule"; boxes[1].Text = "Event";
        boxes.Single(b => Equals(b.Tag, "mod_barks_priority")).Text = "9";
        boxes.Single(b => Equals(b.Tag, "conditions")).Text = "{level_gte:10}";
        Variants(card).GetLogicalDescendants().OfType<TextBox>().Single().Text = "hello";
        var original = (JObject)Assert.Single(Export(w, Path.Combine(root, "new")));
        Assert.Equal("Event", original.Value<string>("trigger"));
        Assert.Equal(9, original.Value<int>("priority"));
        Assert.Equal(10, original["conditions"]!.Value<int>("level_gte"));

        card.GetLogicalDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, Loc.Get("mod_barks_override"))).IsChecked = true;
        boxes[0].Text = "Base.Id";
        var result = (JObject)Assert.Single(Export(w, Path.Combine(root, "override")));
        Assert.Equal(new[] { "id", "variant_pool" }, result.Properties().Select(p => p.Name));
        Assert.Equal("Base.Id", result.Value<string>("id"));
        Assert.Equal("hello", Assert.Single((JArray)result["variant_pool"]!).Value<string>());
    });

    [Fact]
    public void LoadedRuleChangedToOverrideKeepsBackingFields() => Run((w, root) =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileAt(root))!);
        File.WriteAllText(FileAt(root), "[{id:'loaded',trigger:'Event',conditions:{level_gte:10},future:42,variant_pool:['line']}]");
        w.LoadBarksFrom(root);
        var card = Card(w);
        card.GetLogicalDescendants().OfType<CheckBox>().Single(c => Equals(c.Content, Loc.Get("mod_barks_override"))).IsChecked = true;
        var result = (JObject)Assert.Single(Export(w, Path.Combine(root, "override")));
        Assert.Equal("Event", result.Value<string>("trigger"));
        Assert.Equal(10, result["conditions"]!.Value<int>("level_gte"));
        Assert.Equal(42, result.Value<int>("future"));
        Assert.Equal("line", Assert.Single((JArray)result["variant_pool"]!).Value<string>());
    });

    [Theory]
    [InlineData("-3", "-7", -3, -7)]
    [InlineData("4", "-7", 4, -7)]
    [InlineData("4.5", "-2.5", 4, -2)]
    [InlineData("4", "-2.5", 4, -2)]
    public void LoadedIntegerSettingsRoundTripUnderSwedishCulture(string priority, string cooldown, int expectedPriority, int expectedCooldown) => Run((w, root) =>
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
            Directory.CreateDirectory(Path.GetDirectoryName(FileAt(root))!);
            File.WriteAllText(FileAt(root), $"[{{id:'loaded',trigger:'Event',priority:{priority},cooldown_ms:{cooldown},variant_pool:['line']}}]");
            w.LoadBarksFrom(root);
            var result = (JObject)Assert.Single(Export(w, Path.Combine(root, "out")));
            Assert.Equal(expectedPriority, result.Value<int?>("priority"));
            Assert.Equal(expectedCooldown, result.Value<int?>("cooldown_ms"));
            var boxes = Card(w).GetLogicalDescendants().OfType<TextBox>();
            Assert.Equal(expectedPriority.ToString(CultureInfo.InvariantCulture), boxes.Single(b => Equals(b.Tag, "mod_barks_priority")).Text);
            Assert.Equal(expectedCooldown.ToString(CultureInfo.InvariantCulture), boxes.Single(b => Equals(b.Tag, "mod_barks_cooldown")).Text);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    });

    [Theory]
    [InlineData("en", "Mood (comma-separated flavor words)", "e.g. teasing, soft")]
    [InlineData("fr", "Humeur (mots d'ambiance séparés par des virgules)", "p. ex. teasing, soft")]
    [InlineData("de", "Stimmung (durch Kommas getrennte Stimmungswörter)", "z. B. teasing, soft")]
    [InlineData("es", "Ánimo (palabras de ambiente separadas por comas)", "p. ej. teasing, soft")]
    [InlineData("it", "Umore (parole descrittive separate da virgole)", "es. teasing, soft")]
    [InlineData("ja", "雰囲気（カンマ区切りの表現）", "例: teasing, soft")]
    [InlineData("ko", "분위기 (쉼표로 구분한 표현)", "예: teasing, soft")]
    [InlineData("pt-BR", "Humor (palavras de ambiente separadas por vírgulas)", "ex. teasing, soft")]
    [InlineData("ru", "Настроение (слова через запятую)", "напр. teasing, soft")]
    [InlineData("zh-CN", "情绪（用逗号分隔的描述词）", "例如: teasing, soft")]
    public void MoodFieldShowsLocalizedFormatAndExample(string language, string label, string example) => Run((w, root) =>
    {
        var manager = LocalizationManager.Instance;
        var previous = manager.CurrentLanguage;
        var add = w.GetLogicalDescendants().OfType<Button>().First(b => Equals(b.Content, Loc.Get("mod_barks_add")));
        try
        {
            manager.SetLanguage(language);
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var mood = Card(w).GetLogicalDescendants().OfType<TextBox>().Single(b => Equals(b.Tag, "mood"));
            var fields = (StackPanel)mood.Parent!;
            Assert.Contains(fields.Children.OfType<TextBlock>(), t => t.Text == label);
            Assert.Equal(example, mood.PlaceholderText);
        }
        finally { manager.SetLanguage(previous); }
    });

    [Theory]
    [InlineData("priority")]
    [InlineData("cooldown_ms")]
    [InlineData("chance")]
    public void NullNumericFieldsLoadWithoutAbortingAndPreserveOverrides(string key) => Run((w, root) =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileAt(root))!);
        File.WriteAllText(FileAt(root), $"[{{id:'overlay',{key}:null,variant_pool:['line']}},{{id:'new',trigger:'Event',{key}:null,variant_pool:['line']}}]");
        typeof(ModCreatorWindow).GetMethod("LoadSideFilesFrom", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { root });
        Assert.Equal(2, Cards(w).Children.Count);
        var rules = Export(w, Path.Combine(root, "out"));
        Assert.Equal(JTokenType.Null, rules[0][key]!.Type);
        Assert.Null(rules[1][key]);
    });

    [Theory]
    [InlineData("priority", "'bad'")]
    [InlineData("priority", "2147483648")]
    [InlineData("cooldown_ms", "[]")]
    [InlineData("chance", "'bad'")]
    public void MalformedNumericFieldsStopBarkLoadLocallyAndKeepPriorCards(string key, string value) => Run((w, root) =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileAt(root))!);
        File.WriteAllText(FileAt(root), $"[{{id:'good',trigger:'Event',variant_pool:['line']}},{{id:'bad',trigger:'Event',{key}:{value},variant_pool:['line']}},{{id:'later',trigger:'Event',variant_pool:['line']}}]");
        typeof(ModCreatorWindow).GetMethod("LoadSideFilesFrom", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(w, new object[] { root });
        Assert.Equal(2, Cards(w).Children.Count);
        Assert.Equal("good", Card(w).GetLogicalDescendants().OfType<TextBox>().First().Text);
        Assert.Equal(Loc.Get("mod_barks_load_error"), w.FindControl<TextBlock>("TxtStatus")!.Text);
    });

    [Theory]
    [InlineData("priority", "0", false)]
    [InlineData("cooldown_ms", "0", false)]
    [InlineData("chance", "1.0", false)]
    [InlineData("priority", " 0 ", true)]
    [InlineData("cooldown_ms", " 0 ", true)]
    [InlineData("chance", " 1.0 ", true)]
    [InlineData("chance", "1", true)]
    public void NumericPlaceholderTextIsOmittedBeforeTrimming(string key, string value, bool present) => Run((w, root) =>
    {
        Click(w, "mod_barks_add");
        var card = Card(w);
        var boxes = card.GetLogicalDescendants().OfType<TextBox>().ToList();
        boxes[0].Text = "rule"; boxes[1].Text = "Event";
        Variants(card).GetLogicalDescendants().OfType<TextBox>().Single().Text = "line";
        var tag = key == "cooldown_ms" ? "mod_barks_cooldown" : "mod_barks_" + key;
        boxes.Single(b => Equals(b.Tag, tag)).Text = value;
        var rule = (JObject)Assert.Single(Export(w, root));
        if (present) Assert.NotNull(rule[key]); else Assert.Null(rule[key]);
    });

    [Fact]
    public void LoadedZeroFieldsMatchWpfPlaceholderAndChanceFormatting() => Run((w, root) =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileAt(root))!);
        File.WriteAllText(FileAt(root), "[{id:'loaded',trigger:'Event',priority:0,cooldown_ms:0,chance:1.0,variant_pool:['line']}]");
        w.LoadBarksFrom(root);
        var rule = (JObject)Assert.Single(Export(w, Path.Combine(root, "out")));
        Assert.Null(rule["priority"]); Assert.Null(rule["cooldown_ms"]);
        Assert.Equal(1.0, rule.Value<double>("chance"));
    });

    [Fact]
    public void ConditionsEditorWrapsLongJson() => Run((w, root) =>
    {
        Click(w, "mod_barks_add");
        var conditions = Card(w).GetLogicalDescendants().OfType<TextBox>().Single(b => Equals(b.Tag, "conditions"));
        Assert.Equal(TextWrapping.Wrap, conditions.TextWrapping);
    });

    [Theory]
    [InlineData("repeatable", "'bad'")]
    [InlineData("scope", "[]")]
    [InlineData("mood", "[]")]
    [InlineData("id", "[]")]
    public void MalformedLoadedFieldsKeepTheCard(string key, string value) => Run((w, root) =>
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FileAt(root))!);
        var property = key == "id" ? $"id:{value}" : $"id:'bad',{key}:{value}";
        File.WriteAllText(FileAt(root), $"[{{{property},trigger:'Event',variant_pool:['line']}}]");
        w.LoadBarksFrom(root);
        Assert.Single(Cards(w).Children);
        Assert.Equal(Loc.Get("mod_barks_load_error"), w.FindControl<TextBlock>("TxtStatus")!.Text);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExamplesAreEmptyOnlyBeforeTrimming(bool padded) => Run((w, root) =>
    {
        Click(w, "mod_barks_add");
        var card = Card(w);
        var boxes = card.GetLogicalDescendants().OfType<TextBox>().ToList();
        boxes[0].Text = "rule"; boxes[1].Text = "Event";
        Variants(card).GetLogicalDescendants().OfType<TextBox>().Single().Text = "line";
        var mood = boxes.Single(b => Equals(b.Tag, "mood"));
        var conditions = boxes.Single(b => Equals(b.Tag, "conditions"));
        mood.Text = padded ? " " + mood.PlaceholderText + " " : mood.PlaceholderText;
        conditions.Text = padded ? " " + conditions.Watermark + " " : conditions.Watermark;
        var rule = (JObject)Assert.Single(Export(w, root));
        if (padded)
        {
            Assert.Equal(mood.PlaceholderText, rule.Value<string>("mood"));
            Assert.Equal(10, rule["conditions"]!.Value<int>("level_gte"));
        }
        else { Assert.Null(rule["mood"]); Assert.Null(rule["conditions"]); }
    });

}
