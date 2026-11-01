using System;
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

public sealed class ChaosHubRevealTests
{
    // WPF Reveals.cs:54-128: gated surfaces and visual-only difficulty fallback.
    [Theory]
    [InlineData(0, false, false, false, false)]
    [InlineData(1, true, false, false, false)]
    [InlineData(3, true, true, false, false)]
    [InlineData(10, true, true, false, true)]
    [InlineData(25, true, true, true, true)]
    public Task Reveal_gates_follow_rank_and_preserve_the_saved_difficulty(int runs, bool shelves, bool medium, bool hard, bool glass) => Run(() =>
    {
        ChaosMeta.State.RunsCompleted = runs;
        CoreSettings.Current.ChaosDifficulty = "Hard";
        using var hub = new Hub();
        var w = hub.Window;
        Assert.Equal(shelves, Part<Control>(w, "HdrToys").IsVisible);
        Assert.Equal(shelves, Part<Control>(w, "BoonHostSkills").IsVisible);
        Assert.Equal(shelves, Part<Control>(w, "CardBagToys").IsVisible);
        Assert.Equal(!shelves, Part<Control>(w, "StubToys").IsVisible);
        Assert.Equal(shelves, Part<Control>(w, "HdrAccessories").IsVisible);
        Assert.Equal(shelves, Part<Control>(w, "BoonHostAccessories").IsVisible);
        Assert.Equal(shelves, Part<Control>(w, "CardBagAccessories").IsVisible);
        Assert.Equal(!shelves, Part<Control>(w, "StubAccessories").IsVisible);
        Assert.Equal(medium, Part<ToggleButton>(w, "SegMedium").IsVisible);
        Assert.Equal(hard, Part<ToggleButton>(w, "SegHard").IsVisible);
        var glassTab = Part<ToggleButton>(w, "TabImprove");
        Assert.Equal(glass, glassTab.IsEnabled);
        Assert.Equal(glass ? 1.0 : 0.40, glassTab.Opacity);
        Assert.False(Part<Control>(w, "TabDiary").IsVisible);
        Assert.False(Part<Control>(w, "CardMantras").IsVisible);
        Assert.False(Part<Control>(w, "HdrStats").IsVisible);
        Assert.False(Part<Control>(w, "StatsGrid").IsVisible);
        Assert.False(Part<Control>(w, "CardKeybinds").IsVisible);
        if (!hard) Assert.True(Part<Panel>(w, "GrpDifficulty").Children.OfType<ToggleButton>().Single(t => Equals(t.Tag, "Easy")).IsChecked);
        Click(Part<Button>(w, "BtnBackToMenu"));
        Assert.Equal("Hard", CoreSettings.Current.ChaosDifficulty);
    });

    // WPF Bench.cs:207-231 and Reveals.cs:110-132: purchases open the surfaces immediately.
    [Fact]
    public Task Bench_purchases_refresh_gates_and_pocket_copy() => Run(() =>
    {
        ChaosMeta.State.RunsCompleted = 10;
        ChaosMeta.State.Gold = 1000;
        using var hub = new Hub();
        var w = hub.Window;
        foreach (var id in new[] { BenchIds.StartMantra, BenchIds.Diary, BenchIds.StatsPanel, BenchIds.ToyPocket1 })
            Click(Part<StackPanel>(w, "ImprovementsHost").GetLogicalDescendants().OfType<Button>().First(b => Equals(b.Tag, id)));
        Assert.True(Part<Control>(w, "CardMantras").IsVisible);
        Assert.True(Part<Control>(w, "TabDiary").IsVisible);
        Assert.True(Part<Control>(w, "HdrStats").IsVisible);
        Assert.True(Part<Control>(w, "StatsGrid").IsVisible);
        Assert.True(Part<Control>(w, "CardKeybinds").IsVisible);
        Assert.False(Part<Control>(w, "RowToyKey2").IsVisible);
        Assert.Contains("second pocket isn't sewn", Part<TextBlock>(w, "TxtKeybindsSub").Text);
        Assert.Equal("what you take down with you. choose like it matters.", Part<TextBlock>(w, "TxtPocketsSub").Text);
    });

    // WPF xaml.cs:468-482 and :753-764: lesson before rank, capstone before affordability.
    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    public Task Rank_floor_and_capstone_buttons_match_the_purchase_gate(int runs, bool allowed) => Run(() =>
    {
        ChaosMeta.State.RunsCompleted = runs;
        ChaosMeta.State.Sparks = 10000;
        ChaosMeta.State.LessonsComplete.Add("extreme_tier");
        ChaosMeta.State.LifetimeBoonLevels["vibe_popping"] = 3;
        using var hub = new Hub();
        var train = ButtonFor(hub.Window, "extreme_tier", "Train");
        var deepen = ButtonFor(hub.Window, "vibe_popping", "deepen");
        Assert.Equal(allowed, train.IsEnabled);
        Assert.Equal(allowed, deepen.IsEnabled);
        if (!allowed)
        {
            Assert.Contains(ChaosRanks.RankLockedTip, Assert.IsType<string>(ToolTip.GetTip(train)));
            Assert.Contains(ChaosRanks.CapstoneLockedTip, Assert.IsType<string>(ToolTip.GetTip(deepen)));
            Assert.True(ToolTip.GetShowOnDisabled(train));
            Assert.True(ToolTip.GetShowOnDisabled(deepen));
        }
    });

