using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Windows;
using IconKind = FluentIcons.Common.Symbol;
using ConditioningControlPanel.Avalonia.Tours;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Xunit;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;

namespace CCP.Avalonia.Tests;

public sealed class ModCreatorFinishTests
{
    private static object? Call(ModCreatorWindow w, string method, params object?[] args) =>
        typeof(ModCreatorWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Run(Action<ModCreatorWindow, string> test) =>
        RunAsync((w, dir) => { test(w, dir); return Task.CompletedTask; }).GetAwaiter().GetResult();
    private static Task RunAsync(Func<ModCreatorWindow, string, Task> test) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily("fonts:Inter#Inter"),
                FontStyle.Normal, FontWeight.Bold), out _))
            FontManager.Current.AddFontCollection(new global::Avalonia.Fonts.Inter.InterFontCollection());
        var dir = Path.Combine(Path.GetTempPath(), "ccp-creator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var w = new ModCreatorWindow();
        try { await test(w, dir); }
        finally { w.Close(); Dispatcher.UIThread.RunJobs(); Directory.Delete(dir, true); }
    });
    private static StackPanel Section(ModCreatorWindow w, string key, string heading)
    {
        Click(w.GetLogicalDescendants().OfType<Button>().Single(b => Equals(b.Tag, key)));
        return (StackPanel)w.GetLogicalDescendants().OfType<TextBlock>().First(t => t.Text == heading).Parent!;
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
                Call(w, "SetAudioSlot", "sounds/giggle1.wav", path);
                var rows = (Dictionary<string, Grid>)typeof(ModCreatorWindow)
                    .GetField("_audioRows", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
                var a = rows["sounds/giggle1.wav"].Children.OfType<Button>().Single(x => Grid.GetColumn(x) == 2);
                var b = new Button();
                Assert.True(a.IsVisible);
                Click(a);
                Assert.Equal(IconKind.Stop, Assert.IsType<IconGlyph>(a.Content).Kind);
                var stale = finish;
                Call(w, "ToggleAudioPreview", path, b); Assert.Equal(1, stops);
                stale!(); Dispatcher.UIThread.RunJobs(); Assert.Equal(1, stops);
                Call(w, "ToggleAudioPreview", path, b); Assert.Equal(2, stops);
                Assert.Equal(IconKind.Play, Assert.IsType<IconGlyph>(b.Content).Kind);
                Call(w, "ToggleAudioPreview", path, a);
                finish!(); Dispatcher.UIThread.RunJobs(); Assert.Equal(3, stops);
                Call(w, "ToggleAudioPreview", path, a);
                PanicSurfaces.All.Single(s => s.Id == "mod-creator-preview").Stop(null);
                Assert.Equal(4, stops);
                Call(w, "ToggleAudioPreview", path, a);
                w.Show();
                Call(w, "BtnReset_Click");
                Click(w.OwnedWindows.OfType<MessageDialog>().Single().FindControl<Button>("BtnOk")!);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(5, stops);
                Call(w, "ToggleAudioPreview", path, a);
                w.Close(); Assert.Equal(6, stops);
            }
            finally { CoreAudio.PlayStoppableProvider = old; }
        });
    }

    [Fact]
    public void UiArtSlotsLoadAndClearThroughExistingImagePath()
    {
        Run((w, dir) =>
        {
            var section = Section(w, "uiart", "UI Art");
            Assert.Equal(34, section.Children.OfType<WrapPanel>().Sum(p => p.Children.Count));
            Assert.True(((Control)section.Parent!).IsVisible);
            var image = Path.Combine(dir, "nav", "door_home.png");
            Directory.CreateDirectory(Path.GetDirectoryName(image)!);
            using (var bitmap = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96)))
                bitmap.Save(image);
            Call(w, "LoadResourcesFrom", dir);
            var slots = (Dictionary<string, string?>)typeof(ModCreatorWindow)
                .GetField("_imageSlots", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
            Assert.Equal(image, slots["nav/door_home.png"]);
            Call(w, "ClearImageSlot", "nav/door_home.png");
            Assert.Null(slots["nav/door_home.png"]);
        });
    }

    [Fact]
    public void TutorialStepsNavigateToTheirCreatorSection()
    {
        Run((w, _) =>
        {
            TutorialHead.Seed();
            TutorialHead.Service.Start(TutorialType.Modding);
            try
            {
                Assert.Contains(TutorialHead.Service.CurrentSteps, s => s.RequiresTab?.StartsWith("mod:") == true);
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
    [Fact]
    public Task ImagePicksRejectMismatchAndConfirmOversize() => RunAsync(async (w, dir) =>
    {
        w.Show();
        const string key = "nav/door_home.png";
        var image = Path.Combine(dir, "home.png");
        using (var bitmap = new WriteableBitmap(new PixelSize(6, 3), new Vector(96, 96))) bitmap.Save(image);
        await (Task)Call(w, "SetImageSlot", key, image, false)!;
        var slots = (Dictionary<string, string?>)typeof(ModCreatorWindow)
            .GetField("_imageSlots", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(w)!;
        var wrong = Path.ChangeExtension(image, ".jpg"); File.Copy(image, wrong);
        var pick = (Task)Call(w, "SetImageSlot", key, wrong, true)!;
        var dialog = w.OwnedWindows.OfType<MessageDialog>().Single();
        Assert.Contains(".png", dialog.FindControl<TextBlock>("TxtMessage")!.Text);
        Click(dialog.FindControl<Button>("BtnOk")!); await pick;
        Assert.Equal(image, slots[key]);
        var large = Path.Combine(dir, "large.png"); File.Copy(image, large);
        using (var file = File.OpenWrite(large)) file.SetLength(ModImageSlotRules.WarnFileSizeBytes + 1);
        foreach (var accept in new[] { false, true })
        {
            pick = (Task)Call(w, "SetImageSlot", key, large, true)!;
            dialog = w.OwnedWindows.OfType<MessageDialog>().Single();
            Click(dialog.FindControl<Button>(accept ? "BtnOk" : "BtnCancel")!); await pick;
            Assert.Equal(accept ? large : image, slots[key]);
        }
    });
    [Fact]
    public void UiArtUsesLocalizedCopy() => AvaloniaTestDispatcher.Run(() =>
    {
        var loc = LocalizationManager.Instance; var previous = loc.CurrentLanguage;
        try
        {
            loc.SetLanguage("fr");
            Run((w, _) =>
            {
                var section = Section(w, "uiart", Loc.Get("mod_art_title"));
                Assert.Contains(section.Children.OfType<TextBlock>(), t => t.Text == Loc.Get("mod_art_intro"));
                Assert.Contains(section.GetLogicalDescendants().OfType<TextBox>(), b => b.Text == Loc.Get("mod_art_slot_nav_door_home"));
                Assert.Equal(Loc.GetF("mod_art_packed", "nav/door_home.png"), ToolTip.GetTip(section.Children.OfType<WrapPanel>().First().Children[0]));
            });
        }
        finally { loc.SetLanguage(previous); }
    });

}
