using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using ConditioningControlPanel.Services.Commands;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The spiral overlay (WPF OverlayService spiral region): the #722 opacity curve, the #572
/// frame budget, the decode, start/stop with the engine, panic, the card's slider, and the settings.</summary>
public sealed class SpiralOverlayTests(ITestOutputHelper output)
{
    // 4x4, three frames (red, green, blue), 30 ms each, looping - made with PIL.
    private const string TinyGif =
        "R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7";

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.3, 0.03)]     // bottom half: the old flat tenth, unchanged
    [InlineData(0.5, 0.05)]
    [InlineData(0.75, 0.325)]   // top half climbs at 1.1
    [InlineData(1.0, 0.6)]      // 100% paints 0.6, not 0.1
    [InlineData(2.0, 0.6)]
    [InlineData(double.NaN, 0.0)]
    public void Opacity_curve_matches_wpf_722(double slider, double painted)
        => Assert.Equal(painted, SpiralFrames.Paint(slider), 10);

    [Fact]
    public void Frame_plan_caps_size_frames_and_scales_the_delay()
    {
        Assert.Equal((1280, 853, 32, 1, 30), SpiralFrames.Plan(2400, 1600, 32, 30));     // the shipped spiral
        Assert.Equal((1280, 853, 72, 2, 100), SpiralFrames.Plan(2400, 1600, 120, 10));   // 300 MB budget -> stride 2; 10 ms -> 50
        Assert.Equal((4, 4, 3, 1, 30), SpiralFrames.Plan(4, 4, 3, 30));
    }

    [Fact]
    public async Task Engine_start_shows_and_animates_panic_and_stop_clear_it_and_the_slider_repaints()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-spiral-").FullName;
        var gif = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.SpiralTargetMonitor, s.PanicKeyEnabled, s.PanicKey);
            var provider = CoreSession.IsEngineRunningProvider;
            var shell = new MainShellWindow();
            var card = new SpiralFeatureControl();
            var cardHost = new Window { Content = card };
            shell.Show();
            try
            {
                CoreSession.IsEngineRunningProvider = () => CoreEngine.IsRunning;
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                SpiralOverlay.SkipPlatformChecksForTests = true;
                await Settle();   // P02: an earlier test's decode still in flight would swallow this test's first one
                (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.SpiralTargetMonitor) = (true, gif, 40, -1);
                (s.PanicKeyEnabled, s.PanicKey) = (true, "F8");

                // Armed but the engine is off: nothing on screen, nothing ticking (WPF RefreshOverlays).
                SpiralOverlay.Refresh(shell);
                Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsAnimating);

                // Start: the decode lands off-thread, then the windows come up and the clock runs.
                shell.StartEngine();
                await Settle();
                Assert.True(SpiralOverlay.IsShowing, Why());
                Assert.True(SpiralOverlay.IsAnimating, Why());
                Assert.Equal(TimeSpan.FromMilliseconds(30), SpiralOverlay.FrameDelay);
                Assert.Equal(SpiralFrames.Paint(0.4), SpiralOverlay.Shown[0].Spiral.Opacity, 10);
                var first = SpiralOverlay.Shown[0].Spiral.Source;
                SpiralOverlay.Tick();
                Assert.Equal(1, SpiralOverlay.FrameIndex);
                Assert.NotSame(first, SpiralOverlay.Shown[0].Spiral.Source);

                // Frame cost: a tick swaps a cached bitmap, so it stays cheap and allocates little.
                var alloc = GC.GetAllocatedBytesForCurrentThread();
                var sw = Stopwatch.StartNew();
                for (var i = 0; i < 1000; i++) SpiralOverlay.Tick();
                sw.Stop();
                var perTick = (GC.GetAllocatedBytesForCurrentThread() - alloc) / 1000.0;
                output.WriteLine($"spiral tick: {sw.Elapsed.TotalMilliseconds / 1000 * 1000:F2} us, {perTick:F0} B");
                Assert.True(perTick < 4096, $"{perTick} B per tick");

                // The card's slider repaints the live overlay through the curve.
                cardHost.Show();
                card.FindControl<Slider>("SliderOpacity")!.Value = 80;
                Assert.Equal(80, s.SpiralOpacity);
                Assert.Equal(SpiralFrames.Paint(0.8), SpiralOverlay.Shown[0].Spiral.Opacity, 10);

                // Panic: windows and clock gone; the saved flag is untouched.
                shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
                Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsAnimating);
                Assert.True(s.SpiralEnabled);

                // Restart reuses the cached frames at once; Stop clears it again.
                shell.StartEngine();
                Assert.True(SpiralOverlay.IsShowing && SpiralOverlay.IsAnimating);
                MainShellWindow.StopEngine();
                Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsAnimating);
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                CoreEngine.Stop();
                SpiralOverlay.CloseAll();
                SpiralOverlay.SkipPlatformChecksForTests = false;
                CoreSession.IsEngineRunningProvider = provider;
                (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.SpiralTargetMonitor, s.PanicKeyEnabled, s.PanicKey) = saved;
                cardHost.Close();
                shell.Close();
                Directory.Delete(dir, true);
            }
        });
    }

    [Fact]
    public async Task The_shipped_spiral_is_linked_and_decodes_to_the_planned_frames()
    {
        await EnsurePlatform();   // Bitmap needs the render platform; do not rely on test order (P02)
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "spiral.gif");
        var sw = Stopwatch.StartNew();
        var (frames, delay) = SpiralOverlay.Decode(path);
        output.WriteLine($"spiral decode: {sw.ElapsedMilliseconds} ms");
        try
        {
            Assert.Equal(32, frames.Count);
            Assert.Equal(new PixelSize(1280, 853), frames[0].PixelSize);
            Assert.Equal(TimeSpan.FromMilliseconds(30), delay);
        }
        finally { foreach (var f in frames) f.Dispose(); }
    }

    private static Task EnsurePlatform() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        return Task.CompletedTask;
    });

    // 8x8 red; frame 1 a green 2x2 patch with "restore previous" disposal; frame 2 a 2x2 patch on top. PIL-made.
    private const string RestorePreviousGif =
        "R0lGODlhCAAIAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQEAwAAACwAAAAACAAIAAAIDwABCBxIsKDBgwgTKkwYEAAh+QQNAwACACwAAAAAAgACAIEA/wD/AAAAAAAAAAAIBgABCAQQEAAh+QQFAwACACwAAAAAAgACAIH/AAAAAP8AAAAAAAAIBgADABgYEAA7";

    [Fact]
    public async Task A_restore_previous_frame_does_not_cut_the_gif_short()
    {
        await EnsurePlatform();
        var dir = Directory.CreateTempSubdirectory("ccp-spiral-").FullName;
        var gif = Path.Combine(dir, "restore.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(RestorePreviousGif));
        var (frames, _) = SpiralOverlay.Decode(gif);
        try { Assert.Equal(3, frames.Count); }
        finally { foreach (var f in frames) f.Dispose(); Directory.Delete(dir, true); }
    }

    /// <summary>A path picked while the first decode is in flight is decoded once that lands, not dropped.</summary>
    [Fact]
    public async Task A_path_change_mid_decode_shows_the_new_spiral()
        => await WithShell(async (shell, s, gif) =>
        {
            var other = Path.Combine(Path.GetDirectoryName(gif)!, "other.gif");
            File.WriteAllBytes(other, Convert.FromBase64String(RestorePreviousGif));   // 8x8, the first is 4x4
            shell.StartEngine();
            var first = SpiralOverlay.Decoding!;
            s.SpiralPath = other;
            SpiralOverlay.Refresh(shell);   // the card's SelectSpiral, while the 4x4 decode runs
            await first;
            if (SpiralOverlay.Decoding is { } next) await next;
            Assert.True(SpiralOverlay.IsShowing);
            Assert.Equal(new PixelSize(8, 8), ((global::Avalonia.Media.Imaging.Bitmap)SpiralOverlay.Shown[0].Spiral.Source!).PixelSize);
        });

    /// <summary>WPF PauseSession -> App.Overlay.Stop(), ResumeSession -> Start(); StartSession starts it.</summary>
    [Fact]
    public async Task Session_start_shows_it_pause_hides_it_resume_brings_it_back()
        => await WithShell(async (shell, s, gif) =>
        {
            var runner = global::ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
            try
            {
                s.SkipPauseXpWarning = true;
                var session = new Session { Id = "spiral_test", Name = "Spiral Test", DurationMinutes = 1 };
                (session.Settings.SpiralEnabled, session.Settings.SpiralOpacity, session.Settings.SpiralOpacityEnd) = (true, 40, 40);
                shell.StartSession(session);
                await Settle();
                Assert.True(runner.IsRunning && SpiralOverlay.IsShowing && SpiralOverlay.IsAnimating, Why());
                var pause = shell.Named<Button>("BtnPauseSession")!;
                pause.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));   // Pause
                await Task.Yield();
                Assert.True(runner.IsPaused);
                Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.IsAnimating);
                pause.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));   // Resume
                Assert.False(runner.IsPaused);
                Assert.True(SpiralOverlay.IsShowing && SpiralOverlay.IsAnimating);
            }
            finally
            {
                runner.Stop();
                CoreSession.IsSessionRunningProvider = null;
                global::ConditioningControlPanel.Avalonia.App.Sessions = null;
            }
        });

    [Fact]
    public async Task A_session_spiral_starts_at_its_minute_ramps_then_hands_the_slider_back()
        => await WithShell(async (shell, s, gif) =>
        {
            var runner = global::ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
            try
            {
                var session = new Session { Id = "spiral_ramp", Name = "Spiral Ramp", DurationMinutes = 20 };
                session.Settings.RampCurve = RampCurve.Linear;
                (session.Settings.SpiralEnabled, session.Settings.SpiralStartMinute) = (true, 5);
                (session.Settings.SpiralOpacity, session.Settings.SpiralOpacityEnd) = (10, 50);
                shell.StartSession(session);
                await Settle();
                var at = runner.SpiralStartMinute;
                Assert.InRange(at, 2.0, 8.0);   // SessionEngine.RandomizeStartTimes: +-3 min
                Assert.False(s.SpiralEnabled || SpiralOverlay.IsShowing, Why());   // delayed: off until its minute

                runner.Tick(TimeSpan.FromMinutes(at - 0.01));
                shell.OnSessionTick();
                Assert.False(SpiralOverlay.IsShowing);
                var refreshes = SpiralOverlay.Refreshes;
                runner.Tick(TimeSpan.FromMinutes(at - 0.005));
                shell.OnSessionTick();
                Assert.Equal(refreshes, SpiralOverlay.Refreshes);   // nothing moved: no per-tick screen/file probe
                runner.Tick(TimeSpan.FromMinutes((at + 20) / 2));   // halfway through the spiral's own window
                shell.OnSessionTick();
                await Settle();
                shell.OnSessionTick();
                Assert.True(SpiralOverlay.IsShowing, Why());
                Assert.Equal(30, runner.SpiralOpacity!.Value, 2);
                Assert.Equal(SpiralFrames.Paint(0.30), SpiralOverlay.Shown[0].Spiral.Opacity, 3);
                runner.Tick(TimeSpan.FromMinutes((at + 20) / 2 + 1));   // the ramp moved: repainted
                shell.OnSessionTick();
                Assert.Equal(SpiralFrames.Paint(runner.SpiralOpacity!.Value / 100), SpiralOverlay.Shown[0].Spiral.Opacity, 3);
                Assert.Equal(40, s.SpiralOpacity);   // the user's slider is held, never written (#897)
            }
            finally
            {
                runner.Stop();
                CoreSession.IsSessionRunningProvider = null;
                global::ConditioningControlPanel.Avalonia.App.Sessions = null;
            }
            Assert.Null(runner.SpiralOpacity);   // WPF ReleaseOpacityRampHolds
            Assert.Equal(SpiralFrames.Paint(0.40), SpiralOverlay.PaintedOpacity, 3);
        });

    [Fact]
    public async Task Ai_spiral_runs_with_engine_off_preserves_opacity_on_off_and_stops_on_panic()
        => await WithShell(async (shell, s, gif) =>
        {
            var lab = CoreAccount.HasLabAccessProvider;
            var oldSwitch = s.CompanionPrompt.AllowAiToControlEffects;
            var surface = SpiralCommand.Surface;
            try
            {
                CoreAccount.HasLabAccessProvider = () => true;
                s.CompanionPrompt.AllowAiToControlEffects = true;
                s.SpiralEnabled = false;
                CompanionEffects.Seed();
                Assert.NotNull(SpiralCommand.Surface);
                // The desktop lifetime supplies this same host; headless has no desktop lifetime.
                SpiralCommand.Surface = (on, opacity) => CompanionEffects.ApplySpiral(shell, on, opacity);
                Assert.True(await new SpiralCommand(new SpiralPinkFiler(true, 25)).ExecuteAsync());
                await Settle();
                Assert.False(CoreEngine.IsRunning);
                Assert.True(SpiralOverlay.IsShowing, Why());
                Assert.True(MainShellWindow.KeepPortalBound);
                Assert.Equal(25, s.SpiralOpacity);
                Assert.Equal(SpiralFrames.Paint(0.25), SpiralOverlay.Shown[0].Spiral.Opacity, 10);

                Assert.True(await new SpiralCommand(new SpiralPinkFiler(false, 0)).ExecuteAsync());
                Assert.False(SpiralOverlay.IsShowing);
                Assert.False(s.SpiralEnabled);
                Assert.Equal(25, s.SpiralOpacity);
                var nextGif = Path.Combine(Path.GetDirectoryName(gif)!, "next.gif");
                File.Copy(gif, nextGif);
                s.SpiralPath = nextGif;   // force a fresh decode that can land after the panic
                Assert.True(await new SpiralCommand(new SpiralPinkFiler(true, 0)).ExecuteAsync());
                Assert.Equal(25, s.SpiralOpacity);
                MainShellWindow.StopEngine();   // panic's engine stop also runs when the engine was off
                await Settle();
                Assert.False(SpiralOverlay.IsShowing);
                Assert.False(SpiralOverlay.AiRunning);
                Assert.True(s.SpiralEnabled);  // panic does not change the saved switch
            }
            finally
            {
                SpiralCommand.Surface = surface;
                s.CompanionPrompt.AllowAiToControlEffects = oldSwitch;
                CoreAccount.HasLabAccessProvider = lab;
            }
        });

    [Fact]
    public async Task Ai_spiral_refusal_and_delayed_start_leave_settings_untouched()
        => await WithShell(async (shell, s, gif) =>
        {
            var lab = CoreAccount.HasLabAccessProvider;
            var oldSwitch = s.CompanionPrompt.AllowAiToControlEffects;
            try
            {
                CoreAccount.HasLabAccessProvider = () => true;
                s.CompanionPrompt.AllowAiToControlEffects = true;
                (s.SpiralEnabled, s.SpiralOpacity) = (false, 40);
                Assert.False(CompanionEffects.ApplySpiral(null, true, 25));
                SpiralOverlay.SkipPlatformChecksForTests = false;
                Assert.False(CompanionEffects.ApplySpiral(shell, true, 25)); // headless has no X11 compositor
                SpiralOverlay.SkipPlatformChecksForTests = true;
                Assert.Equal((false, 40), (s.SpiralEnabled, s.SpiralOpacity));

                Action? held = null;
                CompanionEffects.StartEffect = start => held = start;
                Assert.True(CompanionEffects.ApplySpiral(shell, true, 25));
                MainShellWindow.CancelPendingAi();
                held!();
                await Settle();
                Assert.Equal((false, 40), (s.SpiralEnabled, s.SpiralOpacity));
                Assert.False(SpiralOverlay.IsShowing);
            }
            finally
            {
                CompanionEffects.StartEffect = MainShellWindow.StartEffect;
                SpiralOverlay.SkipPlatformChecksForTests = true;
                s.CompanionPrompt.AllowAiToControlEffects = oldSwitch;
                CoreAccount.HasLabAccessProvider = lab;
            }
        });

    [Fact]
    public async Task Pausing_a_session_stops_an_ai_spiral()
        => await WithShell(async (shell, s, gif) =>
        {
            var lab = CoreAccount.HasLabAccessProvider;
            var oldSwitch = s.CompanionPrompt.AllowAiToControlEffects;
            var runner = global::ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
            try
            {
                CoreAccount.HasLabAccessProvider = () => true;
                s.CompanionPrompt.AllowAiToControlEffects = true;
                s.SkipPauseXpWarning = true;
                var session = new Session { Id = "ai_spiral", Name = "AI Spiral", DurationMinutes = 1 };
                shell.StartSession(session);
                Assert.True(CompanionEffects.ApplySpiral(shell, true, 25));
                await Settle();
                Assert.True(SpiralOverlay.IsShowing, Why());
                shell.Named<Button>("BtnPauseSession")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Yield();
                Assert.True(runner.IsPaused);
                Assert.False(SpiralOverlay.IsShowing);
                Assert.False(SpiralOverlay.AiRunning);
            }
            finally
            {
                runner.Stop();
                CoreSession.IsSessionRunningProvider = null;
                global::ConditioningControlPanel.Avalonia.App.Sessions = null;
                s.CompanionPrompt.AllowAiToControlEffects = oldSwitch;
                CoreAccount.HasLabAccessProvider = lab;
            }
        });

    [Theory]
    [InlineData(0)] // engine stop
    [InlineData(1)] // session pause
    [InlineData(2)] // session end
    [InlineData(3)] // card off
    [InlineData(4)] // wall off
    public async Task A_held_ai_start_is_cancelled_by_stop_or_session_pause(int reason)
        => await WithShell(async (shell, s, gif) =>
        {
            var lab = CoreAccount.HasLabAccessProvider;
            var consent = s.CompanionPrompt.AllowAiToControlEffects;
            SessionRunner? runner = null;
            Window? cardHost = null;
            try
            {
                CoreAccount.HasLabAccessProvider = () => true;
                s.CompanionPrompt.AllowAiToControlEffects = true;
                if (reason is 1 or 2)
                {
                    if (reason == 2) (s.SpiralEnabled, s.SpiralOpacity) = (false, 40);   // session snapshot
                    runner = global::ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
                    s.SkipPauseXpWarning = true;
                    shell.StartSession(new Session { Id = "held_ai", Name = "Held AI", DurationMinutes = 1 });
                    Assert.True(runner.IsRunning);
                }
                (s.SpiralEnabled, s.SpiralOpacity) = (reason is 3 or 4, 40);
                SpiralFeatureControl? card = null;
                if (reason == 3) { card = new SpiralFeatureControl(); cardHost = new Window { Content = card }; cardHost.Show(); }
                Action? held = null;
                CompanionEffects.StartEffect = start => held = start;
                Assert.True(CompanionEffects.ApplySpiral(shell, true, 25));
                if (reason == 1)
                {
                    shell.Named<Button>("BtnPauseSession")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await Task.Yield();
                    Assert.True(runner!.IsPaused);
                }
                else if (reason == 2) runner!.Stop();
                else if (reason == 3) card!.FindControl<CheckBox>("ChkEnable")!.IsChecked = false;
                else if (reason == 4) shell.SetWallFeature("spiral", false);
                else MainShellWindow.StopEngine();
                Assert.NotNull(held);
                held!();
                await Settle();
                Assert.Equal((false, 40), (s.SpiralEnabled, s.SpiralOpacity));
                Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.AiRunning);
            }
            finally
            {
                CompanionEffects.StartEffect = MainShellWindow.StartEffect;
                cardHost?.Close();
                runner?.Stop();
                if (runner != null) { CoreSession.IsSessionRunningProvider = null; global::ConditioningControlPanel.Avalonia.App.Sessions = null; }
                s.CompanionPrompt.AllowAiToControlEffects = consent;
                CoreAccount.HasLabAccessProvider = lab;
            }
        });

    [Fact]
    public async Task Confirmed_pause_cancels_a_held_pink_start()
        => await WithShell(async (shell, s, gif) =>
        {
            var lab = CoreAccount.HasLabAccessProvider;
            var consent = s.CompanionPrompt.AllowAiToControlEffects;
            var startEffect = CompanionEffects.StartEffect;
            var skipChecks = PinkFilterOverlay.SkipPlatformChecksForTests;
            var sessions = global::ConditioningControlPanel.Avalonia.App.Sessions;
            var sessionProvider = CoreSession.IsSessionRunningProvider;
            var pink = (s.PinkFilterEnabled, s.PinkFilterOpacity);
            SessionRunner? runner = null;
            try
            {
                CoreAccount.HasLabAccessProvider = () => true;
                s.CompanionPrompt.AllowAiToControlEffects = true;
                runner = global::ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
                s.SkipPauseXpWarning = true;
                shell.StartSession(new Session { Id = "held_pink", Name = "Held pink", DurationMinutes = 1 });
                Assert.True(runner.IsRunning);
                (s.PinkFilterEnabled, s.PinkFilterOpacity) = (false, 10);
                PinkFilterOverlay.SkipPlatformChecksForTests = true;
                Action? held = null;
                CompanionEffects.StartEffect = start => held = start;
                Assert.True(CompanionEffects.ApplyPink(shell, true, 25));
                shell.Named<Button>("BtnPauseSession")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                await Task.Yield();
                Assert.True(runner.IsPaused);
                Assert.NotNull(held);
                held!();
                Assert.Equal((false, 10), (s.PinkFilterEnabled, s.PinkFilterOpacity));
                Assert.False(PinkFilterOverlay.IsShowing || PinkFilterOverlay.AiRunning);
            }
            finally
            {
                CompanionEffects.StartEffect = startEffect;
                PinkFilterOverlay.StopForAi();
                PinkFilterOverlay.CloseAll();
                PinkFilterOverlay.SkipPlatformChecksForTests = skipChecks;
                runner?.Stop();
                CoreSession.IsSessionRunningProvider = sessionProvider;
                global::ConditioningControlPanel.Avalonia.App.Sessions = sessions;
                (s.PinkFilterEnabled, s.PinkFilterOpacity) = pink;
                s.CompanionPrompt.AllowAiToControlEffects = consent;
                CoreAccount.HasLabAccessProvider = lab;
            }
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Either_ai_command_starts_pink_and_off_keeps_the_service_running(bool on)
        => await WithShell(async (shell, s, gif) =>
        {
            var lab = CoreAccount.HasLabAccessProvider;
            var consent = s.CompanionPrompt.AllowAiToControlEffects;
            var pink = s.PinkFilterEnabled;
            Window? cardHost = null;
            try
            {
                CoreAccount.HasLabAccessProvider = () => true;
                s.CompanionPrompt.AllowAiToControlEffects = true;
                PinkFilterOverlay.SkipPlatformChecksForTests = true;
                (s.SpiralEnabled, s.PinkFilterEnabled) = (false, true);
                Assert.True(CompanionEffects.ApplySpiral(shell, on, on ? 25 : null));
                await Settle();
                Assert.True(PinkFilterOverlay.IsShowing);
                Assert.True(SpiralOverlay.AiRunning);
                if (on) Assert.True(CompanionEffects.ApplySpiral(shell, false, null));
                Assert.False(SpiralOverlay.IsShowing);
                Assert.True(SpiralOverlay.AiRunning);
                var card = new SpiralFeatureControl();
                cardHost = new Window { Content = card };
                cardHost.Show();
                card.FindControl<CheckBox>("ChkEnable")!.IsChecked = true;
                await Settle();
                Assert.True(SpiralOverlay.IsShowing, Why());
                MainShellWindow.StopEngine();
                Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.AiRunning || PinkFilterOverlay.IsShowing);
            }
            finally
            {
                cardHost?.Close();
                PinkFilterOverlay.CloseAll();
                PinkFilterOverlay.SkipPlatformChecksForTests = false;
                s.PinkFilterEnabled = pink;
                s.CompanionPrompt.AllowAiToControlEffects = consent;
                CoreAccount.HasLabAccessProvider = lab;
            }
        });

    [Fact]
    public async Task Ai_off_keeps_the_service_running_for_the_spiral_card_until_stop()
        => await WithShell(async (shell, s, gif) =>
        {
            var lab = CoreAccount.HasLabAccessProvider;
            var consent = s.CompanionPrompt.AllowAiToControlEffects;
            try
            {
                CoreAccount.HasLabAccessProvider = () => true;
                s.CompanionPrompt.AllowAiToControlEffects = true;
                s.SpiralEnabled = false;
                Assert.True(CompanionEffects.ApplySpiral(shell, false, null));
                Assert.True(SpiralOverlay.AiRunning);
                Assert.False(MainShellWindow.KeepPortalBound);
                var card = new SpiralFeatureControl();
                var host = new Window { Content = card };
                try
                {
                    host.Show();
                    card.FindControl<CheckBox>("ChkEnable")!.IsChecked = true;
                    await Settle();
                    Assert.True(SpiralOverlay.IsShowing, Why());
                    MainShellWindow.StopEngine();
                    Assert.False(SpiralOverlay.AiRunning || SpiralOverlay.IsShowing);
                }
                finally { host.Close(); }
            }
            finally
            {
                s.CompanionPrompt.AllowAiToControlEffects = consent;
                CoreAccount.HasLabAccessProvider = lab;
            }
        });

    [Theory]
    [InlineData("XID", true)]
    [InlineData("HWND", true)]
    [InlineData("WL_SURFACE", false)]
    [InlineData("headless", false)]
    public void Ai_spiral_checks_the_actual_window_backend(string descriptor, bool expected)
    {
        var handle = new global::Avalonia.Platform.PlatformHandle(new IntPtr(1), descriptor);
        Assert.Equal(expected, SpiralOverlay.CanStartForAi(handle, true, true, false));
        Assert.False(SpiralOverlay.CanStartForAi(handle, true, false, false));
        Assert.False(SpiralOverlay.CanStartForAi(handle, true, true, true));
    }

    [Fact]
    public async Task A_synchronous_spiral_start_failure_is_reported_without_saving_settings()
        => await WithShell(async (shell, s, gif) =>
        {
            var lab = CoreAccount.HasLabAccessProvider;
            var consent = s.CompanionPrompt.AllowAiToControlEffects;
            var player = SpiralOverlay.VideoStart;
            try
            {
                CoreAccount.HasLabAccessProvider = () => true;
                s.CompanionPrompt.AllowAiToControlEffects = true;
                (s.SpiralEnabled, s.SpiralOpacity) = (false, 40);
                s.SpiralPath = Path.ChangeExtension(gif, ".mp4");
                File.WriteAllBytes(s.SpiralPath, Array.Empty<byte>());
                SpiralOverlay.VideoStart = (_, _) => null;
                Assert.False(CompanionEffects.ApplySpiral(shell, true, 25));
                Assert.Equal((false, 40), (s.SpiralEnabled, s.SpiralOpacity));
                Assert.False(SpiralOverlay.IsShowing || SpiralOverlay.AiRunning);
                await Task.CompletedTask;
            }
            finally
            {
                SpiralOverlay.VideoStart = player;
                s.CompanionPrompt.AllowAiToControlEffects = consent;
                CoreAccount.HasLabAccessProvider = lab;
            }
        });

    [Fact]
    public async Task An_uncached_gif_refuses_a_failed_window_preflight()
        => await WithShell(async (shell, s, gif) =>
        {
            var lab = CoreAccount.HasLabAccessProvider;
            var consent = s.CompanionPrompt.AllowAiToControlEffects;
            try
            {
                CoreAccount.HasLabAccessProvider = () => true;
                s.CompanionPrompt.AllowAiToControlEffects = true;
                (s.SpiralEnabled, s.SpiralOpacity) = (false, 40);
                SpiralOverlay.PreflightForTests = _ => false;   // window creation or click-through refused
                Assert.False(CompanionEffects.ApplySpiral(shell, true, 25));
                Assert.Equal((false, 40), (s.SpiralEnabled, s.SpiralOpacity));
                Assert.False(SpiralOverlay.AiRunning || SpiralOverlay.IsShowing);
                Assert.Null(SpiralOverlay.Decoding);
                await Task.CompletedTask;
            }
            finally
            {
                SpiralOverlay.PreflightForTests = null;
                s.CompanionPrompt.AllowAiToControlEffects = consent;
                CoreAccount.HasLabAccessProvider = lab;
            }
        });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Idle_service_releases_portal_and_card_activation_binds_before_show(int kind)
        => await WithShell(async (shell, s, gif) =>
        {
            bool pink = (kind & 1) != 0, wall = kind >= 2;
            var lab = CoreAccount.HasLabAccessProvider;
            var consent = s.CompanionPrompt.AllowAiToControlEffects;
            var oldPink = s.PinkFilterEnabled;
            Window? cardHost = null;
            Action? held = null;
            try
            {
                CoreAccount.HasLabAccessProvider = () => true;
                s.CompanionPrompt.AllowAiToControlEffects = true;
                (s.SpiralEnabled, s.PinkFilterEnabled) = (false, false);
                PinkFilterOverlay.SkipPlatformChecksForTests = true;
                Assert.True(CompanionEffects.ApplySpiral(shell, false, null));
                Assert.True(SpiralOverlay.AiRunning);
                MainShellWindow.StartCardEffect = start => held = start;
                UserControl card = pink ? new PinkFilterFeatureControl() : new SpiralFeatureControl();
                cardHost = new Window { Content = card };
                cardHost.Show();
                var toggle = card.FindControl<CheckBox>("ChkEnable")!;
                if (wall) shell.SetWallFeature(pink ? "pinkfilter" : "spiral", true);
                else toggle.IsChecked = true;
                Assert.NotNull(held);
                Assert.True(MainShellWindow.KeepPortalBound);   // the bind is pending
                PortalPanicShortcut.SetSessionForTests("/pending", null);   // handle exists before BindShortcuts finishes
                card.FindControl<Slider>("SliderOpacity")!.Value = 42;
                Assert.False(SpiralOverlay.IsShowing || PinkFilterOverlay.IsShowing);
                Assert.Null(SpiralOverlay.Decoding);
                var release = held!; held = null; release();
                await Settle();
                Assert.True(pink ? PinkFilterOverlay.IsShowing : SpiralOverlay.IsShowing);
                Assert.True(MainShellWindow.KeepPortalBound);
                if (wall) shell.SetWallFeature(pink ? "pinkfilter" : "spiral", false);
                else toggle.IsChecked = false;
                Assert.False(MainShellWindow.KeepPortalBound);
            }
            finally
            {
                held?.Invoke();
                PortalPanicShortcut.SetSessionForTests(null, null);
                MainShellWindow.StartCardEffect = MainShellWindow.StartEffect;
                cardHost?.Close();
                PinkFilterOverlay.CloseAll();
                PinkFilterOverlay.SkipPlatformChecksForTests = false;
                s.PinkFilterEnabled = oldPink;
                s.CompanionPrompt.AllowAiToControlEffects = consent;
                CoreAccount.HasLabAccessProvider = lab;
            }
        });

    [Fact]
    public async Task Portal_session_handle_does_not_mean_shortcut_binding_is_complete()
    {
        if (!OperatingSystem.IsLinux()) return;
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            var type = typeof(MainShellWindow);
            var allowed = type.GetField("_portalAllowed", flags)!;
            var failed = type.GetField("_portalFailed", flags)!;
            var bind = type.GetField("_portalBind", flags)!;
            var saved = (allowed.GetValue(null), failed.GetValue(null), bind.GetValue(null), Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
            var panic = CoreSettings.Current.PanicKeyEnabled;
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool ran = false;
            try
            {
                allowed.SetValue(null, true); failed.SetValue(null, false); bind.SetValue(null, tcs.Task);
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", "ccp-test");
                CoreSettings.Current.PanicKeyEnabled = false; bool cardRan = false;
                MainShellWindow.QueueCardEffect(() => cardRan = true); Assert.True(cardRan);
                CoreSettings.Current.PanicKeyEnabled = true;
                PortalPanicShortcut.SetSessionForTests("/pending", null);
                MainShellWindow.StartEffect(() => ran = true);
                Assert.False(ran);   // session exists, but BindShortcuts is still in flight
                tcs.SetResult(true);
                for (int i = 0; i < 100 && !ran; i++) await Task.Delay(10);
                Assert.True(ran);
            }
            finally
            {
                tcs.TrySetResult(true);
                for (int i = 0; i < 100 && !ran; i++) await Task.Delay(10);
                PortalPanicShortcut.SetSessionForTests(null, null);
                allowed.SetValue(null, saved.Item1); failed.SetValue(null, saved.Item2); bind.SetValue(null, saved.Item3);
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", saved.Item4);
                CoreSettings.Current.PanicKeyEnabled = panic;
            }
        });
    }

    /// <summary>Waits out every in-flight decode, including one a landing decode starts for a newer path.</summary>
    private static async Task Settle()
    {
        while (SpiralOverlay.Decoding is { IsCompleted: false } d) await d;
    }

    private static string Why()
    {
        var sessions = global::ConditioningControlPanel.Avalonia.App.Sessions;
        return $"windows={SpiralOverlay.Shown.Count} decoding={SpiralOverlay.Decoding?.Status} engine={CoreEngine.IsRunning} " +
        $"sessions={sessions?.IsRunning}/{sessions?.IsPaused} " +
        $"enabled={CoreSettings.Current.SpiralEnabled} path={CoreSettings.Current.SpiralPath} exists={File.Exists(CoreSettings.Current.SpiralPath)}";
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task WithShell_restores_previous_globals(bool checkHook, bool throwFromBody)
    {
        var previousHook = CoreEngine.StoppedHook;
        var previousSkip = SpiralOverlay.SkipPlatformChecksForTests;
        var calls = 0;
        Action hook = () => calls++;
        try
        {
            CoreEngine.StoppedHook = hook;
            SpiralOverlay.SkipPlatformChecksForTests = true;
            async Task Run() => await WithShell((shell, settings, gif) =>
                throwFromBody ? Task.FromException(new InvalidOperationException("test body")) : Task.CompletedTask);
            if (throwFromBody) await Assert.ThrowsAsync<InvalidOperationException>(Run);
            else await Run();
            if (checkHook) Assert.Same(hook, CoreEngine.StoppedHook);
            else Assert.True(SpiralOverlay.SkipPlatformChecksForTests);
            Assert.Equal(0, calls);
        }
        finally
        {
            CoreEngine.StoppedHook = previousHook;
            SpiralOverlay.SkipPlatformChecksForTests = previousSkip;
        }
    }

    private static async Task WithShell(Func<MainShellWindow, AppSettings, string, Task> body)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-spiral-").FullName;
        var gif = Path.Combine(dir, "tiny.gif");
        File.WriteAllBytes(gif, Convert.FromBase64String(TinyGif));
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.SpiralTargetMonitor, s.SkipPauseXpWarning);
            var provider = CoreSession.IsEngineRunningProvider;
            var stoppedHook = CoreEngine.StoppedHook;
            var skipChecks = SpiralOverlay.SkipPlatformChecksForTests;
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                CoreSession.IsEngineRunningProvider = () => CoreEngine.IsRunning;
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                SpiralOverlay.SkipPlatformChecksForTests = true;
                await Settle();   // P02: an earlier test's decode still in flight would swallow this test's first one
                (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.SpiralTargetMonitor) = (true, gif, 40, -1);
                await body(shell, s, gif);
            }
            finally
            {
                CoreEngine.StoppedHook = null;   // stop this shell without invoking the previous hook
                try
                {
                    CoreEngine.Stop();
                    SpiralOverlay.CloseAll();
                    CoreSession.IsEngineRunningProvider = provider;
                    (s.SpiralEnabled, s.SpiralPath, s.SpiralOpacity, s.SpiralTargetMonitor, s.SkipPauseXpWarning) = saved;
                    shell.Close();
                    Directory.Delete(dir, true);
                }
                finally
                {
                    CoreEngine.StoppedHook = stoppedHook;
                    SpiralOverlay.SkipPlatformChecksForTests = skipChecks;
                }
            }
        });
    }

    [Fact]
    public void Spiral_settings_round_trip_through_settings_json()
    {
        var a = new AppSettings { SpiralEnabled = true, SpiralOpacity = 73, SpiralTargetMonitor = 1, SpiralPath = "/x/s.gif", SpiralRandomize = true };
        var b = JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(a))!;
        Assert.Equal((true, 73, 1, "/x/s.gif", true), (b.SpiralEnabled, b.SpiralOpacity, b.SpiralTargetMonitor, b.SpiralPath, b.SpiralRandomize));
    }
}
