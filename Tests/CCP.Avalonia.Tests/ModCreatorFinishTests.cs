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

namespace CCP.Avalonia.Tests;

public sealed class ModCreatorFinishTests
{
    private static object? Call(ModCreatorWindow w, string method, params object?[] args) =>
        typeof(ModCreatorWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(w, args);
    private static void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Run(Action<ModCreatorWindow, string> test) => AvaloniaTestDispatcher.Run(() =>
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
        try { test(w, dir); }
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
}
