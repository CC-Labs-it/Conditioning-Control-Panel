using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
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
        var popup = Assert.IsType<ToolTip>(ToolTip.GetTip(panel));
        var tip = Assert.IsType<StackPanel>(popup.Content);
        Assert.Equal(260, tip.MaxWidth);
        var title = Assert.IsType<TextBlock>(tip.Children[0]);
        Assert.Equal(FontWeight.Bold, title.FontWeight);
        Assert.Equal(13, title.FontSize);
        var accent = id == "slow_fuses" ? Color.FromRgb(0x49, 0xB6, 0xE8) : Color.FromRgb(0xE8, 0x43, 0x93);
        Assert.Equal(accent, Assert.IsAssignableFrom<ISolidColorBrush>(title.Foreground).Color);
        Assert.Equal(Colors.Gold, Assert.IsAssignableFrom<ISolidColorBrush>(Assert.IsType<TextBlock>(tip.Children[2]).Foreground).Color);
        Assert.Equal(Color.FromArgb(0xF2, 0x15, 0x12, 0x26), Assert.IsAssignableFrom<ISolidColorBrush>(popup.Background).Color);
        Assert.Equal(Color.FromArgb(0x55, 0xFF, 0x69, 0xB4), Assert.IsAssignableFrom<ISolidColorBrush>(popup.BorderBrush).Color);
        Assert.Equal(new Thickness(10, 8, 10, 8), popup.Padding);
        Assert.Equal(250, ToolTip.GetShowDelay(panel));
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

    [Theory]
    [InlineData("en")][InlineData("de")][InlineData("es")][InlineData("fr")][InlineData("it")]
    [InlineData("pt-BR")][InlineData("ru")][InlineData("ja")][InlineData("ko")][InlineData("zh-CN")]
    public Task Lesson_and_script_copy_uses_the_selected_catalogue(string language) => InHub(state =>
        state.LessonProgress["slow_fuses"] = 30, hub =>
    {
        using var json = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(
            AppContext.BaseDirectory, "Localization", "Languages", language + ".json")));
        string Copy(string key) => json.RootElement.GetProperty(key).GetString()!;
        foreach (var def in ChaosLessons.All)
            foreach (var suffix in new[] { "_text", "_detail" })
            {
                var key = "chaos_lesson_" + def.Id + suffix;
                Assert.Equal(Copy(key), Loc.Get(key));
                if (language != "en") Assert.NotEqual(suffix == "_text" ? def.Text : def.Detail, Copy(key));
            }
        var texts = hub.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(Copy("chaos_lesson_slow_fuses_text"), texts);
        Assert.Contains(string.Format(Copy("chaos_lesson_count"), 30, 60), texts);
        var panel = hub.GetLogicalDescendants().OfType<StackPanel>().Single(p =>
            p.Children.OfType<TextBlock>().Any(t => t.Text == string.Format(Copy("chaos_lesson_count"), 30, 60)));
        var tip = Assert.IsType<StackPanel>(Assert.IsType<ToolTip>(ToolTip.GetTip(panel)).Content);
        Assert.Equal(Copy("chaos_lesson_started"), Assert.IsType<TextBlock>(tip.Children[0]).Text);
        Assert.Equal(Copy("chaos_lesson_slow_fuses_detail"), Assert.IsType<TextBlock>(tip.Children[1]).Text);
        Assert.Equal(string.Format(Copy("chaos_lesson_progress"), 30, 60), Assert.IsType<TextBlock>(tip.Children[2]).Text);
        Assert.Equal(Copy("chaos_accessory_script_lock"), ToolTip.GetTip(Button(hub, "the_pull")));
    }, language);

    [Fact]
    public Task Lesson_tooltip_expires_and_stops_on_close_or_detach() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        EnsurePlatform();
        var target = new Border();
        var window = new Window { Content = target };
        var timer = ChaosHubWindow.AttachLessonTip(target, new StackPanel());
        try
        {
            window.Show();
            Assert.Equal(TimeSpan.FromSeconds(30), timer.Interval);
            ToolTip.SetIsOpen(target, true);
            Assert.True(timer.IsEnabled);
            timer.Interval = TimeSpan.FromMilliseconds(1);
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (ToolTip.GetIsOpen(target) && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.False(ToolTip.GetIsOpen(target));
            Assert.False(timer.IsEnabled);
            timer.Interval = TimeSpan.FromSeconds(30);
            ToolTip.SetIsOpen(target, true);
            Assert.True(timer.IsEnabled);
            ToolTip.SetIsOpen(target, false);
            Assert.False(timer.IsEnabled);
            ToolTip.SetIsOpen(target, true);
            window.Content = null;
            Assert.False(timer.IsEnabled);
            Assert.False(ToolTip.GetIsOpen(target));
        }
        finally { timer.Stop(); window.Close(); }
    });

    private static void EnsurePlatform()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    private static Button Button(Control hub, string id) =>
        hub.GetLogicalDescendants().OfType<Button>().Single(b => b.Tag as string == id);

    private static Task InHub(Action<ChaosMetaState> arrange, Action<ChaosHubWindow> check, string language = "en") =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsurePlatform();
            var previousLanguage = LocalizationManager.Instance.CurrentLanguage;
            LocalizationManager.Instance.SetLanguage(language);
            var state = new ChaosMetaState { RunsCompleted = 25, Sparks = 10000, ToyPockets = 1, AccessoryPockets = 1 };
            arrange(state);
            ChaosMetaStore.Save(state, 1);
            ChaosMetaStore.SetActiveSlot(1);
            var hub = new ChaosHubWindow();
            try { check(hub); }
            finally { hub.Close(); LocalizationManager.Instance.SetLanguage(previousLanguage); }
            return Task.CompletedTask;
        });
}
