using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PerimeterCometHostTests
{
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }

    private static void Settle(Window w)
    {
        Dispatcher.UIThread.RunJobs();
        w.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static PerimeterCometAdorner? Comet(Control c) => AdornerLayer.GetAdornerLayer(c)?.Children
        .OfType<PerimeterCometAdorner>().SingleOrDefault(a => AdornerLayer.GetAdornedElement(a) == c);

    private static void Run(Action<Clock> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
        {
            var options = global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options();
            // These hosts exercise geometry and lifecycle, not Windows font metrics.
            var mappings = new Dictionary<string, FontFamily>(options.FontFamilyMappings!);
            mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Inter);
            options.FontFamilyMappings = mappings;
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().With(options)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        }
        var oldSettings = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.Welcomed = true;
        s.HasAcceptedAgeVerification = true;
        s.WhatMovedCardShown = 1;
        s.MotionLevel = MotionLevel.Full;
        s.PerformanceMode = false;
        s.MasterVolume = 0;
        s.AudioOnlySession = true;
        var oldClock = FxAdorner.Time;
        var clock = new Clock();
        FxAdorner.Time = clock;
        try { body(clock); }
        finally
        {
            CoreEngine.Stop();
            FxAdorner.Time = oldClock;
            CoreSettings.ServiceProvider = oldSettings;
        }
    });

    [Fact]
    public void ChasterLinkWaitLapsHeroAndStopsOnCancelHideOrDetach() => Run(clock =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-comet-").FullName;
        var chaster = new ChasterService(new ChasterClient(new Refuse()), new SecretChasterTokenStore(),
            Path.Combine(dir, "chaster_tab.json"), () => new ChasterOptions(false, "", new HashSet<string>()));
        var oldShell = ExternalOpener.Shell;
        ExternalOpener.Shell = _ => true; // the consent page never reaches the desktop
        ChasterHead.Service = chaster;
        var tab = new ChasterTabView();
        var hero = tab.FindControl<StackPanel>("UnlinkedPanel")!.GetVisualAncestors().OfType<Border>().First();
        var showLinking = typeof(ChasterTabView).GetMethod("ShowLinking", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var w = new Window { Width = 1200, Height = 800, Content = tab };
        try
        {
            tab.Refresh();
            Click(tab.FindControl<Button>("BtnLink")!); // the user path: the Link button starts the wait
            Assert.True(chaster.IsLinking);
            Assert.Null(Comet(hero)); // Linking already started before the page acquired a layer.
            w.Show(); Settle(w);
            var first = Assert.IsType<PerimeterCometAdorner>(Comet(hero));
            Assert.True(first.IsLapping);
            clock.Now += TimeSpan.FromSeconds(3).Ticks;
            first.Tick();
            Assert.Equal(0.5, first.Phase, 3); // WPF linking lap: six seconds.
            Click(tab.FindControl<StackPanel>("LinkingRow")!.Children.OfType<Button>().Single()); // Cancel
            PumpUntil(() => !tab.FindControl<Control>("LinkingRow")!.IsVisible);
            Assert.Null(Comet(hero));
            Assert.False(first.IsTicking);
            showLinking.Invoke(tab, new object[] { true });
            var hidden = Comet(hero)!;
            tab.IsVisible = false;
            Assert.Null(Comet(hero));
            Assert.False(hidden.IsTicking);
            tab.IsVisible = true; Settle(w);
            showLinking.Invoke(tab, new object[] { true });
            var removed = Comet(hero)!;
            w.Content = null;
            Assert.False(removed.IsTicking);
            Assert.Null(removed.Parent);
            w.Content = tab; Settle(w);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            showLinking.Invoke(tab, new object[] { true });
            Assert.Null(Comet(hero)); // WPF host has no wait comet without ambient motion.
        }
        finally
        {
            w.Close();
            chaster.CancelLink();
            ChasterHead.Service = null;
            chaster.Dispose();
            ExternalOpener.Shell = oldShell;
            try { Directory.Delete(dir, true); } catch { }
        }
    });

    private sealed class Refuse : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private static void Click(Button b)
    {
        Assert.True(b.IsEffectivelyEnabled);
        b.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    private static void PumpUntil(Func<bool> done)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew(); // P73: bounded by time
        while (!done() && sw.Elapsed < TimeSpan.FromSeconds(10)) { Dispatcher.UIThread.RunJobs(); Thread.Yield(); }
        Assert.True(done(), "timed out");
    }

    [Fact]
    public void LauncherPointerCometMovesAndParksOnLeaveRebuildAndDeactivation() => Run(clock =>
    {
        var w = new LauncherWindow();
        try
        {
            w.Show(); Settle(w);
            var tiles = w.FindControl<UniformGrid>("GamesGrid")!.Children.OfType<Border>().ToArray();
            Assert.True(tiles.Length >= 2);
            Point At(Border b) => b.TranslatePoint(new Point(b.Bounds.Width / 2, b.Bounds.Height / 2), w)!.Value;
            w.MouseMove(At(tiles[0])); Settle(w);
            var first = Assert.IsType<PerimeterCometAdorner>(Comet(tiles[0]));
            clock.Now += TimeSpan.FromSeconds(3.5).Ticks;
            first.Tick();
            Assert.Equal(0.5, first.Phase, 3); // WPF hovered-tile lap: seven seconds.
            w.MouseMove(At(tiles[1])); Settle(w);
            Assert.Null(Comet(tiles[0]));
            Assert.False(first.IsTicking);
            Assert.NotNull(Comet(tiles[1]));
            w.MouseMove(new Point(2, 2)); Settle(w);
            Assert.Null(Comet(tiles[1]));
            w.MouseMove(At(tiles[0])); Settle(w);
            var rebuilt = Comet(tiles[0])!;
            w.BuildTiles(); Settle(w);
            Assert.False(rebuilt.IsTicking);
            Assert.Null(rebuilt.Parent);
            var tile = w.FindControl<UniformGrid>("GamesGrid")!.Children.OfType<Border>().First();
            w.TileHover(tile, true);
            var parked = Comet(tile)!;
            w.OnFxActivated(false);
            Assert.Null(Comet(tile));
            Assert.False(parked.IsTicking);
            w.OnFxActivated(true);
            CoreSettings.Current.PerformanceMode = true;
            w.TileHover(tile, true);
            Assert.Null(Comet(tile));
            CoreSettings.Current.PerformanceMode = false;
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            w.TileHover(tile, true);
            Assert.Null(Comet(tile));
        }
        finally { w.Close(); }
    });

    [Fact]
    public void LauncherRunningPanelCometFollowsEngineVisibilityAndMotion() => Run(clock =>
    {
        var w = new LauncherWindow();
        var panel = w.FindControl<Border>("PanelCard")!;
        try
        {
            w.Show(); Settle(w);
            Assert.Null(Comet(panel));
            var rest = panel.BorderBrush;
            CoreEngine.Start();
            w.RefreshStatus();
            var running = Assert.IsType<PerimeterCometAdorner>(Comet(panel));
            clock.Now += TimeSpan.FromSeconds(6).Ticks;
            running.Tick();
            Assert.Equal(0.5, running.Phase, 3); // WPF panel lap: twelve seconds.
            // WPF Choreo.cs:403-416: the rim breathes 0.45 <-> 1 over 2.6 s each way while the comet runs.
            Assert.Equal(0.45, w.PanelRimOpacity!.Value, 3);
            w.StepFx(1.3);
            Assert.Equal(0.725, w.PanelRimOpacity!.Value, 3);
            w.StepFx(1.3);
            Assert.Equal(1.0, panel.BorderBrush!.Opacity, 3);   // the breathing copy is what the rim draws
            w.StepFx(2.6);
            Assert.Equal(0.45, w.PanelRimOpacity!.Value, 3);
            w.WindowState = WindowState.Minimized;
            Assert.False(running.IsTicking);
            Assert.Null(Comet(panel));
            w.WindowState = WindowState.Normal; Settle(w);
            Assert.NotNull(Comet(panel));
            w.OnFxActivated(false);
            Assert.Null(Comet(panel));
            w.OnFxActivated(true);
            Assert.NotNull(Comet(panel));
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            w.RefreshStatus();
            Assert.Null(Comet(panel));
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            w.RefreshStatus();
            var stopped = Comet(panel)!;
            CoreEngine.Stop();
            w.RefreshStatus();
            Assert.Null(Comet(panel));
            Assert.False(stopped.IsTicking);
            Assert.Null(w.PanelRimOpacity);
            Assert.Equal(1.0, panel.BorderBrush!.Opacity, 3);                // the theme's rim is back
            Assert.Equal(((ISolidColorBrush)rest!).Color, ((ISolidColorBrush)panel.BorderBrush).Color);
            CoreEngine.Start(); w.RefreshStatus();
            var hidden = Comet(panel)!;
            w.Hide();
            Assert.Null(Comet(panel));
            Assert.False(hidden.IsTicking);
        }
        finally { w.Close(); }
    });
}
