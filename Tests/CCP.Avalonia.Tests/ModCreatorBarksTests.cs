using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Mod Creator Barks panel (WPF ModCreatorWindow.Barks.cs): driven through the sidebar and "+ Add rule",
/// written by the same WriteBarksTo the Export button calls, then read back by the Load path.
/// </summary>
public sealed class ModCreatorBarksTests
{
    private static void Run(Action<ModCreatorWindow, string> body) =>
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var dir = Path.Combine(Path.GetTempPath(), "ccp-barks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var window = new ModCreatorWindow();
            try { body(window, dir); }
            finally
            {
                window.Close();
                Dispatcher.UIThread.RunJobs();
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        });

    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static string Label(Button b) => b.Content as string ?? (b.Content as TextBlock)?.Text ?? "";

    private static StackPanel BarksSection(ModCreatorWindow w)
    {
        Click(w.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, "barks")));
        return (StackPanel)w.GetLogicalDescendants().OfType<TextBlock>()
            .First(t => t.Text?.StartsWith("Reactive companion voicelines") == true).Parent!;
    }

    private static TextBox Box(Control card, string placeholder) =>
        card.GetLogicalDescendants().OfType<TextBox>().First(t => t.PlaceholderText == placeholder);

    [Fact]
    public void NewRuleExportsBarkRulesJsonInWpfShape()
    {
        Run((w, dir) =>
        {
            var stack = BarksSection(w);
            Assert.True(((Control)stack.Parent!).IsVisible);
            Click(stack.Children.OfType<Button>().Single(b => Label(b) == "+ Add rule"));
            var card = (Border)((StackPanel)stack.Children[2]).Children.Single();

            Box(card, "e.g. my_custom_bark").Text = "My Custom Bark!";
            Box(card, "e.g. ChaosRunStarted").Text = " LevelUp ";
            Box(card, "1.0").Text = "1.7";
            Box(card, "e.g. teasing, soft").Text = "soft";
            Box(card, "{ \"level_gte\": 10 }").Text = "{ broken";
            Box(card, "Line text...").Text = "  good girl  ";

            var bad = w.WriteBarksTo(dir);
            Assert.Equal(new[] { "my_custom_bark" }, bad);

            var rule = (JObject)JArray.Parse(File.ReadAllText(
                Path.Combine(dir, "sounds", "companion_audio", "bark_rules.json"))).Single();
            Assert.Equal("my_custom_bark", (string?)rule["id"]);
            Assert.Equal("LevelUp", (string?)rule["trigger"]);
            Assert.Equal(1.0, (double)rule["chance"]!);
            Assert.Null(rule["priority"]);            // blank numeric fields are not emitted
            Assert.Null(rule["conditions"]);
            Assert.True((bool)rule["repeatable"]!);
            Assert.Equal("session", (string?)rule["scope"]);
            Assert.Equal("normal", (string?)rule["class"]);
            Assert.Equal("soft", (string?)rule["mood"]);
            Assert.Equal("good girl", (string?)((JArray)rule["variant_pool"]!).Single());
        });
    }

    [Fact]
    public void LoadedOverrideKeepsUnsurfacedFieldsAndAudioLinks()
    {
        Run((w, dir) =>
        {
            var audioDir = Path.Combine(dir, "in", "sounds", "companion_audio");
            Directory.CreateDirectory(audioDir);
            File.WriteAllText(Path.Combine(audioDir, "pop_1.mp3"), "x");
            File.WriteAllText(Path.Combine(audioDir, "bark_rules.json"), new JArray(
                new JObject { ["id"] = "bubble_pop", ["pool_ref"] = "keep_me",
                    ["variant_pool"] = new JArray(new JObject { ["text"] = "pop!", ["audio"] = "pop_1.mp3" }, "quiet") },
                new JObject { ["id"] = "no_lines", ["trigger"] = "X" }).ToString());

            w.LoadBarksFrom(Path.Combine(dir, "in"));
            var stack = BarksSection(w);
            var cards = ((StackPanel)stack.Children[2]).Children.OfType<Border>().ToList();
            Assert.Equal(2, cards.Count);
            var mode = cards[0].GetLogicalDescendants().OfType<ComboBox>().First();
            Assert.Equal(1, mode.SelectedIndex);    // no trigger -> override mode
            Assert.Contains(cards[0].GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "pop_1.mp3");

            var outDir = Path.Combine(dir, "out");
            Assert.Empty(w.WriteBarksTo(outDir));
            var written = Path.Combine(outDir, "sounds", "companion_audio");
            var rules = JArray.Parse(File.ReadAllText(Path.Combine(written, "bark_rules.json")));
            var rule = (JObject)rules.Single();       // "no_lines" has no pool and no pool_ref: skipped
            Assert.Equal("keep_me", (string?)rule["pool_ref"]);
            Assert.Null(rule["trigger"]);
            Assert.Equal("pop_1.mp3", (string?)rule["variant_pool"]![0]!["audio"]);
            Assert.Equal("quiet", (string?)rule["variant_pool"]![1]);
            Assert.True(File.Exists(Path.Combine(written, "pop_1.mp3")));
        });
    }
}
