using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ChaosHubGateTests
{
    // WPF ChaosHubWindow.xaml.cs:463-482 and :739-747, Lessons.cs:25-81.
    [Theory]
    [InlineData("slow_fuses", 0L)]
    [InlineData("slow_fuses", 30L)]
    [InlineData("vibe_popping", 5L)]
    [InlineData("vibe_popping", 999L)]
    public Task Lesson_rows_replace_buy_with_clamped_progress(string id, long raw) => InHub(state =>
    {
        state.LessonProgress[id] = raw;
    }, hub =>
    {
        var def = ChaosLessons.ById(id)!;
        var progress = Math.Clamp(raw, 0, def.Target);
        var panel = hub.GetLogicalDescendants().OfType<StackPanel>()
            .Single(p => p.Children.OfType<TextBlock>().Any(t => t.Text == $"lesson: {progress} of {def.Target}")
                && p.GetLogicalDescendants().OfType<TextBlock>().Any(t => t.Text == def.Text));
        Assert.Empty(panel.GetLogicalDescendants().OfType<Button>());
        Assert.DoesNotContain(hub.GetLogicalDescendants().OfType<Button>(), b => b.Tag as string == id);
        var track = Assert.Single(panel.Children.OfType<Border>());
        Assert.Equal(120, track.Width);
        Assert.Equal(7, track.Height);
        Assert.Equal(progress == 0 ? 0 : Math.Max(7, 120.0 * progress / def.Target),
            Assert.IsType<Border>(track.Child).Width);
        var tip = Assert.IsType<StackPanel>(ToolTip.GetTip(panel));
        Assert.Contains(tip.Children.OfType<TextBlock>(), t => t.Text == (progress == 0 ? "the lesson" : "the lesson, half-learned"));
        Assert.Contains(tip.Children.OfType<TextBlock>(), t => t.Text == def.Detail);
    });

    // WPF :731-747: script hides a lesson until the first accessory is owned.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Script_gate_precedes_lesson_then_releases_it(bool spanker) => InHub(state =>
    {
        if (spanker) state.LifetimeBoonLevels["the_spanker"] = 1;
    }, hub =>
    {
        var buttons = hub.GetLogicalDescendants().OfType<Button>().Where(b => b.Tag as string == "the_pull").ToList();
        if (spanker)
        {
            Assert.Empty(buttons);
            Assert.Contains(hub.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "lesson: 0 of 15");
        }
        else
        {
            var held = Assert.Single(buttons);
            Assert.False(held.IsEnabled);
            Assert.Equal("she sells these in an order of her own.", ToolTip.GetTip(held));
            Assert.True(ToolTip.GetShowOnDisabled(held));
        }
    });

    // WPF :463-482: a completed lesson still waits for rank.
    [Theory]
    [InlineData(25, false)]
    [InlineData(50, true)]
    public Task Completed_habit_lesson_respects_rank(int runs, bool enabled) => InHub(state =>
    {
        state.RunsCompleted = runs;
        state.LessonsComplete.Add("extreme_tier");
    }, hub =>
    {
        var train = Button(hub, "extreme_tier");
        Assert.Equal(enabled, train.IsEnabled);
        if (!enabled) Assert.Contains(ChaosRanks.RankLockedTip, Assert.IsType<string>(ToolTip.GetTip(train)));
    });

    // WPF :753-767: owned boons keep equip but their final deepen waits for Devoted.
    [Theory]
    [InlineData(25, 2, true)]
    [InlineData(25, 3, false)]
    [InlineData(50, 3, true)]
    public Task Only_the_capstone_deepen_waits_for_Devoted(int runs, int level, bool enabled) => InHub(state =>
    {
        state.RunsCompleted = runs;
        state.LifetimeBoonLevels["vibe_popping"] = level;
    }, hub =>
    {
        var deepen = hub.GetLogicalDescendants().OfType<Button>().Single(b =>
            b.Tag as string == "vibe_popping" && b.Content is StackPanel);
        Assert.Equal(enabled, deepen.IsEnabled);
        if (!enabled)
        {
            Assert.Contains(ChaosRanks.CapstoneLockedTip, Assert.IsType<string>(ToolTip.GetTip(deepen)));
            Assert.True(ToolTip.GetShowOnDisabled(deepen));
        }
    });

    [Theory]
    [InlineData("slow_fuses")]
    [InlineData("vibe_popping")]
    public Task Completing_a_lesson_restores_a_working_purchase(string id) => InHub(state =>
    {
        state.LessonsComplete.Add(id);
    }, hub =>
    {
        var buy = Button(hub, id);
        Assert.True(buy.IsEnabled);
        buy.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
        Assert.True(id == "slow_fuses" ? ChaosMeta.IsOwned(id) : ChaosMeta.IsBoonUnlocked(id));
        var disk = ChaosMetaStore.Load(1);
        Assert.True(id == "slow_fuses" ? disk.PurchasedUpgrades.Contains(id) : disk.LifetimeBoonLevels[id] == 1);
    });

    private static Button Button(Control hub, string id) =>
        hub.GetLogicalDescendants().OfType<Button>().Single(b => b.Tag as string == id);

    private static Task InHub(Action<ChaosMetaState> arrange, Action<ChaosHubWindow> check) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var state = new ChaosMetaState { RunsCompleted = 25, Sparks = 10000, ToyPockets = 1, AccessoryPockets = 1 };
            arrange(state);
            ChaosMetaStore.Save(state, 1);
            ChaosMetaStore.SetActiveSlot(1);
            var hub = new ChaosHubWindow();
            try { check(hub); }
            finally { hub.Close(); }
            return Task.CompletedTask;
        });
}
