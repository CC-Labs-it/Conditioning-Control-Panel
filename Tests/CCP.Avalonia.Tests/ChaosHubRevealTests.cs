using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosHubRevealTests
{
    // Ported from #4013 (restack b147): the ApplyReveals gates. The trunk already covers
    // mantras, the codex and the rank and capstone locks.
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

    // WPF Bench.cs:231: a bench buy flashes and chimes the surface it just revealed.
    [Fact]
    public Task Bench_buy_flashes_the_revealed_surface_now() => Run(() =>
    {
        ChaosMeta.State.RunsCompleted = 10;
        ChaosMeta.State.Gold = 1000;
        var oldSfx = ChaosHubWindow.PlayRevealSfx;
        int chimes = 0;
        ChaosHubWindow.PlayRevealSfx = (k, _) => { if (k == "reveal_chime") chimes++; };
        try
        {
            using var hub = new Hub();
            var w = hub.Window;
            w.AdvanceRevealFlashes(100);   // settle whatever the hub open flashed
            chimes = 0;
            Click(Part<StackPanel>(w, "ImprovementsHost").GetLogicalDescendants().OfType<Button>().First(b => Equals(b.Tag, BenchIds.StatsPanel)));
            w.AdvanceRevealFlashes(0.01);
            Assert.Equal(1, chimes);
        }
        finally { ChaosHubWindow.PlayRevealSfx = oldSfx; }
    });

    // WPF Bench.cs:163: rank-short rows (second pockets before Devoted) sit at 0.6.
    [Fact]
    public Task Rank_short_bench_rows_are_dimmed() => Run(() =>
    {
        ChaosMeta.State.RunsCompleted = 10;
        using var hub = new Hub();
        var host = Part<StackPanel>(hub.Window, "ImprovementsHost");
        var items = ChaosBench.Items.ToList();
        var short2 = (Border)host.Children[items.FindIndex(i => i.Id == BenchIds.ToyPocket2) + 1];
        var open1 = (Border)host.Children[items.FindIndex(i => i.Id == BenchIds.ToyPocket1) + 1];
        Assert.Equal(0.6, short2.Opacity);
        Assert.Equal(1.0, open1.Opacity);
    });

    // WPF xaml.cs:827 and :918: the shelf Unequip plays ui_unequip and the stats panel refreshes.
    [Fact]
    public Task Shelf_unequip_plays_cue_and_refreshes_stats() => Run(() =>
    {
        ChaosMeta.State.RunsCompleted = 50;
        ChaosMeta.State.LifetimeBoonLevels = new() { ["vibe_popping"] = 1 };
        ChaosMeta.State.ActiveLifetimeBoons = new() { "vibe_popping" };
        var old = ChaosHubWindow.PlayCue;
        var cues = new System.Collections.Generic.List<string>();
        ChaosHubWindow.PlayCue = (k, _) => cues.Add(k);
        try
        {
            using var hub = new Hub();
            var w = hub.Window;
            ChaosMeta.State.Sparks = 4321;
            var btn = Part<Panel>(w, "BoonHostSkills").GetLogicalDescendants().OfType<Button>()
                .First(b => Equals(b.Tag, "vibe_popping") && (b.Content as TextBlock)?.Text == "Unequip");
            Click(btn);
            Assert.Contains("ui_unequip", cues);
            Assert.False(ChaosMeta.IsBoonActive("vibe_popping"));
            Assert.Equal(4321.ToString("N0"), Part<TextBlock>(w, "StSparks").Text);
        }
        finally { ChaosHubWindow.PlayCue = old; }
    });

    // WPF xaml.cs:1136: an owned bag tile plays ui_equip even when no toy pocket is sewn yet.
    [Fact]
    public Task Owned_bag_tile_plays_equip_cue_without_a_pocket() => Run(() =>
    {
        ChaosMeta.State.RunsCompleted = 50;
        ChaosMeta.State.LifetimeBoonLevels = new() { ["vibe_popping"] = 1 };
        var old = ChaosHubWindow.PlayCue;
        var cues = new System.Collections.Generic.List<string>();
        ChaosHubWindow.PlayCue = (k, _) => cues.Add(k);
        try
        {
            using var hub = new Hub();
            Assert.Equal(0, ChaosMeta.SlotsFor(ChaosBoonCategory.Skill));
            var tile = Part<Panel>(hub.Window, "TilesSkills").Children
                .Single(c => (ToolTip.GetTip(c) as string)?.Contains("click to equip") == true);
            tile.RaiseEvent(new PointerPressedEventArgs(tile, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true), hub.Window, default, 0,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None));
            Assert.False(ChaosMeta.IsBoonActive("vibe_popping"));
            Assert.Equal(new[] { "ui_equip" }, cues);
        }
        finally { ChaosHubWindow.PlayCue = old; }
    });

    private static T Part<T>(Control root, string name) where T : Control => root.FindControl<T>(name)!;
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

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
