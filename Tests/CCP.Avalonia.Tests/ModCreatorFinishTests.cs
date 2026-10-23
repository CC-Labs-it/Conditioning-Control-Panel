using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

public sealed class ModCreatorFinishTests
{
    private static void Run(Action<ModCreatorWindow, string> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Path.Combine(Path.GetTempPath(), "ccp-mod-finish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var w = new ModCreatorWindow();
        try { body(w, dir); }
        finally { w.Close(); Dispatcher.UIThread.RunJobs(); Directory.Delete(dir, true); }
    });
    private static object? Call(ModCreatorWindow w, string method, params object[] args) =>
        typeof(ModCreatorWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);
    private static IEnumerable<T> Controls<T>(Control c) where T : Control => c.GetLogicalDescendants().OfType<T>();
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static StackPanel Section(ModCreatorWindow w, string key, string heading)
    {
        Click(Controls<Button>(w).Single(b => Equals(b.Tag, key)));
        var stack = (StackPanel)Controls<TextBlock>(w).First(t => t.Text == heading).Parent!;
        Assert.True(((Control)stack.Parent!).IsVisible);
        return stack;
    }
    private static string Clip(string dir, string name, string bytes = "clip")
    {
        var file = Path.Combine(dir, name); File.WriteAllText(file, bytes); return file;
    }

    [Fact]
    public void UiArtOffersEveryWpfSlotInGroupOrderAndAtItsGroupSize() => Run((w, _) =>
    {
        var section = Section(w, "uiart", "UI Art");
        var groups = section.Children.OfType<WrapPanel>().ToArray();
        Assert.Equal(new[] { 8, 7, 11, 8 }, groups.Select(g => g.Children.Count));
        Assert.Equal(new[] { 100d, 150d, 150d, 110d }, groups.Select(g => Controls<Border>(g).First().Width));
        Assert.Equal(new[] { 100d, 84d, 84d, 110d }, groups.Select(g => Controls<Border>(g).First().Height));
        var keys = groups.SelectMany(g => g.Children).Select(c => ToolTip.GetTip(c)?.ToString()).ToArray();
        Assert.StartsWith("Packed as resources/nav/door_home.png ", keys[0]);
        Assert.Contains(keys, k => k?.StartsWith("Packed as resources/features/fyp.png ") == true);
        Assert.StartsWith("Packed as resources/intake/pass_card_drone.png ", keys[^1]);
    });

