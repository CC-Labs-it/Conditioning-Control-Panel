using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using ConditioningControlPanel.Avalonia.Views.Controls;
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
            Assert.Equal(Loc.Get("online_pill_done"), shell.OnlinePillText!.Text);
            shell.PaintOnlineMeterPill(State(42, source: "local"));
            Assert.False(shell.OnlinePill!.IsVisible);
        }
        finally { shell.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HelpUsesTheWpfPaperCardLayout() => Run(() =>
    {
        var slip = new OnlineMediaHelpSlip();
        Assert.Equal("online-help-slip", slip.Tag);
        Assert.Equal(320, slip.Width);
        Assert.Same(LeashHelpSlip.Paper, slip.Background);
        Assert.Equal(new CornerRadius(6), slip.CornerRadius);
        Assert.Equal(new Thickness(0, 0, 0, 1), slip.BorderThickness);
        Assert.Equal(Color.Parse("#D8CFBF"), Assert.IsAssignableFrom<ISolidColorBrush>(slip.BorderBrush).Color);
        Assert.Equal(18, slip.BoxShadow[0].Blur);
        Assert.Equal(6, slip.BoxShadow[0].OffsetY);
        var root = Assert.IsType<StackPanel>(slip.Child);
        Assert.Equal(2, root.Children.Count);
        var band = Assert.IsType<Border>(root.Children[0]);
        Assert.Equal(6, band.Height);
        Assert.Equal(new CornerRadius(6, 6, 0, 0), band.CornerRadius);
        Assert.Equal(LeashHelpSlip.Lilac, Assert.IsAssignableFrom<ISolidColorBrush>(band.Background).Color);
        var body = Assert.IsType<StackPanel>(root.Children[1]);
        Assert.Equal(new Thickness(16, 12, 16, 16), body.Margin);
        Assert.Equal(1 + OnlineMeterRules.HelpParagraphKeys.Length, body.Children.Count);
        var title = Assert.IsType<TextBlock>(body.Children[0]);
        Assert.Equal(Loc.Get(OnlineMeterRules.HelpTitleKey), title.Text);
        Assert.Equal(17, title.FontSize);
        Assert.Equal(FontWeight.ExtraBold, title.FontWeight);
        Assert.Same(LeashHelpSlip.Ink, title.Foreground);
        for (int i = 0; i < OnlineMeterRules.HelpParagraphKeys.Length; i++)
        {
            var text = Assert.IsType<TextBlock>(body.Children[i + 1]);
            bool last = i == OnlineMeterRules.HelpParagraphKeys.Length - 1;
            Assert.Equal(Loc.Get(OnlineMeterRules.HelpParagraphKeys[i]), text.Text);
            Assert.Equal(TextWrapping.Wrap, text.TextWrapping);
            Assert.Equal(new Thickness(0, i == 0 ? 10 : 8, 0, 0), text.Margin);
            Assert.Equal(last ? 11.5 : 13, text.FontSize);
            Assert.Same(last ? LeashHelpSlip.Muted : LeashHelpSlip.Ink, text.Foreground);
            if (last) Assert.Equal(ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer.Mono, text.FontFamily);
            else { Assert.Equal(title.FontFamily, text.FontFamily); Assert.Equal(18, text.LineHeight); }
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task RepeatedAssetsHelpRequestsDoNotToggleThePinOff() => Run(() =>
    {
        var tab = new AssetsTabView();
        tab.PinOnlineHelp();
        Assert.True(LeashHelpButton.IsPinned(tab.OnlineHelp!));
        tab.PinOnlineHelp();
        Assert.True(LeashHelpButton.IsPinned(tab.OnlineHelp!));
        return Task.CompletedTask;
    });

    [Fact]
    public Task PillHelpPinsTheAssetsSlipAndRepeatedRequestsLeaveItOpen() => Run(() =>
    {
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            var tab = shell.FindControl<AssetsTabView>("AssetsTab")!;
            tab.PaintOnlineMeter(State(42), DateTime.Now, DateTime.Now.AddHours(1));
            var request = shell.FindControl<Button>("OnlinePillHelp")!;
            request.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var help = tab.OnlineHelp!;
            Assert.True(LeashHelpButton.IsPinned(help));
            Assert.True(LeashHelpButton.IsOpen(help));
            request.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(LeashHelpButton.IsPinned(help));
            Assert.True(LeashHelpButton.IsOpen(help));
            Assert.Contains(Assert.IsType<OnlineMediaHelpSlip>(LeashHelpButton.Slip(help)).GetLogicalDescendants().OfType<TextBlock>(),
                t => t.Text == Loc.Get("online_help_cache"));
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
