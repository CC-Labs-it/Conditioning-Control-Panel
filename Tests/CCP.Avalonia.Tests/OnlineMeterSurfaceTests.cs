using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using ConditioningControlPanel;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
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
            shell.OpenOnlineHover(State(100, spent: true));
            Assert.NotNull(shell.OnlineHoverBody);
            Assert.Contains(shell.OnlineHoverBody!.Children.OfType<global::Avalonia.Controls.Button>(), b => Equals(b.Content, Loc.Get("online_see_plans")));
            shell.PaintOnlineMeterPill(State(100, spent: true, plan: ScrolllerPlan.Prime));
            Assert.Empty(shell.OnlineHoverBody.Children.OfType<global::Avalonia.Controls.Button>());
            Assert.Contains(shell.OnlineHoverBody.Children.OfType<global::Avalonia.Controls.TextBlock>(), t => t.Text == Loc.Get("online_hover_body"));
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

    [Fact]
    public void Official_note_entrance_obeys_motion_and_gentle_visuals()
    {
        var off = AssetsTabView.OfficialEntrance(ConditioningControlPanel.Models.MotionLevel.Off, false);
        Assert.Empty(off);
        var reduced = AssetsTabView.OfficialEntrance(ConditioningControlPanel.Models.MotionLevel.Reduced, true);
        var fade = Assert.Single(reduced);
        Assert.Equal(TimeSpan.FromMilliseconds(ConditioningControlPanel.Services.Safety.GentleVisuals.RiseMs(420, true)), fade.Duration);
        Assert.Equal(0.2, ((global::Avalonia.Styling.Setter)fade.Children[0].Setters[0]).Value);
        Assert.Equal(1.0, ((global::Avalonia.Styling.Setter)fade.Children[1].Setters[0]).Value);
        var full = AssetsTabView.OfficialEntrance(ConditioningControlPanel.Models.MotionLevel.Full, false);
        Assert.Equal(2, full.Length);
        Assert.Equal(TimeSpan.FromMilliseconds(420), full[0].Duration);
        Assert.Equal(global::Avalonia.Media.TranslateTransform.YProperty, ((global::Avalonia.Styling.Setter)full[1].Children[0].Setters[0]).Property);
        Assert.Equal(-6.0, ((global::Avalonia.Styling.Setter)full[1].Children[0].Setters[0]).Value);
        Assert.Equal(0.0, ((global::Avalonia.Styling.Setter)full[1].Children[1].Setters[0]).Value);
    }

    [Fact]
    public Task Language_changes_repaint_the_live_meter() => Run(() =>
    {
        OnlineMeterHead.Start();
        var count = 0;
        void Refreshed() => count++;
        OnlineMeterHead.Refresh += Refreshed;
        try
        {
            LocalizationManager.Instance.SetLanguage("de");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(count > 0);
        }
        finally
        {
            OnlineMeterHead.Refresh -= Refreshed;
            LocalizationManager.Instance.SetLanguage("en");
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Plans_link_follows_the_plan_and_repairs_a_dead_grant() => Run(() =>
    {
        var tab = new AssetsTabView();
        var button = tab.FindControl<global::Avalonia.Controls.Button>("BtnOnlineSeePlans")!;
        tab.PaintOnlineMeter(State(42), DateTime.Now, DateTime.Now.AddHours(1));
        Assert.True(button.IsVisible);
        tab.PaintOnlineMeter(State(42, plan: ScrolllerPlan.Prime), DateTime.Now, DateTime.Now.AddHours(1));
        Assert.False(button.IsVisible);
        tab.PaintOnlineMeter(State(42, consent: false), DateTime.Now, DateTime.Now.AddHours(1));
        Assert.False(button.IsVisible);
        var shell = new MainShellWindow();
        shell.Content = tab;
        var provider = ConditioningControlPanel.Services.TierGate.ReconnectIsTheAnswerProvider;
        var repaired = false;
        try
        {
            ConditioningControlPanel.Services.TierGate.ReconnectIsTheAnswerProvider = () => true;
            shell.ReconnectFromGateOverride = () => { repaired = true; return Task.CompletedTask; };
            CoreSettings.Current.MediaSource = "mixed";
            CoreSettings.Current.RemoteMediaConsented = true;
            ScrolllerPolicy.Apply(true, null);
            shell.Show();
            Assert.Same(shell, TopLevel.GetTopLevel(tab));
            Assert.NotNull(OnlineMeterHead.Now().SeePlansTier);
            button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
            Assert.True(repaired);
        }
        finally
        {
            ConditioningControlPanel.Services.TierGate.ReconnectIsTheAnswerProvider = provider;
            shell.Close();
        }
        return Task.CompletedTask;
    });

    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        OnlineMeterTestPlatform.EnsureApplication();
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
