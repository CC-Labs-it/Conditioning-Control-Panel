using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosDvdToyTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        ChaosDvdOverlay.CanOverlay = () => true;
        ChaosDvdOverlay.PrepareWindow = (_, _) => true;
        ChaosDvdOverlay.PlaySfx = (_, _) => { };
        ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
    }

    // WPF ChaosModeService.cs:2684-2697,2722-2734,2780,3020-3027.
    [Theory]
    [InlineData(1, 0.7, 0.8, false, 10, "en", "Porn DVD", "📀 now loading")]
    [InlineData(2, 0.85, 0.9, false, 15, "de", "Porno-DVD", "📀 wird geladen")]
    [InlineData(4, 1.0, 1.0, true, 20, "ja", "ポルノDVD", "📀 読み込み中")]
    public async Task EquippedDvdKeyLaunchesWithRankAndCapstoneThenRetires(int level, double speed, double scale, bool maxed, double duration, string language, string title, string loading)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx,
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests, ConditioningControlPanel.CoreAudio.PlayOneShotProvider);
            var oldClock = ChaosEffectBannerOverlay.Clock;
            var savedLanguage = LocalizationManager.Instance.CurrentLanguage;
            Setup();
            var clock = new Clock();
            ChaosEffectBannerOverlay.Clock = clock;
            var meta = ChaosMeta.State;
            var savedMeta = Newtonsoft.Json.JsonConvert.SerializeObject(meta);
            meta.ActiveLifetimeBoons.Clear();
            meta.ActiveLifetimeBoons.Add("porn_dvd");
            meta.LifetimeBoonLevels["porn_dvd"] = level;
            meta.ToyPockets = 1;
            meta.FirstTimesAwarded.Remove(ChaosFirstTimes.Play);
            try
            {
                LocalizationManager.Instance.SetLanguage(language);
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                var state = ChaosRunHost.State!;
                state.DvdSplitBounces = 2;
                var toy = Assert.Single(state.ActiveToys);
                if (maxed) state.MaxedBoons.Add("porn_dvd");
                else state.MaxedBoons.Remove("porn_dvd");
                int sparks = meta.Sparks;
                var cues = new System.Collections.Generic.List<string>();
                ConditioningControlPanel.CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(System.IO.Path.GetFileNameWithoutExtension(path));
                ChaosRunHost.ToggleManualPause();
                ChaosRunHost.UseToyById(toy.Id);
                Assert.False(ChaosDvdOverlay.AnyActive);
                Assert.Equal(0, toy.CooldownRemainingSec);
                ChaosRunHost.ToggleManualPause();
                state.ActivesDisabled = true;
                ChaosRunHost.UseToyById(toy.Id);
                Assert.False(ChaosDvdOverlay.AnyActive);
                state.ActivesDisabled = false;

                ChaosRunHost.OnToyKey(toy.KeyLabel);
                Assert.Equal(maxed ? 2 : 1, ChaosDvdOverlay.Flock.Active.Count);
                foreach (var logo in ChaosDvdOverlay.Flock.Active)
                {
                    Assert.Equal(duration, logo.RemainingSec);
                    Assert.Equal(scale, logo.FontScale);
                    Assert.Equal(230 * speed, Math.Sqrt(logo.Vx * logo.Vx + logo.Vy * logo.Vy), 6);
                    Assert.Equal(2, logo.SplitBouncesLeft);
                    logo.SplitBouncesLeft = 0; // lifetime assertion below does not need wall twins
                }
                Assert.Contains("dvd_launch", cues);
                Assert.Contains(loading, state.RecentEvents);
                var feed = ConditioningControlPanel.Avalonia.Controls.IconText.Split(loading);
                Assert.Equal(toy.Glyph, Assert.Single(feed.Icons));
                Assert.Equal(feed.Rest, Loc.Get("chaos_dvd_loading"));
                Assert.Equal(title, Assert.Single(ChaosEffectBannerOverlay.Active!.Strip.Row).Text);
                Assert.Equal(60, toy.CooldownRemainingSec);
                Assert.True(toy.IsEffectActive);
                Assert.Single(ChaosEffectBannerOverlay.Active!.Row.Children);
                Assert.Equal(sparks + 15, meta.Sparks);
                ChaosRunHost.UseToyById(toy.Id); // cooling: cannot add logos or award again
                Assert.Equal(maxed ? 2 : 1, ChaosDvdOverlay.Flock.Active.Count);
                Assert.Equal(sparks + 15, meta.Sparks);
                ChaosRunHost.Tick();
                Assert.True(toy.IsEffectActive);
                for (int i = 0; i < (duration + 1) * 20; i++) ChaosDvdOverlay.Advance(0.05);
                clock.Now += TimeSpan.FromSeconds(duration + 1);
                ChaosRunHost.Tick();
                Assert.False(toy.IsEffectActive);
                clock.Now += TimeSpan.FromSeconds(1);
                ChaosEffectBannerOverlay.Active!.Step();
                Assert.Empty(ChaosEffectBannerOverlay.Active.Row.Children);
                ChaosRunHost.ForceShutdown();
                Assert.Null(ChaosDvdOverlay.Window);
                Assert.False(ChaosDvdOverlay.AnyActive);
            }
            finally
            {
                ChaosRunHost.ForceShutdown(); ChaosDvdOverlay.CloseActive();
                ChaosEffectBannerOverlay.Clock = oldClock;
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx,
                    ChaosEffectBannerOverlay.SkipPlatformChecksForTests, ConditioningControlPanel.CoreAudio.PlayOneShotProvider) = saved;
                LocalizationManager.Instance.SetLanguage(savedLanguage);
                Newtonsoft.Json.JsonConvert.PopulateObject(savedMeta, meta,
                    new Newtonsoft.Json.JsonSerializerSettings { ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace });
            }
            return Task.CompletedTask;
        });
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task ConsecutiveToyCasesRestoreOverriddenGlobals()
    {
        var clock = ChaosEffectBannerOverlay.Clock;
        var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx,
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests, ConditioningControlPanel.CoreAudio.PlayOneShotProvider);
        var language = LocalizationManager.Instance.CurrentLanguage;
        for (int i = 0; i < 2; i++)
        {
            await EquippedDvdKeyLaunchesWithRankAndCapstoneThenRetires(2, 0.85, 0.9, false, 15, "de", "Porno-DVD", "📀 wird geladen");
            Assert.Same(clock, ChaosEffectBannerOverlay.Clock);
            Assert.Equal(saved, (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx,
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests, ConditioningControlPanel.CoreAudio.PlayOneShotProvider));
            Assert.Equal(language, LocalizationManager.Instance.CurrentLanguage);
        }
    }

    // WPF ChaosDvdOverlay.cs:388-390 and BubbleService.cs:325-340.
    [Fact]
    public async Task MovingLogoPaysTreatAndLiveContactsButRespectsPauseExemptionsAndRetirement()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx,
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests);
            Setup();
            var host = new global::Avalonia.Controls.Window();
            host.Show();
            int pops = 0, snaps = 0;
            try
            {
                BubbleOverlay.BeginChaos(host, _ => pops++, _ => snaps++);
                ChaosDvdOverlay.Launch(host, 5, 1, 1);
                var logo = Assert.Single(ChaosDvdOverlay.Flock.Active);
                logo.X = 100; logo.Y = 100; logo.Vx = logo.Vy = 0;
                var treat = BubbleOverlay.SpawnChaos(new EffectBubbleSpec());
                var live = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsLive = true, FuseMs = 5000 });
                var rabbit = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsDarter = true });
                var freeze = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsFreeze = true });
                var tease = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsTease = true });
                var brittle = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsBrittle = true });
                var far = BubbleOverlay.SpawnChaos(new EffectBubbleSpec());
                foreach (var b in BubbleOverlay.Field.Bubbles) { b.X = 100; b.Y = 100; b.Size = 20; }
                far.X = 900;
                BubbleOverlay.HoldChaos(true);
                ChaosDvdOverlay.Advance(0.05);
                Assert.Equal(0, pops + snaps);
                BubbleOverlay.HoldChaos(false);
                ChaosDvdOverlay.Advance(0.05);
                Assert.Equal(1, pops);
                Assert.Equal(1, snaps);
                Assert.True(live.Deflating);
                Assert.False(rabbit.Popping || freeze.Popping || tease.Popping || brittle.Popping || far.Popping);
                ChaosDvdOverlay.Advance(0.05);
                Assert.Equal(2, pops + snaps); // no duplicate rewards
                far.X = 100;
                logo.RemainingSec = 0;
                ChaosDvdOverlay.Advance(0.05);
                Assert.False(far.Popping); // fading logo has no collider
            }
            finally
            {
                BubbleOverlay.EndChaos(); ChaosDvdOverlay.CloseActive(); host.Close();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx,
                    ChaosEffectBannerOverlay.SkipPlatformChecksForTests) = saved;
            }
            return Task.CompletedTask;
        });
    }
}
