using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The run-pick ribbon from a real run: born empty at BeginRun (WPF ChaosModeService :417),
/// one tile per drafted pick in order with only newcomers popping in, closed at run end and on panic.</summary>
public sealed class ChaosBoonBarOverlayTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static ChaosRunConfig Cfg() => new() { DurationSec = 60, WaveCount = 3 };

    [Fact]
    public async Task Hub_only_equips_discovered_mantras_and_can_unequip_them()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var hub = new ChaosHubWindow();
            var host = new Window { Width = 500, Height = 150 };
            Assert.Equal(ChaosBoonPool.All.Count, hub.FindControl<StackPanel>("MantrasHost")!.Children.Count);
            var boon = ChaosBoonPool.All.First(b => !b.IsCurse);
            var curse = ChaosBoonPool.All.First(b => b.IsCurse);
            var meta = ChaosMeta.State;
            var equipped = meta.EquippedStartBoon;
            var discovered = meta.DiscoveredCodexIds.ToArray();
            void Click(ChaosBoon b)
            {
                host.Content = hub.MantraRow(b);
                host.Show();
                host.UpdateLayout();
                host.MouseDown(new Point(30, 30), global::Avalonia.Input.MouseButton.Left);
                host.MouseUp(new Point(30, 30), global::Avalonia.Input.MouseButton.Left);
            }
            try
            {
                meta.EquippedStartBoon = null;
                meta.DiscoveredCodexIds.Remove("boon:" + boon.Id);
                Click(boon);
                Assert.Null(meta.EquippedStartBoon);
                ChaosMeta.MarkDiscovered("boon:" + boon.Id);
                Click(boon);
                Assert.Equal(boon.Id, meta.EquippedStartBoon);
                Assert.Equal(boon.Id, ChaosMetaStore.Load().EquippedStartBoon);
                ChaosMeta.MarkDiscovered("boon:" + curse.Id);
                Click(curse);
                Assert.Equal(boon.Id, meta.EquippedStartBoon);
                Click(boon);
                Assert.Null(meta.EquippedStartBoon);
                Assert.Null(ChaosMetaStore.Load().EquippedStartBoon);
            }
            finally
            {
                host.Close();
                hub.Close();
                meta.EquippedStartBoon = equipped;
                meta.DiscoveredCodexIds.Clear();
                meta.DiscoveredCodexIds.UnionWith(discovered);
            }
            return Task.CompletedTask;
        });
    }

    // WPF ChaosModeService.cs:422-435, ChaosBoonBarOverlay.cs:179-192, ChaosArt.cs:58-69.
    [Fact]
    public async Task Equipped_boon_lands_with_cached_art_and_scripted_or_invalid_loadouts_stay_empty()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            ChaosRunState.IconResolver = null; // included in the suite's static seam restore
            var boon = ChaosBoonPool.All.Last(b => !b.IsCurse);
            var meta = ChaosMeta.State;
            var equipped = meta.EquippedStartBoon;
            var path = System.IO.Path.Combine(global::ConditioningControlPanel.CorePaths.UserData,
                "assets", "assets", "Chaos", "boons", boon.Id + ".png");
            var prior = System.IO.File.Exists(path) ? System.IO.File.ReadAllBytes(path) : null;
            try
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                using (var bitmap = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(2, 2),
                    new Vector(96, 96), global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul))
                    bitmap.Save(path);
                meta.EquippedStartBoon = boon.Id;
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                var pick = Assert.Single(ChaosRunHost.State!.RunPickTiles);
                Assert.Equal(boon.Id, pick.Id);
                Assert.Contains(ChaosRunHost.State.ActiveBoons, b => b.Id == boon.Id);
                var tile = (Border)Assert.Single(ChaosRunHost.BoonBar!.Row.Children);
                var image = Assert.IsType<Image>(((Panel)tile.Child!).Children[0]);
                Assert.Same(pick.Icon, image.Source);
                Assert.NotNull(image.Source);
                Assert.Same(image.Source, ChaosEffectBannerOverlay.ResolveArt(boon.Id, "boons"));
                Assert.Contains("boon:" + boon.Id, meta.DiscoveredCodexIds);
                ChaosRunHost.ForceShutdown();

                var scripted = Cfg();
                scripted.ScriptedFirstRun = true;
                ChaosRunHost.StartRun(scripted);
                ChaosRunHost.Overlay!.FinishCountdown();
                Assert.Empty(ChaosRunHost.State!.RunPickTiles);
                Assert.False(ChaosRunHost.BoonBar!.Pill.IsVisible);
                ChaosRunHost.ForceShutdown();

                meta.EquippedStartBoon = "unknown-boon";
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                Assert.Empty(ChaosRunHost.State!.RunPickTiles);
                Assert.Null(ChaosEffectBannerOverlay.ResolveArt("unknown-boon", "boons"));
                ChaosRunHost.State.ApplyBoon(ChaosBoonPool.All.First(b => b.IsCurse));
                var fallback = (Border)Assert.Single(ChaosRunHost.BoonBar!.Row.Children);
                Assert.IsType<global::ConditioningControlPanel.Avalonia.Controls.IconGlyph>(((Panel)fallback.Child!).Children[0]);
            }
            finally
            {
                ChaosRunHost.ForceShutdown();
                meta.EquippedStartBoon = equipped;
                if (prior == null) System.IO.File.Delete(path); else System.IO.File.WriteAllBytes(path, prior);
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Picks_land_live_in_order_and_panic_closes_the_ribbon()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            try
            {
                ChaosRunHost.StartRun(Cfg());
                Assert.Null(ChaosRunHost.BoonBar);   // countdown: not yet
                ChaosRunHost.Overlay!.FinishCountdown();
                var bar = ChaosRunHost.BoonBar!;
                Assert.True(bar.IsVisible);
                Assert.False(bar.Pill.IsVisible);    // no picks: pill collapsed

                var state = ChaosRunHost.State!;
                var boon = ChaosBoonPool.All.First(b => !b.IsCurse);
                var curse = ChaosBoonPool.All.First(b => b.IsCurse);
                state.ApplyBoon(boon);
                Assert.True(bar.Pill.IsVisible);
                Assert.Equal(0, Assert.Single(bar.Row.Children).Opacity);   // newcomer pops in from 0
                bar.StepPops(1000);
                Assert.Equal(1, bar.Row.Children[0].Opacity);

                state.ApplyBoon(curse);
                Assert.Equal(2, bar.Row.Children.Count);
                Assert.Equal(1, bar.Row.Children[0].Opacity);   // already shown: not re-animated
                Assert.Equal(0, bar.Row.Children[1].Opacity);
                Assert.Equal(new[] { boon.Name, curse.Name },
                    bar.Row.Children.Select(c => ((TextBlock)((StackPanel)ToolTip.GetTip(c)!).Children[0]).Text));

                ChaosRunHost.ForceShutdown();   // the 'chaos' panic surface
                Assert.Null(ChaosRunHost.BoonBar);
                Assert.False(bar.IsVisible);
            }
            finally { ChaosRunHost.ForceShutdown(); }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Run_end_closes_the_ribbon()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            bool pin = global::ConditioningControlPanel.CoreSettings.Current.ChaosPinOnTop;
            try
            {
                global::ConditioningControlPanel.CoreSettings.Current.ChaosPinOnTop = false;   // WPF BornTopmost follows the pin
                ChaosRunHost.StartRun(Cfg());
                ChaosRunHost.Overlay!.FinishCountdown();
                var bar = ChaosRunHost.BoonBar!;
                Assert.False(bar.Topmost);
                ChaosRunHost.RequestStop();   // WPF EndRun :3235
                Assert.Null(ChaosRunHost.BoonBar);
                Assert.False(bar.IsVisible);
            }
            finally { ChaosRunHost.ForceShutdown(); global::ConditioningControlPanel.CoreSettings.Current.ChaosPinOnTop = pin; }
            return Task.CompletedTask;
        });
    }
}
