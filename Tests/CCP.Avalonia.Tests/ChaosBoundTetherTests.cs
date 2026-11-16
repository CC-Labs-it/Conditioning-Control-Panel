using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosBoundTetherTests
{
    [Fact]
    public async Task Pair_draws_tracks_resolves_and_clears_on_panic()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var clock = BubbleOverlay.Field.Clock;
            var now = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);
            try
            {
                BubbleOverlay.Field.Clock = () => now;
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = true;
                BubbleOverlay.BeginChaos(new Border(), _ => { }, _ => { }, _ => { });
                var (sa, sb) = ChaosBubbleVariants.BuildBoundPair(1);
                var (a, b) = BubbleOverlay.SpawnBoundPair(sa, sb);
                var prims = new List<FieldFxPrim>();
                ChaosFieldFxOverlay.Active!.Model.Frame(0, prims);
                Assert.Single(prims.Where(p => p.Kind == FieldFxKind.Tether));
                a.X += 30;
                BubbleOverlay.TickBoundPairs();
                var old = Assert.Single(prims.Where(p => p.Kind == FieldFxKind.Tether));
                ChaosFieldFxOverlay.Active!.Model.Frame(0, prims);
                Assert.NotEqual(old.X, Assert.Single(prims.Where(p => p.Kind == FieldFxKind.Tether)).X);
                Assert.True(a.Snap());
                BubbleOverlay.Field.ChaosDefused!(a);
                ChaosFieldFxOverlay.Active!.Model.Frame(0, prims);
                Assert.DoesNotContain(prims, p => p.Kind == FieldFxKind.Tether);
                double fuse = b.FuseRemainingMs;
                now = now.AddMilliseconds(2501);
                BubbleOverlay.TickBoundPairs();
                Assert.Equal(fuse / 2, b.FuseRemainingMs);
                var (sc, sd) = ChaosBubbleVariants.BuildBoundPair(1);
                BubbleOverlay.SpawnBoundPair(sc, sd);
                BubbleOverlay.EndChaos();
                ChaosFieldFxOverlay.Active!.Model.Frame(0, prims);
                Assert.DoesNotContain(prims, p => p.Kind == FieldFxKind.Tether);
            }
            finally
            {
                BubbleOverlay.EndChaos();
                ChaosFieldFxOverlay.CloseActive();
                ChaosFieldFxOverlay.SkipPlatformChecksForTests = false;
                BubbleOverlay.Field.Clock = clock;
            }
            return Task.CompletedTask;
        });
    }
}