    // WPF xaml.cs:725-731 keeps the depth explanation available on the disabled button.
    [Fact]
    public Task Rank_locked_boon_button_exposes_its_tooltip() => Run(() =>
    {
        ChaosMeta.State.RunsCompleted = 0;
        using var hub = new Hub();
        var held = Assert.Single(hub.Window.GetLogicalDescendants().OfType<Button>(),
            b => Equals(b.Tag, "vibe_popping"));
        Assert.False(held.IsEnabled);
        Assert.True(ToolTip.GetShowOnDisabled(held));
        Assert.Contains("Slipping", Assert.IsType<string>(ToolTip.GetTip(held)));
    });

    // WPF xaml.cs:1259-1273, 1336-1341 and :1371-1395, :1494-1496.
    [Fact]
    public Task Mantras_and_diary_use_the_real_catalogues_and_discoveries() => Run(() =>
    {
        ChaosMeta.State.RunsCompleted = 10;
        ChaosMeta.State.BenchPurchases.UnionWith(new[] { BenchIds.StartMantra, BenchIds.Diary });
        ChaosMeta.State.DiscoveredCodexIds.UnionWith(new[] { "boon:defuse_chain", "boon:hair_trigger", "bubble:flash" });
        using var hub = new Hub();
        var mantras = Part<StackPanel>(hub.Window, "MantrasHost");
        Assert.Equal(ChaosBoonPool.All.Count, mantras.Children.Count);
        var seen = Assert.IsType<Border>(mantras.Children[0]);
        Assert.Contains("Snap Chain", Texts(seen));
        Assert.Contains("set start", Texts(seen));
        var curse = mantras.Children.OfType<Border>().Single(c => Equals(c.Tag, "hair_trigger"));
        Assert.Contains("taken, never chosen", Texts(curse));
        Assert.Contains("???", Texts(mantras.Children.OfType<Border>().Single(c => Equals(c.Tag, "golden_touch"))));
        var diary = Part<StackPanel>(hub.Window, "DiaryHost");
        Assert.Contains("Flash", Texts(diary));
        Assert.Contains(ChaosBubbleVariants.DescriptionFor("flash"), Texts(diary));
        Assert.DoesNotContain("White Rabbit", Texts(diary));
        Assert.Equal(ChaosBubbleVariants.All.Count + 7, diary.Children.OfType<Border>().Count() - 9);
    });

    [Fact]
    public Task Clicking_a_discovered_mantra_sets_and_clears_the_saved_start() => Run(() =>
    {
        ChaosMeta.State.RunsCompleted = 10;
        ChaosMeta.State.BenchPurchases.Add(BenchIds.StartMantra);
        ChaosMeta.State.DiscoveredCodexIds.Add("boon:defuse_chain");
        using var hub = new Hub();
        var w = hub.Window;
        w.Show();
        Click(Part<Button>(w, "BtnMenuDollhouse"));
        w.NavigateTo("improve");
        void PressFirst()
        {
            w.UpdateLayout();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            global::Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var row = Part<StackPanel>(w, "MantrasHost").Children.OfType<Border>().First();
            var point = row.TranslatePoint(new Point(20, 20), w)!.Value;
            w.MouseDown(point, MouseButton.Left);
            w.MouseUp(point, MouseButton.Left);
        }
        PressFirst();
        Assert.Equal("defuse_chain", ChaosMeta.State.EquippedStartBoon);
        Assert.Equal("defuse_chain", ChaosMetaStore.Load(CoreSettings.Current.ChaosActiveSlot).EquippedStartBoon);
        Assert.Equal(new Thickness(3), Assert.IsType<Border>(Part<StackPanel>(w, "MantrasHost").Children[0]).BorderThickness);
        PressFirst();
        Assert.Null(ChaosMeta.State.EquippedStartBoon);
        Assert.Null(ChaosMetaStore.Load(CoreSettings.Current.ChaosActiveSlot).EquippedStartBoon);
    });

    private static T Part<T>(Control root, string name) where T : Control => root.FindControl<T>(name)!;
    private static string[] Texts(Control root) => root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text ?? "").ToArray();
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Button ButtonFor(Control root, string id, string text) => root.GetLogicalDescendants().OfType<Button>()
        .Single(b => Equals(b.Tag, id) && b.Content is StackPanel p && p.Children.OfType<TextBlock>().Any(t => t.Text == text));

    private static Task Run(Action test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
        try
        {
            ChaosMeta.Init();
            ChaosMeta.DebugResetState();
            test();
        }
        finally { global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = false; }
        return Task.CompletedTask;
    });

    private sealed class Hub : IDisposable
    {
        public ChaosHubWindow Window { get; } = new();
        public void Dispose() => Window.Close();
    }
}
