using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Restack b121 (hand-port of #3502 and #4305): WPF ChaosModeService.cs:2035-2040 and
/// BubbleService.cs:1729-1737, 1786-1802. A brink snap with Aftermath leaves a 2 s residue zone.</summary>
public sealed class ChaosAftermathResidueTests
{
    [Fact]
    public async Task Brink_snap_leaves_residue_that_pops_treats_snaps_lives_and_expires()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var stateField = typeof(ChaosRunHost).GetField("_state", BindingFlags.NonPublic | BindingFlags.Static)!;
            var savedState = stateField.GetValue(null);
            var now = ChaosFieldFxOverlay.NowMs;
            var settings = CoreSettings.Current;
            var enabled = settings.ChaosSkiaFxEnabled;
            var canShow = ChaosEStimOverlay.CanShow;
            var clock = BubbleOverlay.Field.Clock;
            var t = new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
            int treats = 0, snaps = 0;
            try
            {
                var state = new ChaosRunState(new ChaosRunConfig()) { AftermathEnabled = true };
                stateField.SetValue(null, state);
                settings.ChaosSkiaFxEnabled = false;
                ChaosEStimOverlay.CanShow = () => false;   // no strike window in a test
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                ChaosFieldFxOverlay.NowMs = () => 0;
                BubbleOverlay.Field.Clock = () => t;
                BubbleOverlay.BeginChaos(new Border(), _ => treats++, b => { snaps++; ChaosRunHost.OnDefused(b); });
                AmbientBubble Add(double x, EffectBubbleSpec? spec, double fuseMs = 0)
                {
                    var b = new AmbientBubble { X = x - 10, Y = -10, Size = 20, Spec = spec, FuseTotalMs = fuseMs, FuseRemainingMs = fuseMs };
                    BubbleOverlay.Field.Bubbles.Add(b);
                    return b;
                }
                var calm = Add(0, new EffectBubbleSpec { IsLive = true }, 5000);
                Assert.True(calm.Snap());
                BubbleOverlay.Field.ChaosDefused!(calm);
                Assert.Equal(0, BubbleOverlay.ChaosResidueCount);   // only a brink snap (<= 1.5 s) leaves residue

                var brink = Add(0, new EffectBubbleSpec { IsLive = true }, 1000);
                Assert.True(brink.Snap());
                BubbleOverlay.Field.ChaosDefused!(brink);
                Assert.Equal(1, BubbleOverlay.ChaosResidueCount);
                Assert.Equal(Loc.Get("chaos_aftermath_event"), state.RecentEvents.Skip(1).First(e => e == Loc.Get("chaos_aftermath_event")));
                Assert.IsType<ChaosFieldFxOverlay>(ChaosFieldFxOverlay.Active);

                var treat = Add(100, new EffectBubbleSpec());
                var live = Add(150, new EffectBubbleSpec { IsLive = true }, 5000);
                var far = Add(200, new EffectBubbleSpec());
                var rabbit = Add(50, new EffectBubbleSpec { IsDarter = true });
                var freeze = Add(50, new EffectBubbleSpec { IsFreeze = true });
                var tease = Add(50, new EffectBubbleSpec { IsTease = true });
                var brittle = Add(50, new EffectBubbleSpec { IsBrittle = true });
                int snapsBefore = snaps;
                BubbleOverlay.HoldChaos(true);
                BubbleOverlay.TickChaosResidues();
                Assert.False(treat.Popping || live.Popping);   // a held field is untouchable
                BubbleOverlay.HoldChaos(false);
                BubbleOverlay.TickChaosResidues();
                Assert.True(treat.Popping);
                Assert.Equal(1, treats);
                Assert.True(live.Popping && !live.Deflating);   // swept snap bursts like WPF Pop
                Assert.Equal(snapsBefore + 1, snaps);
                foreach (var b in new[] { far, rabbit, freeze, tease, brittle }) Assert.False(b.Popping);

                t = t.AddMilliseconds(2000);
                var late = Add(50, new EffectBubbleSpec());
                BubbleOverlay.TickChaosResidues();
                Assert.False(late.Popping);   // wall-time expiry, no ghost zone
                Assert.Equal(0, BubbleOverlay.ChaosResidueCount);

                var again = Add(0, new EffectBubbleSpec { IsLive = true }, 1000);
                Assert.True(again.Snap());
                BubbleOverlay.Field.ChaosDefused!(again);
                Assert.Equal(1, BubbleOverlay.ChaosResidueCount);
                BubbleOverlay.EndChaos();
                Assert.Equal(0, BubbleOverlay.ChaosResidueCount);   // run end clears the zones

                state.AftermathEnabled = false;
                BubbleOverlay.BeginChaos(new Border(), _ => treats++, ChaosRunHost.OnDefused);
                var off = Add(0, new EffectBubbleSpec { IsLive = true }, 1000);
                Assert.True(off.Snap());
                BubbleOverlay.Field.ChaosDefused!(off);
                Assert.Equal(0, BubbleOverlay.ChaosResidueCount);   // boon gate
            }
            finally
            {
                BubbleOverlay.EndChaos();
                BubbleOverlay.Field.Bubbles.Clear();
                BubbleOverlay.Field.Clock = clock;
                ChaosFieldFxOverlay.CloseActive();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                ChaosFieldFxOverlay.NowMs = now;
                ChaosEStimOverlay.CanShow = canShow;
                settings.ChaosSkiaFxEnabled = enabled;
                stateField.SetValue(null, savedState);
            }
            return Task.CompletedTask;
        });
    }

    // Restack b121 (hand-port of #4245 and #4525): WPF ChaosModeService.cs:745-774. Over a video the
    // ambient field FX band goes up first, then the bubbles; unpinned or idle runs raise nothing.
    [Fact]
    public async Task Game_layer_raise_lifts_field_fx_below_the_bubbles_and_respects_the_pin()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var settings = CoreSettings.Current;
            var pin = settings.ChaosPinOnTop;
            var spawning = typeof(ChaosRunHost).GetField("_spawning", BindingFlags.Static | BindingFlags.NonPublic)!;
            var wasSpawning = (bool)spawning.GetValue(null)!;
            var (chrome, overlay, bubbles, fxRaise) = (ChaosRunHost.RaiseChromeWindow, ChaosRunHost.RaiseOverlayWindow,
                ChaosRunHost.RaiseBubbles, ChaosFieldFxOverlay.RaiseWindow);
            var now = ChaosFieldFxOverlay.NowMs;
            var raised = new System.Collections.Generic.List<Window>();
            var bubble = new Window();
            try
            {
                settings.ChaosPinOnTop = true;
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                ChaosFieldFxOverlay.NowMs = () => 0;
                ChaosFieldFxOverlay.Ripple(new Point(10, 10), 100, 500);
                var fx = Assert.IsType<ChaosFieldFxOverlay>(ChaosFieldFxOverlay.Active);
                ChaosFieldFxOverlay.RaiseWindow = raised.Add;
                ChaosRunHost.RaiseChromeWindow = raised.Add;
                ChaosRunHost.RaiseOverlayWindow = raised.Add;
                ChaosRunHost.RaiseBubbles = () => raised.Add(bubble);
                spawning.SetValue(null, true);
                ChaosRunHost.RaiseGameLayerAboveVideo();
                Assert.True(raised.IndexOf(fx) >= 0 && raised.IndexOf(fx) < raised.IndexOf(bubble));
                raised.Clear();
                ChaosRunHost.KeepChromeTopmost();
                Assert.DoesNotContain(fx, raised);   // without a video only the chrome band moves
                raised.Clear();
                settings.ChaosPinOnTop = false;
                ChaosRunHost.RaiseGameLayerAboveVideo();
                Assert.Empty(raised);
                settings.ChaosPinOnTop = true;
                spawning.SetValue(null, false);
                ChaosRunHost.RaiseGameLayerAboveVideo();
                Assert.Empty(raised);
            }
            finally
            {
                spawning.SetValue(null, wasSpawning);
                settings.ChaosPinOnTop = pin;
                (ChaosRunHost.RaiseChromeWindow, ChaosRunHost.RaiseOverlayWindow, ChaosRunHost.RaiseBubbles,
                    ChaosFieldFxOverlay.RaiseWindow) = (chrome, overlay, bubbles, fxRaise);
                ChaosFieldFxOverlay.CloseActive();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                ChaosFieldFxOverlay.NowMs = now;
                bubble.Close();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void Aftermath_feed_line_is_in_all_ten_languages()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Localization", "Languages");
        if (!System.IO.Directory.Exists(dir)) dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Languages");
        var files = System.IO.Directory.GetFiles(dir, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var f in files)
        {
            var text = System.IO.File.ReadAllText(f);
            Assert.Contains("\"chaos_aftermath_event\": \"⚡ ", text);
        }
    }
}
