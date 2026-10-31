using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// The "Online media today" surfaces on this head (WPF MainWindow.OnlineMeter.cs + AssetsTabView.xaml:273-465), driven
/// by the Core OnlineMeterRules both heads share: the title-bar pill, the Assets card with its percent-only bar and
/// line, the one-time official note, the stamped line, and the "Media from Scrolller" credit that stays up.
/// </summary>
public sealed class OnlineMeterSurfaceTests
{
    private static OnlineMeterState State(int pct, bool spent = false, bool official = false, bool seen = false,
        ScrolllerPlan plan = ScrolllerPlan.Free, string source = "mixed", bool consent = true, bool on = true)
        => OnlineMeterRules.For(source, consent, on, pct, spent, plan, official, seen);

    [Fact]
    public Task The_assets_card_paints_percent_only_and_the_note_follows_the_rules() => Run(() =>
    {
        var tab = new AssetsTabView();
        var noon = new DateTime(2026, 10, 23, 12, 0, 0);
        var midnight = noon.Date.AddDays(1);

        tab.PaintOnlineMeter(State(42), noon, midnight);
        Assert.True(tab.OnlineMeterCard.IsVisible);
        Assert.Equal("42%", tab.TxtOnlineMeterPct.Text);
        Assert.DoesNotContain("MB", tab.TxtOnlineMeterLine.Text);
        Assert.Equal(42, tab.OnlineMeterBar.ColumnDefinitions[0].Width.Value);
        Assert.False(tab.OnlineOfficialNote.IsVisible);
        Assert.False(tab.OnlineMeterStamped.IsVisible);

        tab.PaintOnlineMeter(State(10, official: true), noon, midnight);
        Assert.True(tab.OnlineOfficialNote.IsVisible);
        Assert.True(tab.OnlineMeterStamped.IsVisible);
        tab.PaintOnlineMeter(State(10, official: true, seen: true), noon, midnight);
        Assert.False(tab.OnlineOfficialNote.IsVisible);   // gone for good after "Nice"
        Assert.True(tab.OnlineMeterStamped.IsVisible);

        tab.PaintOnlineMeter(State(100, spent: true), noon, midnight);
        Assert.Equal("100%", tab.TxtOnlineMeterPct.Text);
        Assert.Contains(string.Format(Loc.Get("online_meter_hours"), 12), tab.TxtOnlineMeterLine.Text);

        foreach (var hidden in new[] { State(5, source: "local"), State(5, consent: false), State(5, on: false) })
        {
            tab.PaintOnlineMeter(hidden, noon, midnight);
            Assert.False(tab.OnlineMeterCard.IsVisible);
        }
        // Licence 3.2: the linked credit and the day-limit line stay in the fold, official or not.
        Assert.Equal("Media from Scrolller", ((global::Avalonia.Controls.TextBlock)tab.TxtScrolllerCredit.Link.Content!).Text);
        Assert.Contains(Loc.Get("label_online_media_credit"), tab.TxtScrolllerCredit.Inlines!.OfType<global::Avalonia.Controls.Documents.Run>().Select(r => r.Text));
        Assert.Equal(Loc.Get("online_cache_day_line"), tab.TxtOnlineCacheDay.Text);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(ConditioningControlPanel.Models.MotionLevel.Full, -6)]
    [InlineData(ConditioningControlPanel.Models.MotionLevel.Reduced, 0)]
    public void Official_entrance_definition_matches_Wpf(ConditioningControlPanel.Models.MotionLevel level, int y)
    {
        var animation = AssetsTabView.OfficialNoteEntrance(level);
        Assert.Equal(TimeSpan.FromMilliseconds(420), animation.Duration);
        Assert.IsType<global::Avalonia.Animation.Easings.CubicEaseOut>(animation.Easing);
        Assert.Equal(2, animation.Children.Count);
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(i, animation.Children[i].Cue.CueValue);
            var setters = animation.Children[i].Setters.OfType<global::Avalonia.Styling.Setter>().ToArray();
            Assert.Equal(i == 0 ? 0.2 : 1.0, setters.Single(s => s.Property == Visual.OpacityProperty).Value);
            var transform = Assert.IsType<global::Avalonia.Media.Transformation.TransformOperations>(
                setters.Single(s => s.Property == Visual.RenderTransformProperty).Value);
            Assert.Equal(i == 0 ? y : 0, transform.Value.M32);
        }
    }

