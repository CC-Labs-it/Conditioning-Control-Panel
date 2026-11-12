using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform.Storage;
using System.IO.Enumeration;
using Avalonia.LogicalTree;
using Avalonia.Automation;
using Avalonia.Interactivity;
using ConditioningControlPanel;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json.Linq;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class ModCreatorEventAudioTests
{
    [Fact]
    public void ImportExportLoadAndResetRoundTrip() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetTempPath(), "ccp-event-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var w = new ModCreatorWindow();
        try
        {
            var input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
            File.WriteAllText(Path.Combine(input, "Hello World.mp3"), "audio");
            w.ImportEventAudioFrom(input);
            var panel = (StackPanel)typeof(ModCreatorWindow).GetField("_eventAudioRowsPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
            Assert.Single(panel.Children);
            Assert.Contains(panel.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text != null && x.Text.Contains("clean slug"));
            void Hook(string name, params object[] args) => typeof(ModCreatorWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);
            var output = Path.Combine(root, "output"); Hook("WriteSideFilesTo", output);
            Assert.Equal("audio", File.ReadAllText(Path.Combine(output, "sounds", "event_audio", "hello world.mp3")));
            Hook("ClearSideFileState"); Assert.Empty(panel.Children);
            Hook("LoadSideFilesFrom", output); Assert.Single(panel.Children);
            Assert.True(panel.GetLogicalDescendants().OfType<TextBox>().Single().IsReadOnly);
            Hook("ClearSideFileState"); Assert.Empty(panel.Children);

            var add = w.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, "+ Add line"));
            add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(panel.Children);
            var free = panel.GetLogicalDescendants().OfType<TextBox>().Single();
            Assert.False(free.IsReadOnly);
            free.Text = "A phrase!";
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(panel.GetLogicalDescendants().OfType<TextBlock>(), x => x.Text == "a phrase.mp3");
            var remove = panel.GetLogicalDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Remove line");
            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(panel.Children);

            w.ImportEventAudioFrom(input);
            var play = panel.GetLogicalDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Play preview");
            Assert.True(play.IsVisible);
            var oldPlayer = CoreAudio.PlayStoppableProvider;
            string? played = null;
            var stops = 0;
            CoreAudio.PlayStoppableProvider = (path, _, _, _, _) => { played = path; return () => stops++; };
            try
            {
                play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(Path.Combine(input, "Hello World.mp3"), played);
                play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, stops);
            }
            finally { CoreAudio.PlayStoppableProvider = oldPlayer; }
        }
        finally { w.Close(); Dispatcher.UIThread.RunJobs(); Directory.Delete(root, true); }
    });
    [Fact]
    public void UppercaseMp3ImportsAndLoadsForReexport() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetTempPath(), "ccp-event-case-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var w = new ModCreatorWindow();
        try
        {
            var input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
            File.WriteAllText(Path.Combine(input, "Imported.MP3"), "imported");
            File.WriteAllText(Path.Combine(input, "ignore.txt"), "ignore");
            w.ImportEventAudioFrom(input);
            var output = Path.Combine(root, "out"); w.WriteEventAudioTo(output);
            Assert.Equal("imported", File.ReadAllText(Path.Combine(output, "sounds", "event_audio", "imported.mp3")));
            var pack = Path.Combine(root, "pack", "sounds", "event_audio"); Directory.CreateDirectory(pack);
            File.WriteAllText(Path.Combine(pack, "loaded.MP3"), "loaded");
            File.WriteAllText(Path.Combine(pack, "ignore.txt"), "ignore");
            w.LoadEventAudioFrom(Path.Combine(root, "pack"));
            var reexport = Path.Combine(root, "reexport"); w.WriteEventAudioTo(reexport);
            Assert.Equal("loaded", File.ReadAllText(Path.Combine(reexport, "sounds", "event_audio", "loaded.mp3")));
            Assert.Single(Directory.GetFiles(Path.Combine(reexport, "sounds", "event_audio")));
        }
        finally { w.Close(); Dispatcher.UIThread.RunJobs(); Directory.Delete(root, true); }
    });

    [Fact]
    public void EventAudioCopyIsTranslatedAndResourcesExistInEveryLanguage() => AvaloniaTestDispatcher.Run(() =>
    {
        var keys = "title intro lines add import help fixed phrase attach preview remove flag folder pick error empty imported flagged duplicate".Split(' ');
        foreach (var (code, _, _) in LocalizationManager.AvailableLanguages)
        {
            var file = Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", code + ".json");
            var strings = JObject.Parse(File.ReadAllText(file));
            foreach (var key in keys) Assert.False(string.IsNullOrWhiteSpace((string?)strings["mod_event_" + key]));
        }
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var loc = LocalizationManager.Instance; var previous = loc.CurrentLanguage;
        var root = Path.Combine(Path.GetTempPath(), "ccp-event-loc-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            loc.SetLanguage("fr");
            var w = new ModCreatorWindow();
            try
            {
                var elements = w.GetLogicalDescendants().ToList();
                Assert.Contains(elements.OfType<Button>(), b => Equals(b.Tag, "eventaudio") && Equals(b.Content, Loc.Get("mod_event_title")));
                Assert.Contains(elements.OfType<TextBlock>(), t => t.Text == Loc.Get("mod_event_intro"));
                Assert.Contains(elements.OfType<Button>(), b => Equals(b.Content, Loc.Get("mod_event_import")) && Equals(ToolTip.GetTip(b), Loc.Get("mod_event_help")));
                Assert.Contains(elements.OfType<Button>(), b => Equals(b.Content, Loc.Get("mod_event_add")));
                File.WriteAllText(Path.Combine(root, "Bad Name.MP3"), "audio"); w.ImportEventAudioFrom(root);
                Assert.Contains(w.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("mod_event_flag"));
                Assert.Contains(w.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.GetF("mod_event_flagged", 1, 1));
                Assert.Contains(w.GetLogicalDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == Loc.Get("mod_event_preview"));
                Assert.Contains(w.GetLogicalDescendants().OfType<Button>(), b => AutomationProperties.GetName(b) == Loc.Get("mod_event_remove"));
                Assert.Contains(w.GetLogicalDescendants().OfType<Button>(), b => Equals(b.Content, Loc.Get("mod_event_attach")));
            }
            finally { w.Close(); Dispatcher.UIThread.RunJobs(); }
        }
        finally { loc.SetLanguage(previous); Directory.Delete(root, true); }
    });

    [Fact]
    public void AttachPickerOffersEveryMp3CaseAndAttachesMixedCaseFile() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var root = Path.Combine(Path.GetTempPath(), "ccp-event-pick-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "clip.Mp3"); File.WriteAllText(path, "audio");
        var file = DispatchProxy.Create<IStorageFile, NewEnhancementSourceTests.ItemProxy>();
        ((NewEnhancementSourceTests.ItemProxy)(object)file).PathValue = new Uri(path);
        var provider = DispatchProxy.Create<IStorageProvider, NewEnhancementSourceTests.PickerProxy>();
        var picker = (NewEnhancementSourceTests.PickerProxy)(object)provider; picker.Result = file;
        using var scope = NewEnhancementSourceTests.BindStorageProvider(new NewEnhancementSourceTests.Factory(provider));
        var w = new ModCreatorWindow(); w.Show();
        try
        {
            w.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.Get("mod_event_add")))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var panel = (StackPanel)typeof(ModCreatorWindow).GetField("_eventAudioRowsPanel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
            panel.GetLogicalDescendants().OfType<TextBox>().Single().Text = "Chosen phrase";
            panel.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Content, Loc.Get("mod_event_attach")))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var patterns = Assert.IsType<FilePickerOpenOptions>(picker.Options).FileTypeFilter!.Single().Patterns!;
            foreach (var name in new[] { "x.mp3", "x.mP3", "x.Mp3", "x.MP3" })
                Assert.Contains(patterns, pattern => FileSystemName.MatchesSimpleExpression(pattern, name, ignoreCase: false));
            Assert.DoesNotContain(patterns, pattern => FileSystemName.MatchesSimpleExpression(pattern, "x.wav", ignoreCase: false));
            var output = Path.Combine(root, "out"); w.WriteEventAudioTo(output);
            Assert.Equal("audio", File.ReadAllText(Path.Combine(output, "sounds", "event_audio", "chosen phrase.mp3")));
        }
        finally { w.Close(); Dispatcher.UIThread.RunJobs(); Directory.Delete(root, true); }
    });

}
