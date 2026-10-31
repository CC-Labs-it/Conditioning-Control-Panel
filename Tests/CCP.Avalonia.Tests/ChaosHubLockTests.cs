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

public sealed class ChaosHubLockTests
{
    // WPF ChaosHubWindow.Lessons.cs:29-85: clamp, minimum visible fill and hover mechanics.
    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(1, 1, 7)]
    [InlineData(30, 30, 60)]
    [InlineData(100, 60, 120)]
    public Task Habit_lesson_replaces_train_and_shows_saved_progress(long saved, long shown, double width) => Run(() =>
    {
        ChaosMeta.State.LessonProgress["slow_fuses"] = saved;
        using var hub = new Hub();
        var panel = hub.Window.GetLogicalDescendants().OfType<StackPanel>()
            .Single(p => p.Tag as string == "slow_fuses");
        Assert.Contains(panel.Children.OfType<TextBlock>(), t => t.Text == $"lesson: {shown} of 60");
        var track = Assert.IsType<Border>(panel.Children[1]);
        Assert.Equal((120, 7), (track.Width, track.Height));
        Assert.Equal(width, Assert.IsType<Border>(track.Child).Width);
        var tip = Assert.IsType<string>(ToolTip.GetTip(panel));
        Assert.StartsWith(shown == 0 ? "the lesson\n" : "the lesson, half-learned\n", tip);
        Assert.Contains(ChaosLessons.ById("slow_fuses")!.Detail, tip);
        Assert.Empty(Buttons(hub.Window, "slow_fuses"));
    });

    // WPF xaml.cs:735-747: rank, then scripted first accessory, then lesson, then buy.
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public Task Accessory_script_precedes_lesson_and_completion_restores_unlock(bool spanker, bool complete) => Run(() =>
    {
        if (spanker) ChaosMeta.State.LifetimeBoonLevels["the_spanker"] = 1;
        if (complete) ChaosMeta.State.LessonsComplete.Add("blindfold");
        using var hub = new Hub();
        var buttons = Buttons(hub.Window, "blindfold");
        if (!spanker)
        {
            var button = Assert.Single(buttons);
            Assert.False(button.IsEnabled);
            Assert.Equal("she sells these in an order of her own.", ToolTip.GetTip(button));
            Assert.True(ToolTip.GetShowOnDisabled(button));
        }
        else if (!complete)
        {
            Assert.Empty(buttons);
            Assert.Contains(hub.Window.GetLogicalDescendants().OfType<StackPanel>(), p => p.Tag as string == "blindfold");
        }
        else Assert.True(Assert.Single(buttons).IsEnabled);
    });

    [Fact]
    public Task Completed_habit_and_owned_boon_do_not_show_lesson_locks() => Run(() =>
    {
        ChaosMeta.State.LessonsComplete.Add("slow_fuses");
        ChaosMeta.State.LifetimeBoonLevels["vibe_popping"] = 1;
        using var hub = new Hub();
        Assert.True(Assert.Single(Buttons(hub.Window, "slow_fuses")).IsEnabled);
        Assert.DoesNotContain(hub.Window.GetLogicalDescendants().OfType<StackPanel>(), p => p.Tag as string == "vibe_popping");
        Assert.NotEmpty(Buttons(hub.Window, "vibe_popping"));
    });

    private static Button[] Buttons(Control root, string id) => root.GetLogicalDescendants().OfType<Button>()
        .Where(b => b.Tag as string == id).ToArray();

    private static Task Run(Action test) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        // Rendering uses this in-memory state without Init reloading it from disk.
        global::ConditioningControlPanel.Avalonia.RenderProof.Rendering = true;
        try
        {
            ChaosMeta.Init();
            ChaosMeta.State.RunsCompleted = 100;
            ChaosMeta.State.Sparks = 10000;
            ChaosMeta.State.LessonProgress.Clear();
            ChaosMeta.State.LessonsComplete.Clear();
            ChaosMeta.State.LifetimeBoonLevels.Clear();
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