    [Fact]
    public void LoadedFramingSurvivesExportAndIsRemovedWhenItsImageIsClearedOrReplaced() => Run((w, dir) =>
    {
        const string key = "features/fyp.png";
        var image = Path.Combine(dir, "art.png");
        File.WriteAllBytes(image, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII="));
        var m = new ModManifest { ArtFraming = new() { ["Features/FYP.png"] = new()
            { [ModArtFramingRegistry.SurfaceRailChip] = new ModArtFraming { CenterX = .123456, Zoom = 2 } } } };
        w.PopulateFromManifest(m);
        Call(w, "SetImageSlot", key, image, false);
        var built = w.BuildManifestFromForm();
        Assert.Equal(.1235, built.ArtFraming![key][ModArtFramingRegistry.SurfaceRailChip].CenterX);
        Call(w, "ClearImageSlot", key);
        Assert.Null(w.BuildManifestFromForm().ArtFraming);
        w.PopulateFromManifest(m);
        Call(w, "SetImageSlot", key, image, true);
        Assert.Null(w.BuildManifestFromForm().ArtFraming);
        w.PopulateFromManifest(m);
        Call(w, "ClearPanelSections");
        Assert.Null(w.BuildManifestFromForm().ArtFraming);
    });

    [Fact]
    public void ImagePickingRejectsWrongFormatsAndHonorsLargeImageCancelAndAccept() => Run((w, dir) =>
    {
        w.Show();
        const string key = "features/fyp.png";
        var image = Path.Combine(dir, "large.png");
        File.WriteAllBytes(image, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII="));
        using (var file = File.OpenWrite(image)) file.SetLength(4L * 1024 * 1024 + 1);
        var slots = (Dictionary<string, string?>)typeof(ModCreatorWindow)
            .GetField("_imageSlots", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
        var pending = (Task)Call(w, "SetPickedImageSlot", key, Clip(dir, "wrong.jpg"))!;
        var dialog = Assert.Single(w.OwnedWindows.OfType<MessageDialog>());
        Assert.Equal("Wrong file type", dialog.Title);
        Click(dialog.FindControl<Button>("BtnOk")!); Dispatcher.UIThread.RunJobs();
        Assert.True(pending.IsCompletedSuccessfully); Assert.Null(slots[key]);
        pending = (Task)Call(w, "SetPickedImageSlot", key, image)!;
        dialog = Assert.Single(w.OwnedWindows.OfType<MessageDialog>());
        Assert.Equal("Large image", dialog.Title);
        Click(dialog.FindControl<Button>("BtnCancel")!); Dispatcher.UIThread.RunJobs();
        Assert.True(pending.IsCompletedSuccessfully); Assert.Null(slots[key]);
        pending = (Task)Call(w, "SetPickedImageSlot", key, image)!;
        dialog = Assert.Single(w.OwnedWindows.OfType<MessageDialog>());
        Click(dialog.FindControl<Button>("BtnOk")!); Dispatcher.UIThread.RunJobs();
        Assert.True(pending.IsCompletedSuccessfully); Assert.Equal(image, slots[key]);
    });

    [Fact]
    public void EventImportDeduplicatesFilesAndExportKeepsFirstSlugWithWarning() => Run((w, dir) =>
    {
        Clip(dir, "a.mp3", "first"); Clip(dir, "b.mp3", "second"); Clip(dir, "Not Clean!.mp3");
        w.ImportEventAudioFrom(dir); w.ImportEventAudioFrom(dir);
        var section = Section(w, "eventaudio", "Event Audio");
        var boxes = Controls<TextBox>(section).ToArray();
        Assert.Equal(new[] { "a", "b", "Not Clean!" }, boxes.Select(b => b.Text));
        Assert.Contains(Controls<TextBlock>(section), t => t.Text?.StartsWith("Filename isn't a clean slug") == true);
        boxes[0].Text = "Hello, world!"; boxes[1].Text = "HELLO world"; boxes[2].Text = "!!!";
        Click(Controls<Button>(section).Single(b => Equals(b.Content, "+ Add line")));
        var output = Path.Combine(dir, "out"); w.WriteEventAudioTo(output);
        Assert.Equal("first", File.ReadAllText(Path.Combine(output, "sounds", "event_audio", "hello world.mp3")));
        Assert.Single(Directory.GetFiles(Path.Combine(output, "sounds", "event_audio")));
        Assert.Contains(Controls<TextBlock>(section), t => t.Text?.Contains("Skipped 1 duplicate slug(s): hello world") == true);
    });

    [Fact]
    public void EventLoadUsesReadOnlyStemsAndRemoveAndResetExcludeClips() => Run((w, dir) =>
    {
        var input = Path.Combine(dir, "in", "sounds", "event_audio"); Directory.CreateDirectory(input);
        Clip(input, "exact slug.mp3");
        w.LoadEventAudioFrom(Path.Combine(dir, "in"));
        var section = Section(w, "eventaudio", "Event Audio");
        Assert.Equal("exact slug", Controls<TextBox>(section).Single().Text);
        Assert.True(Controls<TextBox>(section).Single().IsReadOnly);
        var output = Path.Combine(dir, "out"); w.WriteEventAudioTo(output);
        Assert.Equal("clip", File.ReadAllText(Path.Combine(output, "sounds", "event_audio", "exact slug.mp3")));
        Click(Controls<Button>(section).Single(b => AutomationProperties.GetName(b) == "Remove line"));
        w.WriteEventAudioTo(Path.Combine(dir, "removed"));
        Assert.False(Directory.Exists(Path.Combine(dir, "removed")));
        w.LoadEventAudioFrom(Path.Combine(dir, "in")); Call(w, "ClearSideFileState");
        Assert.Empty(Controls<TextBox>(section));
        w.WriteEventAudioTo(Path.Combine(dir, "reset")); Assert.False(Directory.Exists(Path.Combine(dir, "reset")));
        w.ImportEventAudioFrom(Path.Combine(dir, "missing"));
        Assert.Contains(Controls<TextBlock>(section), t => t.Text == "Could not read that folder.");
    });

    [Fact]
    public void PreviewTogglesSwitchesIgnoresLateCompletionAndStopsForPanicAndClose() => Run((w, dir) =>
    {
        var previous = CoreAudio.PlayStoppableProvider;
        var finished = new List<Action>(); int stops = 0;
        CoreAudio.PlayStoppableProvider = (_, volume, tag, _, end) =>
        { Assert.Equal(1f, volume); Assert.Equal("modcreator-preview", tag); finished.Add(end!); return () => stops++; };
        try
        {
            Call(w, "AddVoiceLineRow", Clip(dir, "one.mp3")); Call(w, "AddVoiceLineRow", Clip(dir, "two.mp3"));
            var buttons = Controls<Button>(w).Where(b => AutomationProperties.GetName(b) == "Play preview").ToArray();
            var a = buttons[^2]; var b = buttons[^1];
            Click(a); Assert.Equal(IconKind.Stop, ((IconGlyph)a.Content!).Kind);
            Click(a); Assert.Equal(1, stops);
            Click(a); Click(b); Assert.Equal(2, stops);
            finished[1](); Dispatcher.UIThread.RunJobs();
            Assert.Equal(IconKind.Stop, ((IconGlyph)b.Content!).Kind);
            finished[^1](); Dispatcher.UIThread.RunJobs();
            Assert.Equal(IconKind.Play, ((IconGlyph)b.Content!).Kind);
            Click(a); PanicSurfaces.StopAll("test"); Assert.Equal(3, stops);
            Click(b); w.Close(); Assert.Equal(4, stops);
        }
        finally { CoreAudio.PlayStoppableProvider = previous; }
    });

    [Fact]
    public void UnseededPreviewFinishesSynchronouslyAndResetStopsAnActiveClip() => Run((w, dir) =>
    {
        var previous = CoreAudio.PlayStoppableProvider; var previousOneShot = CoreAudio.PlayOneShotProvider;
        CoreAudio.PlayStoppableProvider = null; CoreAudio.PlayOneShotProvider = null;
        try
        {
            var b = new Button(); var clip = Clip(dir, "preview.mp3");
            Call(w, "ToggleAudioPreview", clip, b);
            Assert.Equal(IconKind.Play, ((IconGlyph)b.Content!).Kind);
            var stops = 0; CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => () => stops++;
            Call(w, "ToggleAudioPreview", clip, b); Call(w, "StopAudioPreview"); Assert.Equal(1, stops);
            Call(w, "ToggleAudioPreview", Path.Combine(dir, "missing.mp3"), b); Assert.Equal(1, stops);
        }
        finally { CoreAudio.PlayStoppableProvider = previous; CoreAudio.PlayOneShotProvider = previousOneShot; }
    });

    [Fact]
    public void ModdingTutorialNavigatesEveryRequestedSectionIncludingBackwards() => Run((w, _) =>
    {
        TutorialHead.Seed();
        try
        {
            Call(w, "LaunchTutorial");
            var service = TutorialHead.Service;
            while (service.IsActive)
            {
                if (service.CurrentStep?.RequiresTab is { } tab && tab.StartsWith("mod:"))
                {
                    var panels = (Dictionary<string, Border>)typeof(ModCreatorWindow)
                        .GetField("_sectionPanels", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w)!;
                    Assert.True(panels[tab[4..]].IsVisible);
                }
                if (service.CurrentStepIndex == 3) { service.Previous(); service.Next(); }
                service.Next();
            }
        }
        finally { TutorialHead.ResetForTests(); }
    });
}
