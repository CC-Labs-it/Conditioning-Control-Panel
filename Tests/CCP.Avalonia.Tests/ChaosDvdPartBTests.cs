using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosDvdPartBTests
{
    private static void Setup()
    {
        if (Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    // WPF ChaosModeService.cs:2819-2831,2996-3004; ChaosDvdOverlay.cs:322-331,392-398.
    [Fact]
    public async Task EquippedThoughtsUsePoolCadencePauseCapAndRunReset()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx, ChaosEffectBannerOverlay.SkipPlatformChecksForTests);
            var pool = CoreSettings.Current.BouncingTextPool;
            var language = LocalizationManager.Instance.CurrentLanguage;
            var meta = ChaosMeta.State;
            var json = JsonConvert.SerializeObject(meta);
            try
            {
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };
                ChaosEffectBannerOverlay.SkipPlatformChecksForTests = true;
                CoreSettings.Current.BouncingTextPool = new()
                {
                    ["enabled"] = true,
                    ["disabled"] = false
                };
                meta.ActiveLifetimeBoons.Clear();
                meta.ActiveLifetimeBoons.Add("intrusive_thoughts");
                meta.LifetimeBoonLevels["intrusive_thoughts"] = 4;
                void Start()
                {
                    ChaosRunHost.StartRun(new ChaosRunConfig { DurationSec = 600, WaveCount = 3 });
                    ChaosRunHost.Overlay!.FinishCountdown();
                    ChaosRunHost.StopTimerForTests();
                }
                Start();
                Assert.True(ChaosRunHost.State!.IntrusiveThoughtsSec > 0);
                for (int i = 0; i < 19; i++)
                    ChaosRunHost.Tick();
                Assert.False(ChaosDvdOverlay.AnyActive);
                ChaosRunHost.ToggleManualPause();
                for (int i = 0; i < 20; i++)
                    ChaosRunHost.Tick();
                Assert.False(ChaosDvdOverlay.AnyActive);
                ChaosRunHost.ToggleManualPause();
                ChaosRunHost.Tick();
                var logo = Assert.Single(ChaosDvdOverlay.Flock.Active);
                Assert.Equal("enabled", logo.Text);
                Assert.Equal(ChaosRunHost.State.IntrusiveThoughtsSec, logo.RemainingSec);
                Assert.Equal(0.8, logo.FontScale);
                Assert.Equal(230 * 1.3, Math.Sqrt(logo.Vx * logo.Vx + logo.Vy * logo.Vy), 6);
                Assert.True(logo.SplitOnRabbit);
                Assert.False(ChaosDvdOverlay.AnyToyActive);
                CoreSettings.Current.BouncingTextPool.Clear();
                for (int i = 0; i < 20; i++)
                    ChaosRunHost.Tick();
                Assert.Equal("GIVE IN", ChaosDvdOverlay.Flock.Active[1].Text);
                foreach (var (lang, expected) in new[] { ("en", "GIVE IN"), ("de", "GIB NACH"), ("es", "CEDE"), ("fr", "CÈDE"), ("it", "CEDI"),
                    ("ja", "身をゆだねて"), ("ko", "맡겨 봐"), ("pt-BR", "CEDA"), ("ru", "ПОДДАЙСЯ"), ("zh-CN", "放下抵抗") })
                {
                    LocalizationManager.Instance.SetLanguage(lang);
                    Assert.Equal(expected, Loc.Get("chaos_thought_fallback"));
                    for (int i = 0; i < 20; i++)
                        ChaosRunHost.Tick();
                    Assert.Equal(Loc.Get("chaos_thought_fallback"), ChaosDvdOverlay.Flock.Active[^1].Text);
                    Assert.NotEqual("chaos_thought_fallback", Loc.Get("chaos_thought_fallback"));
                    ChaosDvdOverlay.CloseActive();
                }
                for (int i = 0; i < 200; i++)
                    ChaosRunHost.Tick();
                Assert.Equal(8, ChaosDvdOverlay.Flock.Active.Count);
                for (int i = 0; i < 19; i++)
                    ChaosRunHost.Tick();
                ChaosRunHost.ForceShutdown();
                Assert.False(ChaosDvdOverlay.AnyActive);
                Start();
                ChaosRunHost.Tick();
                Assert.False(ChaosDvdOverlay.AnyActive); // prior partial interval cannot leak
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                LocalizationManager.Instance.SetLanguage(language);
                CoreSettings.Current.BouncingTextPool = pool;
                JsonConvert.PopulateObject(json, meta, new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace });
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx, ChaosEffectBannerOverlay.SkipPlatformChecksForTests) = saved;
            }
            return Task.CompletedTask;
        });
    }

    // WPF ChaosModeService.cs:2819-2831,2996-3004; ChaosDvdOverlay.cs:322-331,392-398.
    [Fact]
    public async Task RabbitContactSplitsOnceAndCloneCanSplitAgain()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var saved = (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx);
            var host = new Window();
            host.Show();
            try
            {
                ChaosDvdOverlay.CanOverlay = () => true;
                ChaosDvdOverlay.PrepareWindow = (_, _) => true;
                ChaosDvdOverlay.PlaySfx = (_, _) => { };
                BubbleOverlay.BeginChaos(host, _ => { });
                ChaosDvdOverlay.Launch(host, 5, 1, 0.8, text: "thought", splitOnRabbit: true);
                var logo = Assert.Single(ChaosDvdOverlay.Flock.Active);
                logo.X = 100;
                logo.Y = 100;
                logo.Vx = 10;
                logo.Vy = 0;
                var rabbit = BubbleOverlay.SpawnChaos(new EffectBubbleSpec { IsDarter = true });
                rabbit.X = 100;
                rabbit.Y = 100;
                rabbit.Size = 20;
                ChaosDvdOverlay.Advance(0.05);
                Assert.Equal(2, ChaosDvdOverlay.Flock.Active.Count);
                Assert.True(logo.SplitSpent);
                var clone = ChaosDvdOverlay.Flock.Active[1];
                Assert.False(clone.SplitSpent);
                Assert.Equal(6.95, logo.RemainingSec, 6);
                Assert.Equal(logo.RemainingSec, clone.RemainingSec);
                Assert.Equal(logo.Text, clone.Text);
                Assert.Equal(logo.X, clone.X);
                Assert.Equal(10, Math.Sqrt(clone.Vx * clone.Vx + clone.Vy * clone.Vy), 6);
                Assert.False(rabbit.Popping);
                ChaosDvdOverlay.Advance(0.05);
                Assert.Equal(3, ChaosDvdOverlay.Flock.Active.Count);
                Assert.Equal(6.9, logo.RemainingSec, 6); // parent cannot split again
            }
            finally
            {
                BubbleOverlay.EndChaos();
                ChaosDvdOverlay.CloseActive();
                host.Close();
                (ChaosDvdOverlay.CanOverlay, ChaosDvdOverlay.PrepareWindow, ChaosDvdOverlay.PlaySfx) = saved;
            }
            return Task.CompletedTask;
        });
    }

}
