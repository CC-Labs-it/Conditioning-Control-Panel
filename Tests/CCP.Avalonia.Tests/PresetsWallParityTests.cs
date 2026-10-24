using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PresetsWallParityTests
{
    private static Task WithShell(Func<MainShellWindow, SettingsService, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var sessionProvider = CoreSession.IsSessionRunningProvider;
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreSession.IsSessionRunningProvider = () => false;
        var shell = new MainShellWindow();
        shell.Show();
        try { await body(shell, settings); }
        finally
        {
            shell.Close();
            CoreSession.IsSessionRunningProvider = sessionProvider;
            CoreSettings.ServiceProvider = previous;
        }
    });

    [Fact]
    public Task WallGestureHintCountsSuccessfulTogglesAndRetiresAtThree() => WithShell(async (shell, settings) =>
    {
        var s = settings.Current;
        s.DashboardToggleHintUses = 0;
        var dash = shell.SettingsPage!;
        var hint = dash.FindControl<TextBlock>("DashToggleHint");
        Assert.NotNull(hint);
        shell.RefreshFavoritesRail();
        Assert.True(hint!.IsVisible);
        Assert.Equal(Loc.Get("dash_toggle_hint"), hint.Text);
        void Toggle(FeatureCard card) => card.RaiseEvent(new RoutedEventArgs(FeatureCard.ToggleRequestedEvent, card));

        Toggle(dash.CardFlash);
        Assert.Equal(1, s.DashboardToggleHintUses);
        Assert.True(hint.IsVisible);
        bool flash = s.FlashEnabled;
        CoreSession.IsSessionRunningProvider = () => true;
        Toggle(dash.CardFlash); // refused by the session: no lesson was learned
        Assert.Equal(flash, s.FlashEnabled);
        Assert.Equal(1, s.DashboardToggleHintUses);
        CoreSession.IsSessionRunningProvider = () => false;
        Toggle(dash.CardSubliminal);
        Assert.Equal(2, s.DashboardToggleHintUses);
        Assert.True(hint.IsVisible);
        Toggle(dash.CardFlash);
        Assert.Equal(3, s.DashboardToggleHintUses);
        Assert.False(hint.IsVisible);
        for (int i = 0; i < 5; i++) Toggle(dash.CardFlash);
        Assert.Equal(3, s.DashboardToggleHintUses);
        settings.SaveImmediate();
        Assert.Contains("\"DashboardToggleHintUses\": 3", File.ReadAllText(Path.Combine(CorePaths.UserData, "settings.json")));
        await Task.CompletedTask;
    });

    [Fact]
    public Task SpiralWallToggleStartsAndStopsTheRunningOverlayAndPanicStillClosesIt() => WithShell(async (shell, settings) =>
    {
        const string tinyGif = "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";
        var dir = Directory.CreateTempSubdirectory("ccp-wall-spiral-").FullName;
        var path = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(path, Convert.FromBase64String(tinyGif));
        var provider = CoreSession.IsEngineRunningProvider;
        var stopped = CoreEngine.StoppedHook;
        bool skip = SpiralOverlay.SkipPlatformChecksForTests;
        var s = settings.Current;
        s.FlashEnabled = s.SubliminalEnabled = s.BubblesEnabled = s.LockCardEnabled = s.BouncingTextEnabled = false;
        s.MandatoryVideosEnabled = s.BubbleCountEnabled = s.PopQuizEnabled = s.MindWipeEnabled = false;
        s.SpiralEnabled = false;
        s.SpiralPath = path;
        s.SpiralTargetMonitor = -1;
        CoreSession.IsEngineRunningProvider = () => CoreEngine.IsRunning;
        CoreEngine.StoppedHook = shell.OnEngineStopped;
        SpiralOverlay.SkipPlatformChecksForTests = true;
        // The user path: right-click on the Spiral half of the combo -> ToggleA -> ToggleWallFeature.
        var combo = shell.SettingsPage!.ComboSpiralPink;
        void ToggleSpiral() => combo.RaiseEvent(new RoutedEventArgs(SplitFeatureCard.ToggleAEvent, combo));
        try
        {
            CoreEngine.Start();
            Assert.False(SpiralOverlay.IsShowing);
            ToggleSpiral();
            if (SpiralOverlay.Decoding is { } decode) await decode;
            Dispatcher.UIThread.RunJobs();
            Assert.True(SpiralOverlay.IsShowing);
            Assert.True(SpiralOverlay.IsAnimating);
            ToggleSpiral();
            Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsAnimating);
            ToggleSpiral();
            Assert.True(SpiralOverlay.IsShowing);
            PanicSurfaces.StopAll("wall-toggle-test", shell);
            Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsAnimating);
            Assert.True(s.SpiralEnabled); // safety closes the effect without changing the saved choice
        }
        finally
        {
            CoreEngine.Stop();
            SpiralOverlay.CloseAll();
            SpiralOverlay.SkipPlatformChecksForTests = skip;
            CoreEngine.StoppedHook = stopped;
            CoreSession.IsEngineRunningProvider = provider;
            Directory.Delete(dir, true);
        }
    });
}
