using System;
using System.Collections;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ModCreatorEmotesTests
{
    private static void Run(Action<ModCreatorWindow, string> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily("fonts:Inter#Inter"), FontStyle.Normal, FontWeight.Bold), out _))
            FontManager.Current.AddFontCollection(new global::Avalonia.Fonts.Inter.InterFontCollection());
        var previous = LocalizationManager.Instance.CurrentLanguage;
        LocalizationManager.Instance.SetLanguage("en");
        var dir = Path.Combine(Path.GetTempPath(), "ccp-emotes-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var window = new ModCreatorWindow();
        try { body(window, dir); }
        finally { window.Close(); Dispatcher.UIThread.RunJobs(); LocalizationManager.Instance.SetLanguage(previous); Directory.Delete(dir, true); }
    });
    private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static IList Rows(ModCreatorWindow w) => (IList)typeof(ModCreatorWindow).GetField("_emoteRows", Flags)!.GetValue(w)!;
    private static void Hook(ModCreatorWindow w, string name, params object[] args) => typeof(ModCreatorWindow).GetMethod(name, Flags)!.Invoke(w, args);
    private static T Field<T>(object row, string name) => (T)row.GetType().GetField(name)!.GetValue(row)!;
    private static void Add(ModCreatorWindow w) => w.GetLogicalDescendants().OfType<Button>()
        .First(b => Equals(b.Content, Loc.Get("mod_emote_add"))).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static string Set(string root, string name, string map)
    {
        var dir = Path.Combine(root, name); Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "emotes.json"), map);
        File.WriteAllText(Path.Combine(dir, "idle.gif"), "clip");
        return dir;
    }

    [Fact]
    public void ImportValidatesExportsOnlyValidAndReloads() => Run((w, root) =>
    {
        var bad = Set(root, "bad", "{mood:{happy:'missing'}}");
        var good = Set(root, "good", "{}");
        File.WriteAllText(Path.Combine(good, "ignore.txt"), "junk");
        Add(w); Add(w);
        Hook(w, "SetEmoteFolder", Rows(w)[0]!, bad); Hook(w, "SetEmoteFolder", Rows(w)[1]!, good);
        Assert.False(Field<bool>(Rows(w)[0]!, "Valid"));
        Assert.Contains("missing.gif", Field<TextBlock>(Rows(w)[0]!, "Issues").Text);
        Assert.True(Field<bool>(Rows(w)[1]!, "Valid"));
        var output = Path.Combine(root, "out"); Hook(w, "WriteSideFilesTo", output);
        Assert.False(Directory.Exists(Path.Combine(output, "emotes", "set1")));
        Assert.True(File.Exists(Path.Combine(output, "emotes", "set2", "idle.gif")));
        Assert.False(File.Exists(Path.Combine(output, "emotes", "set2", "ignore.txt")));
        Hook(w, "ClearSideFileState"); Assert.Empty(Rows(w));
        Hook(w, "LoadSideFilesFrom", output);
        Assert.Single(Rows(w));
        Assert.Equal(1, Field<ComboBox>(Rows(w)[0]!, "Set").SelectedIndex);
        Assert.True(Field<bool>(Rows(w)[0]!, "Valid"));
    });

    [Theory]
    [InlineData(0L, "0 B", "en-US")]
    [InlineData(1023L, "1023 B", "en-US")]
    [InlineData(1024L, "1 KB", "en-US")]
    [InlineData(1536L, "1.5 KB", "en-US")]
    [InlineData(1048576L, "1 MB", "en-US")]
    [InlineData(1572864L, "1.5 MB", "en-US")]
    [InlineData(1073741824L, "1 GB", "en-US")]
    [InlineData(1342177280L, "1.25 GB", "en-US")]
    [InlineData(1536L, "1,5 KB", "de-DE")]
    public void SizeFormattingMatchesWpf(long bytes, string expected, string cultureName)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            var method = typeof(ModCreatorWindow).GetMethod("FormatEmoteSize", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.Equal(expected, method.Invoke(null, new object[] { bytes }));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Fact]
    public void SmallFolderShowsKilobytesInStats() => Run((w, root) =>
    {
        var dir = Set(root, "small", "{}");
        using (var file = File.Create(Path.Combine(dir, "idle.gif"))) file.SetLength(1022);
        Add(w); Hook(w, "SetEmoteFolder", Rows(w)[0]!, dir);
        Assert.Equal("1 GIFs, 1 KB", Field<TextBlock>(Rows(w)[0]!, "Stats").Text);
    });

    [Fact]
    public void InvalidSetIsExcludedFromExport() => Run((w, root) =>
    {
        var bad = Set(root, "bad", "{mood:{happy:'missing'}}");
        Add(w); Hook(w, "SetEmoteFolder", Rows(w)[0]!, bad);
        Assert.False(Field<bool>(Rows(w)[0]!, "Valid"));
        Hook(w, "WriteSideFilesTo", Path.Combine(root, "out"));
        Assert.False(Directory.Exists(Path.Combine(root, "out", "emotes", "set1")));
    });

    [Fact]
    public void ValidSetIsExported() => Run((w, root) =>
    {
        var good = Set(root, "good", "{}");
        Add(w); Hook(w, "SetEmoteFolder", Rows(w)[0]!, good);
        Hook(w, "WriteSideFilesTo", Path.Combine(root, "out"));
        Assert.True(File.Exists(Path.Combine(root, "out", "emotes", "set1", "idle.gif")));
    });

    [Fact]
    public void LoadHookRepopulatesPreparedSets() => Run((w, root) =>
    {
        var emotes = Path.Combine(root, "emotes"); Directory.CreateDirectory(emotes);
        Set(emotes, "set2", "{}");
        Hook(w, "LoadSideFilesFrom", root);
        Assert.Single(Rows(w));
        Assert.Equal(1, Field<ComboBox>(Rows(w)[0]!, "Set").SelectedIndex);
    });

    [Fact]
    public void ClearHookRemovesRows() => Run((w, _) =>
    {
        Add(w); Assert.Single(Rows(w));
        Hook(w, "ClearSideFileState");
        Assert.Empty(Rows(w));
    });

    [Fact]
    public void SevenSetNumbersAreAvailableOnlyOnce() => Run((w, _) =>
    {
        for (var i = 0; i < 8; i++) Add(w);
        Assert.Equal(7, Rows(w).Count);
        Assert.Equal(Enumerable.Range(0, 7), Rows(w).Cast<object>().Select(row => Field<ComboBox>(row, "Set").SelectedIndex));
    });

    [Fact]
    public void DuplicateSetRevertsAndRemovedNumberCanBeReused() => Run((w, _) =>
    {
        Add(w); Add(w);
        var second = Field<ComboBox>(Rows(w)[1]!, "Set");
        second.SelectedIndex = 0;
        Assert.Equal(1, second.SelectedIndex);
        var first = Field<Border>(Rows(w)[0]!, "Frame");
        first.GetLogicalDescendants().OfType<Button>().First(b => Equals(b.Content, Loc.Get("mod_barks_remove")))
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Add(w);
        Assert.Equal(0, Field<ComboBox>(Rows(w)[1]!, "Set").SelectedIndex);
    });

    [Fact]
    public void ValidationMessagesUseTheCurrentLanguageAndArguments() => Run((w, root) =>
    {
        Add(w);
        LocalizationManager.Instance.SetLanguage("de");
        var bad = Set(root, "bad", "{mood:{happy:'missing'}}");
        Hook(w, "SetEmoteFolder", Rows(w)[0]!, bad);
        var text = Field<TextBlock>(Rows(w)[0]!, "Issues").Text!;
        Assert.Contains(Loc.GetF("mod_emote_clips_missing", "missing.gif"), text);
        Assert.DoesNotContain("Missing clips", text);
    });
}
