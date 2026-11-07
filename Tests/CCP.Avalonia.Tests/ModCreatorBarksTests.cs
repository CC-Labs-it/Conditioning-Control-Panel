using System;
using System.IO;
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
        boxes[0].Text = " My Rule "; boxes[1].Text = " ChaosRunStarted "; boxes[2].Text = " hello ";
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
        card.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true;
        var boxes = card.GetLogicalDescendants().OfType<TextBox>().ToList();
        boxes[0].Text = "Base.Id"; boxes[2].Text = "replacement";
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
        card.GetLogicalDescendants().OfType<TextBox>().ElementAt(2).Text = "edited";
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
            second.GetLogicalDescendants().OfType<CheckBox>().Single().IsChecked = true;
            second.GetLogicalDescendants().OfType<TextBox>().First().Text = "base_rule";
            Variants(second).GetLogicalDescendants().OfType<TextBox>().Single().Text = "Replacement line";
            w.Height = 1100;
            w.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, "barks")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            w.Show(); Dispatcher.UIThread.RunJobs();
            using var image = w.CaptureRenderedFrame();
            Assert.NotNull(image); image!.Save(Path.Combine(evidence, "barks-cards.png"));
        }
    });
}