    [Theory]
    [InlineData(ConditioningControlPanel.Models.MotionLevel.Full, false, true, -6, false)]
    [InlineData(ConditioningControlPanel.Models.MotionLevel.Full, false, true, -6, true)]
    [InlineData(ConditioningControlPanel.Models.MotionLevel.Reduced, false, true, 0, false)]
    [InlineData(ConditioningControlPanel.Models.MotionLevel.Off, false, false, 0, false)]
    [InlineData(ConditioningControlPanel.Models.MotionLevel.Full, true, true, 0, false)]
    public Task Official_note_lands_only_on_first_effective_show(
        ConditioningControlPanel.Models.MotionLevel level, bool gentle, bool animates, int y, bool detach) => Run(() =>
    {
        CoreSettings.Current.MotionLevel = level;
        CoreSettings.Current.GentleVisuals = gentle;
        var probe = UiMotionPreference.OsProbe;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        var tab = new AssetsTabView();
        // Keep the real note and its view wiring, without measuring unrelated font-heavy assets UI.
        ((global::Avalonia.Controls.Panel)tab.OnlineOfficialNote.Parent!).Children.Remove(tab.OnlineOfficialNote);
        tab.OnlineOfficialNote.Child = null;
        tab.Content = tab.OnlineOfficialNote;
        var parent = new global::Avalonia.Controls.Border { Child = tab, IsVisible = false };
        var window = new global::Avalonia.Controls.Window { Content = parent };
        var calls = 0;
        global::Avalonia.Animation.Animation? captured = null;
        var token = default(System.Threading.CancellationToken);
        tab.RunOfficialLanding = (animation, cancellation) =>
        {
            calls++;
            token = cancellation;
            captured = animation;
            return Task.Delay(System.Threading.Timeout.Infinite, cancellation);
        };
        try
        {
            window.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            tab.PaintOnlineMeter(State(10, official: true), DateTime.Now, DateTime.Now.AddDays(1));
            Assert.Equal(0, calls); // a visible note under a hidden tab must wait
            parent.IsVisible = true;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(animates ? 1 : 0, calls);
            if (animates)
            {
                var animation = Assert.IsType<global::Avalonia.Animation.Animation>(captured);
                Assert.Equal(TimeSpan.FromMilliseconds(420), animation.Duration);
                Assert.IsType<global::Avalonia.Animation.Easings.CubicEaseOut>(animation.Easing);
                Assert.Equal(2, animation.Children.Count);
                Assert.Equal(0.2, animation.Children[0].Setters.OfType<global::Avalonia.Styling.Setter>().Single(s => s.Property == Visual.OpacityProperty).Value);
                Assert.Equal(1.0, animation.Children[1].Setters.OfType<global::Avalonia.Styling.Setter>().Single(s => s.Property == Visual.OpacityProperty).Value);
                var transform = Assert.IsType<global::Avalonia.Media.Transformation.TransformOperations>(
                    animation.Children[0].Setters.OfType<global::Avalonia.Styling.Setter>().Single(s => s.Property == Visual.RenderTransformProperty).Value);
                Assert.Equal(y, transform.Value.M32);
            }
            tab.PaintOnlineMeter(State(20, official: true), DateTime.Now, DateTime.Now.AddDays(1));
            Assert.Equal(animates ? 1 : 0, calls); // repaint cannot restart it
            if (detach) parent.Child = null;
            else parent.IsVisible = false;
            if (animates) Assert.True(token.IsCancellationRequested);
            parent.IsVisible = true;
            parent.Child = null;
            parent.Child = tab;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(animates ? 1 : 0, calls); // no replay on reattach
            Assert.Equal(1.0, tab.OnlineOfficialNote.Opacity);
        }
        finally
        {
            window.Close();
            UiMotionPreference.OsProbe = probe;
            UiMotionPreference.CacheStamp = long.MinValue;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task The_title_bar_pill_shows_the_day_and_hides_off_the_rules() => Run(() =>
    {
        var shell = new MainShellWindow();
        try
        {
            shell.PaintOnlineMeterPill(State(42));
            Assert.True(shell.OnlinePill!.IsVisible);
            Assert.Equal(string.Format(Loc.Get("online_pill"), 42), shell.OnlinePillText!.Text);
            shell.PaintOnlineMeterPill(State(100, spent: true));
            Assert.Equal(Loc.Get("online_pill_done"), shell.OnlinePillText!.Text);
            shell.PaintOnlineMeterPill(State(42, source: "local"));
            Assert.False(shell.OnlinePill!.IsVisible);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void The_scrolller_link_never_opens_under_a_lock(bool strict, bool lockdown, bool chaos, bool opens)
        => Assert.Equal(opens, AssetsTabView.CanOpenScrolllerHome(strict, lockdown, chaos));

    [Fact]
    public void The_help_names_every_paragraph_and_no_figure()
    {
        var help = AssetsTabView.HelpText();
        foreach (var key in OnlineMeterRules.HelpParagraphKeys) Assert.Contains(Loc.Get(key), help);
        Assert.DoesNotContain(" MB", help);
    }

    [Fact]
    public void The_shell_wires_the_pill_and_the_picker_pokes_the_meter()
    {
        var root = RepoRoot();
        Assert.Contains("WireOnlineMeterPill()", File.ReadAllText(Path.Combine(root, "CCP.Avalonia", "Views", "Windows", "MainShellWindow.axaml.cs")));
        var picker = File.ReadAllText(Path.Combine(root, "CCP.Avalonia", "Views", "Tabs", "AssetsTabView.MediaPicker.cs"));
        Assert.Contains("WireOnlineMeter()", picker);
        Assert.Contains("OnlineMeterHead.Poke()", picker);
    }

    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        LocalizationManager.Instance.SetLanguage("en");
        await body();
    });

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Avalonia", "Platform"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
