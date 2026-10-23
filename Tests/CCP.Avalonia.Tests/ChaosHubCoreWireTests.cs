using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>chaos-hub-core-wire: the hub's Diary gate, mantras, codex, pool presets and the
/// Inescapable tooltip read Core (RevealService, ChaosBoonPool, ChaosBubbleVariants,
/// ChaosLessons/ChaosRanks) instead of head stand-ins (WPF ChaosHubWindow.xaml.cs:174, :1262,
/// :1378, :1693, :1588).</summary>
public sealed class ChaosHubCoreWireTests
{
    [Fact]
    public async Task Hub_reads_reveals_mantras_codex_and_presets_from_core()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var old = CoreSettings.Current.ChaosActiveSlot;
            ChaosHubWindow? hub = null, bare = null;
            try
            {
                for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
                ChaosMetaStore.Save(new ChaosMetaState
                {
                    BenchPurchases = new HashSet<string> { BenchIds.Diary },
                    DiscoveredCodexIds = new HashSet<string> { "boon:defuse_chain", "bubble:flash" },
                }, 1);
                ChaosMetaStore.SetActiveSlot(1);

                hub = new ChaosHubWindow();
                Assert.True(hub.FindControl<ToggleButton>("TabDiary")!.IsVisible);      // the Diary was bought
                Assert.False(hub.FindControl<Border>("CardMantras")!.IsVisible);       // the start mantra was not

                var mantras = Texts(hub.FindControl<StackPanel>("MantrasHost")!);
                Assert.Contains("Snap Chain", mantras);                                // met in a draft
                Assert.DoesNotContain("Golden Touch", mantras);                        // not met: ???
                Assert.Equal(ChaosBoonPool.All.Count - 1, mantras.Count(t => t == "???"));

                var diary = Texts(hub.FindControl<StackPanel>("DiaryHost")!);
                Assert.Contains(ChaosBubbleVariants.DescriptionFor("flash"), diary);
                Assert.DoesNotContain("White Rabbit", diary);
                Assert.Equal(ChaosBubbleVariants.All.Count + 7 - 1, diary.Count(t => t == "???"));

                // Core's Tease mix carries bambifreeze; Flash-only carries subliminal (the old stand-ins did not).
                var pool = hub.FindControl<WrapPanel>("GrpPool")!.Children.OfType<ToggleButton>()
                    .ToDictionary(t => t.Tag!.ToString()!);
                Preset(hub, "Tease");
                Assert.Equal((true, false), (pool["bambifreeze"].IsChecked == true, pool["braindrain"].IsChecked == true));
                Preset(hub, "Flash-only");
                Assert.Equal((true, false), (pool["subliminal"].IsChecked == true, pool["spiral"].IsChecked == true));

                int cost = ChaosUpgrades.ById("extreme_tier")!.Cost;
                Assert.Equal("a deeper door. she sells the key in the Toybox: "
                    + $"finish {ChaosLessons.T_EXTREME_TIER} relentless descents, reach Devoted "
                    + $"({ChaosRanks.Thresholds[(int)ChaosRank.Devoted]} descents), then train it for \u2726{cost}.",
                    ToolTip.GetTip(hub.FindControl<ToggleButton>("SegExtreme")!));

                // A save that never bought the Diary is not offered the tab.
                ChaosMetaStore.Save(new ChaosMetaState(), 1);
                bare = new ChaosHubWindow();
                Assert.False(bare.FindControl<ToggleButton>("TabDiary")!.IsVisible);
            }
            finally
            {
                hub?.Close();
                bare?.Close();
                for (int s = 1; s <= 3; s++) ChaosMetaStore.Delete(s);
                CoreSettings.Current.ChaosActiveSlot = old;
                ChaosMeta.Init();
            }
            return Task.CompletedTask;
        });
    }

    private static void Preset(Control root, string tag) =>
        root.GetLogicalDescendants().OfType<Button>().First(b => b.Tag as string == tag)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static List<string> Texts(Control root) =>
        root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
}
