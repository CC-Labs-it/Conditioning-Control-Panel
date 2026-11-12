using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaos;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

public sealed class ChaosChaperoneTests
{
    private sealed class AlwaysHit : Random { public override double NextDouble() => 0; }

    [Fact]
    public Task Spawned_escort_shields_live_and_shimmers_every_seventh_step() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var settings = CoreSettings.Current;
        bool enabled = settings.ChaosSkiaFxEnabled;
        bool announcer = settings.ChaosAnnouncerEnabled;
        var language = LocalizationManager.Instance.CurrentLanguage;
        var composited = ChaosSkiaFxOverlay.Composited;
        var rng = ChaosRunHost.Rng;
        int runs = ChaosMeta.State.RunsCompleted;
        bool seen = ChaosMeta.State.SeenChaperone;
        var discovered = new HashSet<string>(ChaosMeta.State.DiscoveredCodexIds);
        try
        {
            settings.ChaosSkiaFxEnabled = true;
            ChaosSkiaFxOverlay.Composited = () => true;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = true;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            ChaosMeta.State.RunsCompleted = 100;
            ChaosMeta.State.SeenChaperone = false;
            ChaosRunHost.Rng = new AlwaysHit();
            settings.ChaosAnnouncerEnabled = true;
            LocalizationManager.Instance.Initialize("es");
            ChaosAnnouncerOverlay.CloseActive();
            ChaosRunHost.SpawnTick();
            Assert.Equal("💞 LA ACOMPAÑANTE: primero su pequeña escolta", ChaosAnnouncerOverlay.Active!.Current!.Text);
            Assert.Equal("primero su pequeña escolta", ChaosAnnouncerOverlay.Active!.Current!.SubText);
            var live = Assert.Single(BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.IsChaperoneLive == true));
            Assert.True(ChaosMeta.State.SeenChaperone);
            var feedGlyph = Assert.Single(IconText.Split(ChaosRunHost.State!.RecentEvents[0]).Icons);
            Assert.Equal(IconMap.Chaperone, feedGlyph);
            Assert.True(IconMap.TryGet(feedGlyph, out var feedIcon));
            Assert.Equal(IconKind.Heart, feedIcon.Kind);
            ChaosMeta.State.DiscoveredCodexIds.Add("test:chaperone-isolation");
            var escort = live.Escort!;
            Assert.NotNull(escort);
            Assert.True(BubbleOverlay.IsShielded(live));
            Assert.InRange(live.ShieldOpacity, 0.35, 0.55);
            BubbleOverlay.Press(live, live.CenterX, live.CenterY);
            Assert.Equal(0.95, live.ShieldOpacity);
            Assert.False(live.Channeling);
            BubbleOverlay.PopChaos(live);
            Assert.False(live.Popping);
            BubbleOverlay.PopChaosInRect(new Rect(live.CenterX - 1, live.CenterY - 1, 2, 2));
            Assert.False(live.Popping);
            ChaosSkiaFxOverlay.CloseActive();
            for (int i = 0; i < 6; i++) BubbleOverlay.StepChaosField();
            Assert.Null(ChaosSkiaFxOverlay.Active);
            BubbleOverlay.StepChaosField();
            Assert.NotNull(ChaosSkiaFxOverlay.Active);
            Assert.True(ChaosSkiaFxOverlay.Active!.Fx.ParticleCount > 0);
            escort.Popping = true;
            Assert.False(BubbleOverlay.IsShielded(live));
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosSkiaFxOverlay.CloseActive();
            ChaosRunHost.Rng = rng;
            ChaosMeta.State.RunsCompleted = runs;
            ChaosMeta.State.SeenChaperone = seen;
            ChaosMeta.State.DiscoveredCodexIds.Clear();
            ChaosMeta.State.DiscoveredCodexIds.UnionWith(discovered);
            ChaosSkiaFxOverlay.Composited = composited;
            ChaosSkiaFxOverlay.SkipPlatformChecksForTests = false;
            settings.ChaosSkiaFxEnabled = enabled;
            settings.ChaosAnnouncerEnabled = announcer;
            LocalizationManager.Instance.SetLanguage(language);
        }
        Assert.True(discovered.SetEquals(ChaosMeta.State.DiscoveredCodexIds));
        return Task.CompletedTask;
    });
    [Fact]
    public Task Shield_ring_renders_only_while_escort_stands() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var saved = BubbleOverlay.Field.Bubbles.ToArray();
        var live = new AmbientBubble { Screen = 0, X = 300, Y = 200, Size = 200,
            Spec = new EffectBubbleSpec { IsLive = true, IsChaperoneLive = true } };
        var escort = new AmbientBubble { Screen = 1, Spec = new EffectBubbleSpec { IsEscort = true } };
        live.Escort = escort;
        BubbleOverlay.Field.Bubbles.Clear();
        BubbleOverlay.Field.Bubbles.Add(live);
        BubbleOverlay.Field.Bubbles.Add(escort);
        var window = new BubbleOverlayWindow(0, new PixelRect(0, 0, 800, 600), 1) { Width = 800, Height = 600 };
        try
        {
            window.Show(); window.Sync();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            var shielded = window.CaptureRenderedFrame()!;
            escort.Popping = true;
            window.Sync(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            var unshielded = window.CaptureRenderedFrame()!;
            int brighter = 0;
            using (var a = shielded.Lock())
            using (var b = unshielded.Lock())
                for (int y = 190; y <= 410; y++)
                    for (int x = 290; x <= 510; x++)
                        if (System.Runtime.InteropServices.Marshal.ReadByte(a.Address + y * a.RowBytes + x * 4 + 3) >
                            System.Runtime.InteropServices.Marshal.ReadByte(b.Address + y * b.RowBytes + x * 4 + 3)) brighter++;
            Assert.True(brighter > 30, $"shield pixels: {brighter}");
        }
        finally
        {
            window.Close();
            BubbleOverlay.Field.Bubbles.Clear();
            BubbleOverlay.Field.Bubbles.AddRange(saved);
        }
        return Task.CompletedTask;
    });

}
