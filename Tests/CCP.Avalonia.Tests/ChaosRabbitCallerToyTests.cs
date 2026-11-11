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
            bool down = true;
            var point = new PixelPoint(500, 400);
            ChaosRunHost.ReadToyPointer = () => (point, down, false);
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
                ChaosRunHost.ToggleManualPause();
                ChaosRunHost.UseToyById(toy.Id);
                Assert.False(ChaosRunHost.RabbitAimRunning);
                Assert.Equal(sparks, meta.Sparks);
                ChaosRunHost.ToggleManualPause();
                ChaosRunHost.UseToyById(toy.Id);
                Assert.True(ChaosRunHost.RabbitAimRunning);
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
}
