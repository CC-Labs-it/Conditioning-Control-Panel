using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosThoughtFieldTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RabbitsReachMaxedThoughts(bool enabled)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var saved = (ChaosDvdOverlay.SharedHost, ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx);
            var meta = ChaosMeta.State;
            var json = JsonConvert.SerializeObject(meta);
            try
            {
                ChaosDvdOverlay.SharedHost = () => true;
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };
                meta.ActiveLifetimeBoons.Clear();
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 600, WaveCount = 3, DartersEnabled = enabled });
                ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
                var state = ChaosRunHost.State!;
                state.RabbitRateMult = 1000; state.IntrusiveThoughtsSec = 5;
                state.MaxedBoons.Add("intrusive_thoughts");
                ChaosRunHost.SpawnTick();
                var rabbits = BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.IsDarter == true).ToList();
                Assert.Equal(enabled ? 1 : 0, rabbits.Count);
                for (int i = 0; i < 20; i++) ChaosRunHost.Tick();
                var thought = Assert.Single(ChaosDvdOverlay.Flock.Active);
                Assert.True(thought.SplitOnRabbit);
                if (enabled)
                {
                    var rabbit = rabbits[0]; rabbit.X = thought.X; rabbit.Y = thought.Y;
                    thought.Vx = thought.Vy = 0;
                    ChaosDvdOverlay.Advance(0.01);
                    Assert.Equal(2, ChaosDvdOverlay.Flock.Active.Count);
                    for (int i = 0; i < 20; i++) ChaosDvdOverlay.Advance(0.01);
                    Assert.Equal(8, ChaosDvdOverlay.Flock.Active.Count);
                    var score = state.Score;
                    BubbleOverlay.PopChaos(rabbit);
                    Assert.True(state.Score > score);
                }
            }
            finally
            {
                ChaosRunHost.ForceShutdown(); ChaosDvdOverlay.CloseActive();
                JsonConvert.PopulateObject(json, meta, new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
                (ChaosDvdOverlay.SharedHost, ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx) = saved;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task PickupAndToyHoldThoughtsAndTeardownClearsFreeze()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var saved = (ChaosDvdOverlay.SharedHost, ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx);
            var meta = ChaosMeta.State; var json = JsonConvert.SerializeObject(meta);
            try
            {
                ChaosDvdOverlay.SharedHost = () => true; ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true; ChaosDvdOverlay.PlaySfx = (_, _) => { };
                meta.ActiveLifetimeBoons.Clear(); meta.ActiveLifetimeBoons.Add("freeze_trigger");
                meta.LifetimeBoonLevels["freeze_trigger"] = 2; meta.ToyPockets = 1;
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 600, WaveCount = 3, DartersEnabled = false, EnabledVariants = new() { "bambifreeze" } });
                ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
                ChaosRunHost.State!.IntrusiveThoughtsSec = 5;
                for (int i = 0; i < 19; i++) ChaosRunHost.Tick();
                ChaosRunHost.SpawnTick();
                var pickup = Assert.Single(BubbleOverlay.Field.Bubbles.Where(b => b.Spec?.IsFreeze == true));
                BubbleOverlay.PopChaos(pickup);
                Assert.True(BubbleOverlay.Field.FreezeChaos);
                Assert.Equal(2.5, EffectPayload.GlobalDurationMult);
                int count = BubbleOverlay.Field.Bubbles.Count;
                ChaosRunHost.SpawnTick(); Assert.Equal(count, BubbleOverlay.Field.Bubbles.Count);
                for (int i = 0; i < 13; i++) ChaosRunHost.Tick();
                Assert.False(ChaosDvdOverlay.AnyActive);
                ChaosRunHost.Tick(); Assert.Single(ChaosDvdOverlay.Flock.Active);
                ChaosDvdOverlay.CloseActive();
                for (int i = 0; i < 19; i++) ChaosRunHost.Tick();
                ChaosRunHost.UseToyById("freeze_trigger"); Assert.True(BubbleOverlay.Field.FreezeChaos);
                for (int i = 0; i < 13; i++) ChaosRunHost.Tick();
                Assert.False(ChaosDvdOverlay.AnyActive);
                ChaosRunHost.Tick(); Assert.Single(ChaosDvdOverlay.Flock.Active);
                ChaosRunHost.UseToyById("freeze_trigger"); Assert.True(BubbleOverlay.Field.FreezeChaos);
                ChaosRunHost.ForceShutdown(); Assert.False(BubbleOverlay.Field.FreezeChaos);
                Assert.Equal(1.0, EffectPayload.GlobalDurationMult);
                ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 600, WaveCount = 3, DartersEnabled = false });
                ChaosRunHost.Overlay!.FinishCountdown(); ChaosRunHost.StopTimerForTests();
                ChaosRunHost.State!.IntrusiveThoughtsSec = 5; ChaosRunHost.Tick();
                Assert.False(ChaosDvdOverlay.AnyActive);
            }
            finally
            {
                ChaosRunHost.ForceShutdown(); ChaosDvdOverlay.CloseActive();
                JsonConvert.PopulateObject(json, meta, new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
                (ChaosDvdOverlay.SharedHost, ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx) = saved;
            }
            return Task.CompletedTask;
        });
    }
}
