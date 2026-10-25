using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using IconKind = FluentIcons.Common.Symbol;
using ConditioningControlPanel.Avalonia.Tours;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ModCreatorFinishTests
{
    private static object? Call(ModCreatorWindow w, string method, params object?[] args) =>
        typeof(ModCreatorWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Run(Action<ModCreatorWindow, string> test) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Path.Combine(Path.GetTempPath(), "ccp-creator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var w = new ModCreatorWindow();
        try { test(w, dir); }
        finally { w.Close(); Dispatcher.UIThread.RunJobs(); Directory.Delete(dir, true); }
    });
    private static StackPanel Section(ModCreatorWindow w, string key, string heading)
    {
        Click(w.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, key)));
        return (StackPanel)w.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == heading).Parent!;
    }
    private static void Add(StackPanel section, string label) =>
        Click(section.Children.OfType<Button>().Single(b => Equals(b.Content, label)));
    private static TextBox Box(Control root, string placeholder) =>
        root.GetLogicalDescendants().OfType<TextBox>().First(t => t.PlaceholderText == placeholder);
    private static string AudioDir(string dir) => Path.Combine(dir, "sounds", "companion_audio");

    [Fact]
    public void MantraCardsValidateDefaultDeduplicateAndRemove()
    {
        Run((w, dir) =>
        {
            var section = Section(w, "mantras", "Mantras");
            var cards = (StackPanel)section.Children[3];
            for (var i = 0; i < 4; i++) Add(section, "+ Add mantra");
            Box(cards.Children[0], "good girls obey").Text = "  Repeat Me  ";
            Box(cards.Children[1], "good girls obey").Text = "Repeat Me";
            Box(cards.Children[1], "Say it for me, sweetie: good girls obey").Text = "Please REPEAT ME";
            Box(cards.Children[2], "good girls obey").Text = "missing";
            Box(cards.Children[2], "Say it for me, sweetie: good girls obey").Text = "wrong prompt";
            var warnings = w.WriteMantrasTo(dir);
            Assert.Single(warnings);
            var json = JObject.Parse(File.ReadAllText(Path.Combine(AudioDir(dir), "mantras.json")));
            var entries = (JArray)json["mantras"]!;
            Assert.Equal(2, entries.Count);
            Assert.Equal("Repeat Me", (string?)entries[0]!["promptText"]);
            Assert.NotEqual((string?)entries[0]!["id"], (string?)entries[1]!["id"]);
            // WPF "\u2715 Remove" / warning prefix are icons here, not dropped glyphs.
            Assert.Contains(IconKind.Warning, cards.Children[0].GetLogicalDescendants().OfType<IconGlyph>().Select(g => g.Kind));
            var remove = cards.Children[0].GetLogicalDescendants().OfType<Button>().Single(b =>
                b.Content is StackPanel sp && sp.Children.OfType<TextBlock>().Any(t => t.Text == "Remove"));
            Assert.Equal(IconKind.Dismiss, ((StackPanel)remove.Content!).Children.OfType<IconGlyph>().Single().Kind);
            Click(remove);
            Assert.Equal(3, cards.Children.Count);
            Call(w, "ClearSideFileState");
            Assert.Empty(cards.Children);
            Assert.Empty(w.WriteMantrasTo(Path.Combine(dir, "empty")));
            Assert.False(Directory.Exists(Path.Combine(dir, "empty")));
        });
    }

    [Fact]
    public void MantraLoadPreservesClipsMissingReferencesAndSharedLines()
    {
        Run((w, dir) =>
        {
            Directory.CreateDirectory(AudioDir(dir));
            File.WriteAllText(Path.Combine(AudioDir(dir), "mantra_yes_prompt.mp3"), "clip");
            File.WriteAllText(Path.Combine(AudioDir(dir), "mantras.json"), """
                {"Mantras":[{"Id":"keep","Phrase":"yes","PromptText":"say YES","PromptAudio":"mantra_yes_prompt.mp3","ResponseAudio":"missing.wav","Enabled":false}],
                 "Retry":[{"Text":"again","Audio":"mantra_yes_prompt.mp3"}],"Timeout":[{"Text":"hello"}]}
                """);
            w.LoadMantrasFrom(dir).GetAwaiter().GetResult();
            var section = Section(w, "mantras", "Mantras");
            Assert.Contains(section.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "missing.wav (missing)");
            var output = Path.Combine(dir, "out");
            Assert.Empty(w.WriteMantrasTo(output));
            var json = JObject.Parse(File.ReadAllText(Path.Combine(AudioDir(output), "mantras.json")));
            Assert.Equal("keep", (string?)json["mantras"]![0]!["id"]);
            Assert.False((bool)json["mantras"]![0]!["enabled"]!);
            Assert.Equal("missing.wav", (string?)json["mantras"]![0]!["responseAudio"]);
            Assert.Single((JArray)json["retry"]!);
            Assert.Single((JArray)json["timeout"]!);
            Assert.Equal("clip", File.ReadAllText(Path.Combine(AudioDir(output), "mantra_yes_prompt.mp3")));
            Assert.Equal("mantra_yes_prompt_2.mp3", Call(w, "MantraAutoFileName", "Yes!", "prompt", ".mp3"));
            w.WriteMantrasTo(dir); // self-copy is safe
        });
    }

    [Fact]
    public void PreviewTogglesReplacesIgnoresStaleFinishAndStopsOnClose()
    {
        Run((w, dir) =>
        {
            var old = CoreAudio.PlayStoppableProvider;
            var stops = 0;
            Action? finish = null;
            CoreAudio.PlayStoppableProvider = (_, volume, tag, _, ended) =>
            { Assert.Equal(1f, volume); Assert.Equal("modcreator-preview", tag); finish = ended; return () => stops++; };
            try
            {
                var path = Path.Combine(dir, "clip.wav"); File.WriteAllText(path, "fake");
                var a = new Button(); var b = new Button();
                Call(w, "ToggleAudioPreview", path, a);
                var stale = finish;
                Call(w, "ToggleAudioPreview", path, b); Assert.Equal(1, stops);
                stale!(); Dispatcher.UIThread.RunJobs(); Assert.Equal(1, stops);
                Call(w, "ToggleAudioPreview", path, b); Assert.Equal(2, stops);
                Call(w, "ToggleAudioPreview", path, a);
                finish!(); Dispatcher.UIThread.RunJobs(); Assert.Equal(3, stops);
                Call(w, "ToggleAudioPreview", path, a);
                PanicSurfaces.All.Single(s => s.Id == "mod-creator-preview").Stop(null);
                Assert.Equal(4, stops);
                Call(w, "ToggleAudioPreview", path, a);
                w.Close(); Assert.Equal(5, stops);
            }
            finally { CoreAudio.PlayStoppableProvider = old; }
        });
    }

    [Fact]
    public void UiArtRegistersAllWpfSlotsAndTutorialStepsNavigate()
    {
        Run((w, _) =>
        {
            var section = Section(w, "uiart", "UI Art");
            Assert.Equal(34, section.Children.OfType<WrapPanel>().Sum(p => p.Children.Count));
            TutorialHead.Seed();
            TutorialHead.Service.Start(TutorialType.Modding);
            try
            {
                w.WireModdingSteps();
                foreach (var step in TutorialHead.Service.CurrentSteps.Where(s => s.RequiresTab?.StartsWith("mod:") == true))
                {
                    Assert.NotNull(step.OnActivate);
                    step.OnActivate();
                    Assert.Equal(step.RequiresTab![4..], typeof(ModCreatorWindow)
                        .GetField("_activeSectionKey", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w));
                }
            }
            finally { TutorialHead.Service.Skip(); }
        });
    }
}
