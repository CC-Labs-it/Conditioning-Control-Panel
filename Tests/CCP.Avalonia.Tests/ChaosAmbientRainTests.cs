using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ChaosAmbientRainTests
{
    private sealed class Dice(double value) : Random
    {
        public override double NextDouble() => value;
    }
    private sealed class Clock : TimeProvider
    {
        public long Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Now;
    }
    private static void Setup()
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    // WPF ChaosModeService.cs:2279-2291,2346-2349. Real failed-hold callback,
    // deterministic coin branches, duration scaling and the busy-rain override.
    [Theory]
    [InlineData(0.1, false, EffectBubblePayloadKind.GifCascade)]
    [InlineData(0.9, false, EffectBubblePayloadKind.BouncingText)]
    [InlineData(0.1, true, EffectBubblePayloadKind.BouncingText)]
    public async Task Ambient_failed_hold_softens_htlink(double roll, bool busy, EffectBubblePayloadKind expected)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var dice = ChaosRunHost.AmbientRng;
            var saved = (EffectPayload.Firer, CoreAudio.PlayOneShotProvider, EffectPayload.GlobalDurationMult);
            var fired = new List<(EffectPayload Payload, double Duration)>();
            var sounds = new List<string>();
            try
            {
                ChaosRunHost.StartRun(new ChaosRunConfig { AmbientMode = true, DurationSec = 60 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                ChaosRunHost.AmbientRng = new Dice(roll);
                EffectPayload.Firer = p => fired.Add((p, EffectPayload.GlobalDurationMult));
                CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => sounds.Add(System.IO.Path.GetFileNameWithoutExtension(path));
                ChaosRunHost.State!.DetonationDurationMult = 1.5;
                // Video stays a video even while ambient mode is active.
                ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() });
                Assert.IsType<VideoPayload>(Assert.Single(fired).Payload);
                if (busy) ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new GifCascadePayload() });
                fired.Clear(); sounds.Clear();
                var b = BubbleOverlay.SpawnChaos(new EffectBubbleSpec
                {
                    IsLive = true, FuseMs = 5000, SizePx = 200,
                    Payload = new HtLinkPayload { Strength = 73 }
                });
                ChaosRunHost.State.Focus = 0;
                BubbleOverlay.Press(b, b.CenterX, b.CenterY);
                var result = Assert.Single(fired);
                Assert.Equal(expected, result.Payload.Kind);
                Assert.Equal(73, result.Payload.Strength);
                Assert.Equal(1.5, result.Duration);
                Assert.Equal(1, EffectPayload.GlobalDurationMult);
                Assert.Contains(expected == EffectBubblePayloadKind.GifCascade ? "fx_rain_start" : "fx_text", sounds);
                fired.Clear();
                // WPF deliberately leaves video out of the ambient remap.
                ChaosRunHost.ForceShutdown();
                ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() });
                Assert.IsType<VideoPayload>(Assert.Single(fired).Payload);
            }
            finally { ChaosRunHost.ForceShutdown(); ChaosRunHost.AmbientRng = dice;
                (EffectPayload.Firer, CoreAudio.PlayOneShotProvider, EffectPayload.GlobalDurationMult) = saved; }
            return Task.CompletedTask;
        });
    }

    // WPF ChaosModeService.cs:2298-2303: the payload is dropped, but its HUD event survives.
    [Fact]
    public async Task Heavy_drop_reaches_live_feed_without_firing()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var firer = EffectPayload.Firer;
            var language = ConditioningControlPanel.Localization.LocalizationManager.Instance.CurrentLanguage;
            try
            {
                ConditioningControlPanel.Localization.LocalizationManager.Instance.SetLanguage("en");
                ChaosRunHost.StartRun(new ChaosRunConfig());
                int fired = 0;
                EffectPayload.Firer = _ => fired++;
                ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new GifCascadePayload() });
                ChaosRunHost.FirePayloadForDetonation(new EffectBubbleSpec { Payload = new VideoPayload() });
                Assert.Equal(1, fired);
                // The trunk's feed line (chaos_heavy_fizzle) already carries the drop; one key, not two.
                Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("chaos_heavy_fizzle", "video"), ChaosRunHost.State!.RecentEvents[0]);
            }
            finally { ChaosRunHost.ForceShutdown(); EffectPayload.Firer = firer;
                ConditioningControlPanel.Localization.LocalizationManager.Instance.SetLanguage(language); }
            return Task.CompletedTask;
        });
    }

    // WPF EffectPayloadFiring.cs:177-220: actual runner, enabled phrase, scoped duration,
    // 12 bounces, user ownership, and stop releasing an old timeout before a new owner.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Text_runner_uses_renderer_and_releases_ownership_on_timeout_bounces_and_stop(bool rebuild, bool realTimer = false)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var hostProvider = AvApp.ChaosTextHost;
            var settings = CoreSettings.Current;
            var renderer = (ChaosBouncingTextEffect.Clock, BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests);
            var saved = (EffectPayload.GlobalDurationMult, settings.BouncingTextPool, settings.BouncingTextOutline, BouncingTextOverlay.RaiseAll);
            var clock = new Clock();
            var host = new Window();
            try
            {
                host.Show();
                AvApp.ChaosTextHost = () => host;
                BouncingTextOverlay.SkipPlatformChecksForTests = true;
                BouncingTextOverlay.ManualFramesForTests = true;
                BouncingTextOverlay.RaiseAll = () => { };
                ChaosBouncingTextEffect.Clock = clock;
                CoreSettings.Current.BouncingTextPool = new Dictionary<string, bool> { ["enabled"] = true, ["disabled"] = false };
                if (realTimer)
                {
                    EffectPayload.GlobalDurationMult = 0; // 0.5 s floor, frozen elapsed clock
                    AvApp.FireChaosPayload(new BouncingTextPayload());
                    Assert.True(BouncingTextOverlay.IsRunning);
                    Assert.True(ChaosBouncingTextEffect.IsActive);
                    Assert.Equal(0.5, ChaosBouncingTextEffect.Duration);
                    for (int i = 0; i < 250 && ChaosBouncingTextEffect.IsActive; i++) await Task.Delay(20);
                    Assert.False(BouncingTextOverlay.IsRunning);
                    Assert.False(ChaosBouncingTextEffect.IsActive);
                    return; // only the actual DispatcherTimer can stop this payload
                }
                EffectPayload.GlobalDurationMult = 2;
                AvApp.FireChaosPayload(new BouncingTextPayload());
                Assert.True(BouncingTextOverlay.IsRunning);
                Assert.True(ChaosBouncingTextEffect.IsActive);
                Assert.All(BouncingTextOverlay.Engine.Logos, l => Assert.Equal("enabled", l.Text));
                Assert.Equal(16, ChaosBouncingTextEffect.Duration);
                clock.Now = TimeSpan.FromSeconds(15.9).Ticks;
                ChaosBouncingTextEffect.CheckExpiry();
                Assert.True(BouncingTextOverlay.IsRunning);
                // WPF BouncingTextService.cs:165-170, EffectPayloadFiring.cs:190-203:
                // a settings rebuild must retain the original deadline and bounce count.
                if (rebuild)
                {
                    CoreSettings.Current.BouncingTextOutline = !CoreSettings.Current.BouncingTextOutline;
                    BouncingTextOverlay.Restart();
                    Assert.True(ChaosBouncingTextEffect.IsActive);
                    Assert.All(BouncingTextOverlay.Engine.Logos, l => Assert.Equal("enabled", l.Text));
                }
                clock.Now = TimeSpan.FromSeconds(16).Ticks;
                ChaosBouncingTextEffect.CheckExpiry();
                Assert.False(BouncingTextOverlay.IsRunning);
                Assert.False(ChaosBouncingTextEffect.IsActive);
                BouncingTextOverlay.Start(host);
                AvApp.FireChaosPayload(new BouncingTextPayload());
                Assert.False(ChaosBouncingTextEffect.IsActive); // user-started bouncer is untouched
                BouncingTextOverlay.Stop();
                AvApp.FireChaosPayload(new BouncingTextPayload());
                for (int i = 0; i < BouncingTextPayload.MAX_BOUNCES; i++)
                {
                    var logo = BouncingTextOverlay.Engine.Logos[0];
                    logo.PosX = BouncingTextOverlay.Engine.MinX - 10;
                    logo.VelX = -100;
                    BouncingTextOverlay.Advance(0.016);
                    Assert.Equal(i < BouncingTextPayload.MAX_BOUNCES - 1, BouncingTextOverlay.IsRunning);
                    if (rebuild && i == 5) BouncingTextOverlay.Restart();
                }
                AvApp.FireChaosPayload(new BouncingTextPayload());
                BouncingTextOverlay.Stop(); // panic/user stop cancels the payload's lifetime
                Assert.False(ChaosBouncingTextEffect.IsActive);
                BouncingTextOverlay.Start(host);
                clock.Now += TimeSpan.FromSeconds(100).Ticks;
                ChaosBouncingTextEffect.CheckExpiry();
                Assert.True(BouncingTextOverlay.IsRunning);
            }
            finally
            {
                BouncingTextOverlay.Stop(); host.Close();
                AvApp.ChaosTextHost = hostProvider;
                (EffectPayload.GlobalDurationMult, settings.BouncingTextPool, settings.BouncingTextOutline, BouncingTextOverlay.RaiseAll) = saved;
                (ChaosBouncingTextEffect.Clock, BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests) = renderer;
            }
        });
    }

    [Fact]
    public async Task Payload_fixtures_restore_globals_when_called_in_reverse_order()
    {
        var saved = (EffectPayload.GlobalDurationMult, EffectPayload.Firer, CoreAudio.PlayOneShotProvider,
            CoreSettings.Current.BouncingTextPool, CoreSettings.Current.BouncingTextOutline, BouncingTextOverlay.RaiseAll);
        await Text_runner_uses_renderer_and_releases_ownership_on_timeout_bounces_and_stop(true);
        await Ambient_failed_hold_softens_htlink(0.1, false, EffectBubblePayloadKind.GifCascade);
        await Heavy_drop_reaches_live_feed_without_firing();
        Assert.Equal(saved, (EffectPayload.GlobalDurationMult, EffectPayload.Firer, CoreAudio.PlayOneShotProvider,
            CoreSettings.Current.BouncingTextPool, CoreSettings.Current.BouncingTextOutline, BouncingTextOverlay.RaiseAll));
    }
}
