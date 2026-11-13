using System;
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
