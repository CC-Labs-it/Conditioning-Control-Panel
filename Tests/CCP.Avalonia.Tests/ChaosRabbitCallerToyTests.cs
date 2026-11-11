using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Localization;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosRabbitCallerToyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task WhistleSwallowsArmingPressHoldsDuringPauseAndDisarmsOnTeardown(bool endRun) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var read = ChaosRunHost.ReadToyPointer;
            var glowRead = ChaosCursorGlowOverlay.ReadPointer;
            var make = ChaosCursorGlowOverlay.MakeClickThrough;
            var audio = ConditioningControlPanel.CoreAudio.PlayOneShotProvider;
            var cues = new System.Collections.Generic.List<string>();
            ConditioningControlPanel.CoreAudio.PlayOneShotProvider = (path, _, _, _, _) => cues.Add(System.IO.Path.GetFileNameWithoutExtension(path));
            var meta = ChaosMeta.State;
            var saved = Newtonsoft.Json.JsonConvert.SerializeObject(meta);
            bool down = true, available = true;
            var point = new PixelPoint(500, 400);
            ChaosRunHost.ReadToyPointer = () => available ? (point, down, false) : null;
            ChaosCursorGlowOverlay.ReadPointer = () => (point, down, false);
            ChaosCursorGlowOverlay.MakeClickThrough = _ => true;
            try
            {
                meta.ActiveLifetimeBoons.Clear();
                meta.ActiveLifetimeBoons.Add("rabbit_caller");
                meta.LifetimeBoonLevels["rabbit_caller"] = 2;
                meta.ToyPockets = 1;
                meta.FirstTimesAwarded.Remove(ChaosFirstTimes.Play);
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
                ChaosRunHost.Overlay!.FinishCountdown();
                ChaosRunHost.StopTimerForTests();
                var state = ChaosRunHost.State!;
                var toy = Assert.Single(state.ActiveToys);
                int count = Math.Max(1, (int)state.ToyPower[toy.Id]);
                int sparks = meta.Sparks;
                available = false;
                ChaosRunHost.UseToyById(toy.Id);
                Assert.False(ChaosRunHost.RabbitAimRunning);
                Assert.Equal(0, toy.CooldownRemainingSec);
                Assert.Equal(sparks, meta.Sparks);
                Assert.Contains($"{toy.Glyph} {Loc.Get("chaos_rabbit_pointer_unavailable")}", state.RecentEvents);
                available = true;
                ChaosRunHost.ToggleManualPause();
                ChaosRunHost.UseToyById(toy.Id);
                Assert.False(ChaosRunHost.RabbitAimRunning);
                Assert.Equal(sparks, meta.Sparks);
                ChaosRunHost.ToggleManualPause();
                ChaosRunHost.UseToyById(toy.Id);
                Assert.True(ChaosRunHost.RabbitAimRunning);
                Assert.Contains($"{toy.Glyph} {Loc.Get("chaos_rabbit_armed")}", state.RecentEvents);
                Assert.True(toy.IsEffectActive);
                Assert.True(ChaosCursorGlowOverlay.Active!.HaloVisible);
                Assert.Equal(sparks + 15, meta.Sparks);
                ChaosRunHost.UseToyById(toy.Id);
                Assert.Equal(sparks + 15, meta.Sparks);
                ChaosRunHost.RabbitAimTick();
                Assert.Empty(BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.IsDarter == true));
                down = false;
                ChaosRunHost.RabbitAimTick();
                ChaosRunHost.ToggleManualPause();
                down = true;
                ChaosRunHost.RabbitAimTick();
                Assert.True(ChaosRunHost.RabbitAimRunning);
                Assert.Empty(BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.IsDarter == true));
                ChaosRunHost.ToggleManualPause();
                down = false;
                ChaosRunHost.RabbitAimTick();
                down = true;
                ChaosRunHost.RabbitAimTick();
                var rabbits = BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.IsDarter == true).ToArray();
                Assert.Equal(count, rabbits.Length);
                Assert.Contains("rabbit_spawn", cues);
                Assert.Contains($"🐇 {Loc.GetF("chaos_rabbit_answered", count)}", state.RecentEvents);
                Assert.All(rabbits, b =>
                {
                    Assert.InRange(b.Spec!.SpawnAtPxX!.Value, point.X - 60, point.X + 60);
                    Assert.InRange(b.Spec.SpawnAtPxY!.Value, point.Y - 60, point.Y + 60);
                });
                Assert.False(ChaosRunHost.RabbitAimRunning);
                Assert.False(toy.IsEffectActive);
                Assert.False(ChaosCursorGlowOverlay.Active!.HaloVisible);
                toy.CooldownRemainingSec = 0;
                ChaosRunHost.UseToyById(toy.Id);
                Assert.True(ChaosRunHost.RabbitAimRunning);
                if (endRun) ChaosRunHost.RequestStop();
                else PanicSurfaces.All.Single(s => s.Id == "chaos").Stop(null);
                Assert.False(ChaosRunHost.RabbitAimRunning);
                Assert.True(ChaosCursorGlowOverlay.Active == null || !ChaosCursorGlowOverlay.Active.HaloVisible);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                ConditioningControlPanel.CoreAudio.PlayOneShotProvider = audio;
                ChaosRunHost.ReadToyPointer = read;
                ChaosCursorGlowOverlay.ReadPointer = glowRead;
                ChaosCursorGlowOverlay.MakeClickThrough = make;
                Newtonsoft.Json.JsonConvert.PopulateObject(saved, meta,
                    new Newtonsoft.Json.JsonSerializerSettings { ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace });
            }
            return Task.CompletedTask;
        });
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task MaxedStormTicksForTenSecondsAndEndsOnTeardown(bool panic) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var read = ChaosRunHost.ReadToyPointer;
        var make = ChaosCursorGlowOverlay.MakeClickThrough;
        var banner = ChaosEffectBannerOverlay.SkipPlatformChecksForTests;
        var meta = ChaosMeta.State;
        var saved = Newtonsoft.Json.JsonConvert.SerializeObject(meta);
        bool down = true;
        ChaosRunHost.ReadToyPointer = () => (new PixelPoint(500, 400), down, false);
        ChaosCursorGlowOverlay.MakeClickThrough = _ => true;
        ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
        try
        {
            meta.ActiveLifetimeBoons.Clear();
            meta.ActiveLifetimeBoons.Add("rabbit_caller");
            meta.LifetimeBoonLevels["rabbit_caller"] = 4;
            meta.ToyPockets = 1;
            ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 60, WaveCount = 3 });
            ChaosRunHost.Overlay!.FinishCountdown();
            ChaosRunHost.StopTimerForTests();
            var state = ChaosRunHost.State!;
            state.MaxedBoons.Add("rabbit_caller");
            var toy = Assert.Single(state.ActiveToys);
            ChaosRunHost.UseToyById(toy.Id);
            down = false; ChaosRunHost.RabbitAimTick();
            down = true; ChaosRunHost.RabbitAimTick();
            int initial = BubbleOverlay.Field.Bubbles.Count(b => b.Spec?.IsDarter == true);
            Assert.Contains($"🐇 {Loc.GetF("chaos_rabbit_answered_storm", initial)}", state.RecentEvents);
            var entry = Assert.Single(ChaosEffectBannerOverlay.Active!.Strip.Row);
            Assert.Equal(Loc.Get("chaos_rabbit_storm"), entry.Text);
            Assert.True(toy.IsEffectActive);
            ChaosRunHost.ToggleManualPause();
            for (int i = 0; i < 8; i++) ChaosRunHost.Tick();
            Assert.Equal(initial, BubbleOverlay.Field.Bubbles.Count(b => b.Spec?.IsDarter == true));
            ChaosRunHost.ToggleManualPause();
            for (int i = 1; i <= 40; i++)
            {
                ChaosRunHost.Tick();
                Assert.Equal(initial + i / 5, BubbleOverlay.Field.Bubbles.Count(b => b.Spec?.IsDarter == true));
                Assert.Equal(i < 40, toy.IsEffectActive);
            }
            Assert.NotNull(entry.EndedAt);
            toy.CooldownRemainingSec = 0;
            ChaosRunHost.UseToyById(toy.Id);
            down = false; ChaosRunHost.RabbitAimTick();
            down = true; ChaosRunHost.RabbitAimTick();
            Assert.True(toy.IsEffectActive);
            if (panic) PanicSurfaces.All.Single(s => s.Id == "chaos").Stop(null);
            else ChaosRunHost.RequestStop();
            Assert.False(toy.IsEffectActive);
            Assert.False(ChaosRunHost.RabbitAimRunning);
        }
        finally
        {
            ChaosRunHost.ForceShutdown();
            ChaosRunHost.ReadToyPointer = read;
            ChaosCursorGlowOverlay.MakeClickThrough = make;
            ChaosEffectBannerOverlay.SkipPlatformChecksForTests = banner;
            Newtonsoft.Json.JsonConvert.PopulateObject(saved, meta,
                new Newtonsoft.Json.JsonSerializerSettings { ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace });
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    [InlineData("zh-CN")]
    public void RabbitMessagesAreAvailableInEveryLanguage(string language)
    {
        var manager = LocalizationManager.Instance;
        var previous = manager.CurrentLanguage;
        try
        {
            manager.SetLanguage(language);
            foreach (var key in new[] { "chaos_rabbit_storm", "chaos_rabbit_armed", "chaos_rabbit_pointer_unavailable" })
                Assert.NotEqual(key, Loc.Get(key));
            Assert.Contains("3", Loc.GetF("chaos_rabbit_answered", 3));
            Assert.Contains("3", Loc.GetF("chaos_rabbit_answered_storm", 3));
        }
        finally { manager.SetLanguage(previous); }
    }

}
