using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using ConditioningControlPanel;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Controls.Documents;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
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
        Assert.DoesNotContain("MB", tab.TxtOnlineMeterSummary.Text);
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
        Assert.Contains(string.Format(Loc.Get("online_meter_hours"), 12), tab.TxtOnlineMeterSummary.Text);

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
            Assert.Equal(new CornerRadius(12), shell.OnlinePill.CornerRadius);
            Assert.Equal(new Thickness(6, 3, 10, 3), shell.OnlinePill.Padding);
            Assert.Equal(string.Format(Loc.Get("online_pill"), 42), shell.OnlinePillText!.Text);
            Assert.Equal(42, shell.OnlinePillRing!.Percent);
            Assert.Equal(OnlineMeterTone.Pink, shell.OnlinePillRing.Tone);
            shell.PaintOnlineMeterPill(State(100, spent: true));
            shell.OpenOnlineHover(State(100, spent: true));
            Assert.NotNull(shell.OnlineHoverBody);
            Assert.Contains(shell.OnlineHoverBody!.Children.OfType<global::Avalonia.Controls.Button>(), b => Equals(b.Content, Loc.Get("online_see_plans")));
            shell.PaintOnlineMeterPill(State(100, spent: true, plan: ScrolllerPlan.Prime));
            Assert.DoesNotContain(shell.OnlineHoverBody.Children.OfType<Button>(), b => Equals(b.Content, Loc.Get("online_see_plans")));
            Assert.Single(shell.OnlineHoverBody.GetLogicalDescendants().OfType<Button>(), b => Equals(b.Tag, "online-help-open"));
            Assert.Contains(shell.OnlineHoverBody.Children.OfType<global::Avalonia.Controls.TextBlock>(), t => t.Text == Loc.Get("online_hover_body"));
            Assert.Equal(Loc.Get("online_pill_done"), shell.OnlinePillText!.Text);
            Assert.Equal(OnlineMeterTone.Spent, shell.OnlinePillRing!.Tone);
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task Spent_help_opens_assets_scrolls_to_media_and_pins_the_slip(int plan) => Run(() =>
    {
        CoreSettings.Current.RemoteMediaConsented = true;
        CoreSettings.Current.MediaSource = "mixed";
        ScrolllerPolicy.Apply(true, null);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.ShowTab("settings");
            var tab = shell.FindControl<AssetsTabView>("AssetsTab")!;
            var scrolled = false;
            tab.RemoteMediaBlock.AddHandler(Control.RequestBringIntoViewEvent, (_, _) => scrolled = true);
            shell.PaintOnlineMeterPill(State(100, spent: true, plan: (ScrolllerPlan)plan));
            shell.OpenOnlineHover(State(100, spent: true, plan: (ScrolllerPlan)plan));
            var help = Assert.Single(shell.OnlineHoverBody!.GetLogicalDescendants().OfType<Button>(), b => Equals(b.Tag, "online-help-open"));
            help.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal("assets", shell.CurrentTab);
            Assert.True(scrolled);
            Assert.True(LeashHelpButton.IsPinned(tab.OnlineHelpButton));
            Assert.True(LeashHelpButton.IsOpen(tab.OnlineHelpButton));
            Assert.Equal(AssetsTabView.HelpText(), Assert.IsType<TextBlock>(Assert.IsType<Border>(LeashHelpButton.Slip(tab.OnlineHelpButton)).Child).Text);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Plans_link_is_inline_and_does_not_add_a_card_row() => Run(() =>
    {
        var tab = new AssetsTabView();
        var now = DateTime.Now;
        tab.PaintOnlineMeter(State(42), now, now.AddHours(1));
        var inline = Assert.Single(tab.TxtOnlineMeterLine.Inlines!.OfType<InlineUIContainer>());
        Assert.Same(tab.FindControl<Button>("BtnOnlineSeePlans"), inline.Child);
        tab.OnlineMeterCard.Measure(new Size(1200, double.PositiveInfinity));
        var withPlans = tab.OnlineMeterCard.DesiredSize.Height;
        tab.PaintOnlineMeter(State(42, plan: ScrolllerPlan.Prime), now, now.AddHours(1));
        tab.OnlineMeterCard.Measure(new Size(1200, double.PositiveInfinity));
        Assert.Equal(tab.OnlineMeterCard.DesiredSize.Height, withPlans, precision: 2);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Spent_hover_handles_presses_before_the_title_bar() => Run(() =>
    {
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.PaintOnlineMeterPill(State(100, spent: true));
            shell.OpenOnlineHover(State(100, spent: true));
            var holder = Assert.IsType<Border>(shell.OnlineHoverBody!.Parent);
            var title = shell.FindControl<Border>("TitleBarBorder")!;
            var reachedTitle = 0;
            title.AddHandler(InputElement.PointerPressedEvent, (_, _) => reachedTitle++, RoutingStrategies.Bubble);
            var initial = shell.WindowState;
            var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            foreach (var clicks in new[] { 1, 2 })
            {
                var press = new PointerPressedEventArgs(holder, pointer, shell, new Point(300, 100), 0,
                    new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed), KeyModifiers.None, clicks);
                holder.RaiseEvent(press);
                Assert.True(press.Handled);
                Assert.Equal(0, reachedTitle);
                Assert.Equal(initial, shell.WindowState);
            }
            Assert.Equal(Color.Parse("#3A3A5C"), Assert.IsType<SolidColorBrush>(holder.BorderBrush).Color);
            var body = shell.OnlineHoverBody;
            var head = Assert.IsType<DockPanel>(body.Children[0]);
            var heading = Assert.Single(head.Children.OfType<TextBlock>());
            Assert.Equal(13, heading.FontSize);
            Assert.Equal(FontWeight.SemiBold, heading.FontWeight);
            Assert.Equal(Color.Parse("#CCCCE0"), Assert.IsType<SolidColorBrush>(Assert.IsType<TextBlock>(body.Children[1]).Foreground).Color);
            Assert.Equal(Color.Parse("#E0B0FF"), Assert.IsType<SolidColorBrush>(Assert.IsType<TextBlock>(body.Children[2]).Foreground).Color);
            Assert.NotNull(Assert.Single(body.Children.OfType<Button>()).Theme);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    private static Task Run(Func<Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        OnlineMeterTestPlatform.EnsureApplication();
        LocalizationManager.Instance.SetLanguage("en");
        var settings = CoreSettings.Current;
        var (source, consent) = (settings.MediaSource, settings.RemoteMediaConsented);
        var (enabled, cap) = (ScrolllerPolicy.Enabled, ScrolllerPolicy.MaxClipShortSide);
        try { await body(); }
        finally
        {
            settings.MediaSource = source;
            settings.RemoteMediaConsented = consent;
            ScrolllerPolicy.Apply(enabled, cap);
        }
    });

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "CCP.Avalonia", "Platform"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
