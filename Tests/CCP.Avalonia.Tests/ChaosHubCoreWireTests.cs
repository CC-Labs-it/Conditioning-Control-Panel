using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Input;
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
    [Theory]
    [InlineData("gates")]
    [InlineData("mantras")]
    [InlineData("codex")]
    [InlineData("presets")]
    [InlineData("tooltip")]
    [InlineData("purchase")]
    public async Task Hub_reads_reveals_mantras_codex_and_presets_from_core(string behavior)
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
                if (behavior == "gates")
                {
                    Assert.True(hub.FindControl<ToggleButton>("TabDiary")!.IsVisible);      // the Diary was bought
                    Assert.False(hub.FindControl<Border>("CardMantras")!.IsVisible);       // the start mantra was not
                }

                if (behavior == "mantras")
                {
                    var mantras = Texts(hub.FindControl<StackPanel>("MantrasHost")!);
                    Assert.Contains("Snap Chain", mantras);                                // met in a draft
                    Assert.DoesNotContain("Golden Touch", mantras);                        // not met: ???
                    Assert.Equal(ChaosBoonPool.All.Count - 1, mantras.Count(t => t == "???"));
                }

                if (behavior == "codex")
                {
                    var diary = Texts(hub.FindControl<StackPanel>("DiaryHost")!);
                    Assert.Contains(ChaosBubbleVariants.DescriptionFor("flash"), diary);
                    Assert.DoesNotContain("White Rabbit", diary);
                    Assert.Equal(ChaosBubbleVariants.All.Count + 7 - 1, diary.Count(t => t == "???"));
                }

                if (behavior == "presets")
                {
                    // Core's Tease mix carries bambifreeze; Flash-only carries subliminal (the old stand-ins did not).
                    var pool = hub.FindControl<WrapPanel>("GrpPool")!.Children.OfType<ToggleButton>()
                        .ToDictionary(t => t.Tag!.ToString()!);
                    Preset(hub, "Tease");
                    Assert.Equal((true, false), (pool["bambifreeze"].IsChecked == true, pool["braindrain"].IsChecked == true));
                    Preset(hub, "Flash-only");
                    Assert.Equal((true, false), (pool["subliminal"].IsChecked == true, pool["spiral"].IsChecked == true));
                }

                if (behavior == "tooltip")
                {
                    Assert.True(ToolTip.GetShowOnDisabled(hub.FindControl<ToggleButton>("SegExtreme")!));
                    int cost = ChaosUpgrades.ById("extreme_tier")!.Cost;
                    Assert.Equal("a deeper door. she sells the key in the Toybox: "
                        + $"finish {ChaosLessons.T_EXTREME_TIER} relentless descents, reach Devoted "
                        + $"({ChaosRanks.Thresholds[(int)ChaosRank.Devoted]} descents), then train it for \u2726{cost}.",
                        ToolTip.GetTip(hub.FindControl<ToggleButton>("SegExtreme")!));
                }

                if (behavior == "purchase")
                {
                    // A save that never bought the Diary is not offered the tab.
                    ChaosMetaStore.Save(new ChaosMetaState { Gold = ChaosBench.GOLD_DIARY }, 1);
                    bare = new ChaosHubWindow();
                    Assert.False(bare.FindControl<ToggleButton>("TabDiary")!.IsVisible);
                    bare.FindControl<ToggleButton>("TabDiary")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(bare.FindControl<ScrollViewer>("PanelLoadout")!.IsVisible);
                    Assert.False(bare.FindControl<ScrollViewer>("PanelDiary")!.IsVisible);
    
                    // Buying it on the bench re-applies the gates at once (WPF Bench.cs:223 ApplyReveals).
                    bare.FindControl<StackPanel>("ImprovementsHost")!.GetLogicalDescendants().OfType<Button>()
                        .First(b => b.Tag as string == BenchIds.Diary)
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.True(bare.FindControl<ToggleButton>("TabDiary")!.IsVisible);
                }

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

    [Fact]
    public async Task Mantra_clicks_persist_toggle_but_unseen_and_curses_do_not()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var normal = ChaosBoonPool.All.First(b => !b.IsCurse);
            var curse = ChaosBoonPool.All.First(b => b.IsCurse);
            var unseen = ChaosBoonPool.All.First(b => !b.IsCurse && b.Id != normal.Id);
            ChaosMetaStore.Save(new ChaosMetaState
            {
                BenchPurchases = new HashSet<string> { BenchIds.StartMantra, BenchIds.Diary },
                DiscoveredCodexIds = new HashSet<string> { "boon:" + normal.Id, "boon:" + curse.Id, "bubble:darter" },
            }, 1);
            ChaosMetaStore.SetActiveSlot(1);
            var hub = new ChaosHubWindow();
            try
            {
                Border Row(ChaosBoon b) => (Border)hub.FindControl<StackPanel>("MantrasHost")!
                    .Children[ChaosBoonPool.All.IndexOf(b)];
                void Press(ChaosBoon b, bool left = true)
                {
                    var row = Row(b);
                    row.RaiseEvent(new PointerPressedEventArgs(row,
                        new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), hub, new Point(), 0,
                        new PointerPointProperties(left ? RawInputModifiers.LeftMouseButton : RawInputModifiers.RightMouseButton,
                            left ? PointerUpdateKind.LeftButtonPressed : PointerUpdateKind.RightButtonPressed), KeyModifiers.None, 1));
                }
                Press(normal, false);
                Assert.Null(ChaosMetaStore.Load(1).EquippedStartBoon);
                Press(normal);
                Assert.Equal(normal.Id, ChaosMetaStore.Load(1).EquippedStartBoon);
                Assert.Equal(3, Row(normal).BorderThickness.Left);
                Press(curse);
                Press(unseen);
                Assert.Equal(normal.Id, ChaosMetaStore.Load(1).EquippedStartBoon);
                Assert.Contains(curse.Name, Texts(Row(curse)));
                Assert.DoesNotContain(unseen.Name, Texts(Row(unseen)));
                Press(normal);
                Assert.Null(ChaosMetaStore.Load(1).EquippedStartBoon);
                Assert.Equal(2, Row(normal).BorderThickness.Left);
                var diary = Texts(hub.FindControl<StackPanel>("DiaryHost")!);
                Assert.Contains("White Rabbit", diary);
                Assert.Contains(ChaosBubbleVariants.DescriptionFor("darter"), diary);
                Assert.DoesNotContain("Lucky Bubble", diary);
                Assert.Contains("hold to snap", diary); // verbs are always readable
            }
            finally { hub.Close(); }
            return Task.CompletedTask;
        });
    }

    private static void Preset(Control root, string tag) =>
        root.GetLogicalDescendants().OfType<Button>().First(b => b.Tag as string == tag)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static List<string> Texts(Control root) =>
        root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToList();
}
