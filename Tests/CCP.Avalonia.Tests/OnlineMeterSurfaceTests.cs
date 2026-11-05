using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls;
using Avalonia.Controls;
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
    public Task Dashboard_faces_have_matching_insets_and_rounded_clipping() => Run(() =>
    {
        var tab = new SettingsTabView();
        var logo = tab.FindControl<global::Avalonia.Controls.Border>("LogoFaceLogo")!;
        var pass = tab.FindControl<global::Avalonia.Controls.Border>("IntakePassFace")!;
        Assert.Equal(new Thickness(24), logo.Margin);
        Assert.Equal(logo.Margin, pass.Margin);
        Assert.True(logo.ClipToBounds);
        Assert.Equal(new CornerRadius(12), logo.CornerRadius);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Daily_share_slip_uses_the_shared_title_and_all_paragraphs() => Run(() =>
    {
        var slip = new ConditioningControlPanel.Avalonia.Views.Controls.OnlineMediaHelpSlip();
        var texts = global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(slip)
            .OfType<TextBlock>().Select(t => t.Text).ToArray();
        Assert.Equal(new[] { Loc.Get(OnlineMeterRules.HelpTitleKey) }
            .Concat(OnlineMeterRules.HelpParagraphs(Loc.Get)), texts);
        var paragraphs = global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(slip)
            .OfType<TextBlock>().Skip(1).ToArray();
        Assert.All(paragraphs.SkipLast(1), p => Assert.Equal(18, p.LineHeight));
        Assert.Equal(FriendsDrawer.Mono, paragraphs[^1].FontFamily);
        Assert.True(double.IsNaN(paragraphs[^1].LineHeight));
        return Task.CompletedTask;
    });

    [Fact]
    public Task Clicking_the_spent_pill_closes_the_hover_before_navigating() => Run(() =>
    {
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        CoreSettings.Current.MediaSource = "mixed";
        CoreSettings.Current.RemoteMediaConsented = true;
        var limits = new[] { ScrolllerAllowance.DailyBytes(ScrolllerPlan.Free), ScrolllerAllowance.DailyBytes(ScrolllerPlan.Basic),
            ScrolllerAllowance.DailyBytes(ScrolllerPlan.Prime), ScrolllerAllowance.FreeFirstDayBytes };
        var policy = (ScrolllerPolicy.Enabled, ScrolllerPolicy.MaxClipShortSide);
        ScrolllerPolicy.Apply(true, null);
        ScrolllerAllowance.ApplyLimits(0, 0, 0, 0); // live state stays spent when Assets refreshes the meter
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            shell.PaintOnlineMeterPill(State(100, spent: true));
            shell.UpdateLayout();
            var pill = shell.OnlinePill!;
            var at = pill.TranslatePoint(new Point(pill.Bounds.Width / 2, pill.Bounds.Height / 2), shell)!.Value;
            shell.MouseMove(at, RawInputModifiers.None);
            Assert.True(pill.IsPointerOver);
            Assert.NotNull(shell.OnlineHoverHelp);
            shell.MouseDown(at, MouseButton.Left, RawInputModifiers.None);
            shell.MouseUp(at, MouseButton.Left, RawInputModifiers.None);
            Assert.Null(shell.OnlineHoverHelp);
            Assert.Equal("assets", shell.CurrentTab);
            Assert.True(pill.IsPointerOver); // the pointer never left to trigger the hover leave timer
        }
        finally
        {
            shell.Close();
            ScrolllerAllowance.ApplyLimits(limits[0] / ScrolllerAllowance.MB, limits[1] / ScrolllerAllowance.MB,
                limits[2] / ScrolllerAllowance.MB, limits[3] / ScrolllerAllowance.MB);
            ScrolllerPolicy.Apply(policy.Enabled, policy.MaxClipShortSide);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Daily_share_help_pins_from_the_spent_card_and_contains_every_paragraph() => Run(() =>
    {
        CoreSettings.Current.Welcomed = true;
        CoreSettings.Current.HasAcceptedAgeVerification = true;
        CoreSettings.Current.MediaSource = "mixed";
        CoreSettings.Current.RemoteMediaConsented = true;
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            shell.PaintOnlineMeterPill(State(100, spent: true));
            shell.OpenOnlineMeterHover();
            Assert.NotNull(shell.OnlineHoverHelp);
            shell.OnlineHoverHelp!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<AssetsTabView>("AssetsTab")!;
            var help = tab.OnlineHelpButton!;
            Assert.True(ConditioningControlPanel.Avalonia.Views.Controls.Leash.LeashHelpButton.IsOpen(help));
            Assert.True(ConditioningControlPanel.Avalonia.Views.Controls.Leash.LeashHelpButton.IsPinned(help));
            var slip = ConditioningControlPanel.Avalonia.Views.Controls.Leash.LeashHelpButton.Slip(help)!;
            Assert.Equal("online-help-slip", slip.Tag);
            var text = string.Join("\n", global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(slip)
                .OfType<TextBlock>().Select(t => t.Text));
            foreach (var key in OnlineMeterRules.HelpParagraphKeys) Assert.Contains(Loc.Get(key), text);
            Assert.Null(shell.OnlineHoverHelp);
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
        {
            var fonts = AppFonts.Options();
            // Match the app's bundled fonts, including portable aliases for Windows-only faces.
            var mappings = new Dictionary<string, FontFamily>(fonts.FontFamilyMappings!);
            mappings["Segoe UI"] = mappings["Segoe UI Black"] = new FontFamily(AppFonts.Inter);
            fonts.FontFamilyMappings = mappings;
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(fonts).ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        }
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
