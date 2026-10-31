using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Avalonia.Helpers;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// WPF ImgLogo_MouseLeftButtonDown (MainWindow.UiUpdates.cs:1189) through real presses on the
/// Dashboard wordmark: the achievement track (neon_obsession at 20 rapid clicks), the click pulse
/// under motion, and the 100th click asking the server for the reader count before the note opens.
/// </summary>
public sealed class DashboardLogoClickTests
{
    private static readonly System.Reflection.PropertyInfo AchievementsProp = typeof(AvApp).GetProperty("Achievements",
        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)!;

    private const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    private static readonly Type Egg = typeof(SettingsTabView);

    [Fact]
    public Task DeeperHomeDoorNavigatesWithoutOpeningAnEditor() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        AvApp.StartMods();
        var dir = Directory.CreateTempSubdirectory("ccp-deeper-art-");
        var png = Path.Combine(dir.FullName, "mod.png");
        using (var bmp = new WriteableBitmap(new PixelSize(2, 1), new Vector(96, 96))) bmp.Save(png);
        var previousArt = CoreModArt.OverridePathProvider;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); Dispatcher.UIThread.RunJobs();
            var card = shell.SettingsPage!.CardDeeperEditor;
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("dash_deeper_editor_title"), card.Title);
            Assert.False(card.DimWhenInactive);
            Assert.Null(card.TierBadge);
            var bundled = Assert.IsType<Bitmap>(card.Icon).PixelSize;
            void Activate() { CoreMods.RaiseModChanged(null, new ModPackage(new ModManifest(), null, false)); Dispatcher.UIThread.RunJobs(); }
            CoreModArt.OverridePathProvider = p => p == "features/deeper_editor.png" ? png : null;
            Activate();
            Assert.Equal(new PixelSize(768, 384), Assert.IsType<Bitmap>(card.Icon).PixelSize);
            CoreModArt.OverridePathProvider = _ => null;
            Activate();
            Assert.Equal(bundled, Assert.IsType<Bitmap>(card.Icon).PixelSize);
            var windows = Application.Current!.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime life
                ? life.Windows.Count : 0;
            card.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(
                ConditioningControlPanel.Avalonia.Views.Features.FeatureCard.ClickEvent));
            Assert.Equal("deeper", shell.CurrentTab);
            if (Application.Current.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime after)
                Assert.Equal(windows, after.Windows.Count);
        }
        finally { shell.Close(); CoreModArt.OverridePathProvider = previousArt; dir.Delete(true); Dispatcher.UIThread.RunJobs(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task WordmarkClicksTrackPulseAndReachTheEasterEgg() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var clockWas = BeatLoop.Time; var clock = new SteppedTime();
        var s = CoreSettings.Current;
        var (motionWas, achWas, readersWas) = (s.MotionLevel, AchievementsProp.GetValue(null), SettingsTabView.EasterEggReaders);
        var dir = Directory.CreateTempSubdirectory("ccp-logo-click-").FullName;
        int readerAsks = 0;
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
        AchievementsProp.SetValue(null, engine);
        SettingsTabView.EasterEggReaders = () => { readerAsks++; return new TaskCompletionSource<int>().Task; };   // the note never opens
        var shell = new MainShellWindow();
        shell.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            var dash = shell.SettingsPage!;
            var frame = dash.FindControl<Border>("LogoBrandFrame")!;
            var logo = dash.FindControl<AnimatedLogoImage>("ImgLogo")!;
            void Click()
            {
                var pt = frame.TranslatePoint(new Point(frame.Bounds.Width / 2, frame.Bounds.Height / 2), shell)!.Value;
                shell.MouseDown(pt, MouseButton.Left); shell.MouseUp(pt, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            }

            s.MotionLevel = MotionLevel.Off;
            Click();
            Assert.Equal(1, engine.Progress.AvatarClickCount);
            Assert.Null(logo.RenderTransform);   // no pulse with motion off, as WPF's AllowTransitions gate
            s.MotionLevel = MotionLevel.Full; BeatLoop.Time = clock;
            void Step(int ms) { clock.Advance(TimeSpan.FromMilliseconds(ms)); BeatLoop.StepAll(); }
            double Scale() => Assert.IsType<ScaleTransform>(logo.RenderTransform).ScaleX;
            Click(); Step(60); Assert.Equal(1.0375, Scale(), 4);
            Click(); Assert.Equal(1, Scale()); // restart, replacing the first pulse
            Step(20); Assert.Equal(1.0125, Scale(), 4); // first click's old reset time
            Step(60); Assert.Equal(1.05, Scale(), 4);
            Step(20); Assert.Equal(1.0375, Scale(), 4); // first pulse would now finish
            Step(60); Assert.Equal(1, Scale());
            // Clicks through the shell take seconds each on a loaded runner, so the windows are primed rather
            // than clicked out (thresholds: AchievementEngineTests.AvatarClicksUnlockAtWpfThresholds).
            (engine.Progress.AvatarClickStartTime, engine.Progress.AvatarClickCount) = (DateTime.Now, 19);
            Click();
            Assert.True(engine.Progress.IsUnlocked("neon_obsession"));   // the 20th rapid click
            Egg.GetField("_easterEggFirstClick", Flags)!.SetValue(dash, DateTime.Now);
            Egg.GetField("_easterEggClickCount", Flags)!.SetValue(dash, 98);
            Assert.Equal(0, readerAsks);
            Click();   // the 99th: still nothing
            Assert.Equal(0, readerAsks);
            Click();   // the 100th inside 60 s
            Assert.Equal(1, readerAsks);
            Click();
            Assert.Equal(1, readerAsks);   // once per run, as WPF's _easterEggTriggered
        }
        finally
        {
            shell.Close(); Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = clockWas; s.MotionLevel = motionWas;
            AchievementsProp.SetValue(null, achWas);
            SettingsTabView.EasterEggReaders = readersWas;
            try { Directory.Delete(dir, true); } catch { }
        }
        return Task.CompletedTask;
    });
}
