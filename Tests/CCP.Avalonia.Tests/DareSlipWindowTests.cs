using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.Automation;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.LogicalTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs.Dares;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Controls.Dares;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Dares;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

public sealed class DareSlipWindowTests
{
    private static void Init()
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().WithInterFont().With(AppFonts.Options())
                .ConfigureFonts(AppFonts.Register).UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Control? Find(Control root, string tag) => root.GetLogicalDescendants().OfType<Control>().FirstOrDefault(c => Equals(c.Tag, tag));
    private static void Click(Control root, string tag) => Assert.IsType<Button>(Find(root, tag)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public Task IntroThenComposeAndSendWritingSlip() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var service = new FakeService();
        var marked = 0;
        var friend = new DareFriend("friend", "Friend", null);
        var window = new DareSlipWindow(service, null, new[] { friend }, null, false, () => marked++);
        try
        {
            Assert.NotNull(Find(window, "dares-intro"));
            Click(window, "dares-intro-go");
            Assert.Equal(1, marked);
            Click(window, "dares-who:friend");
            Click(window, "dares-tile:duel");
            Click(window, "dares-tile:bubbles");
            Click(window, "dares-when:today");
            Click(window, "dares-stake:sp");
            var amount = DareGrammar.AmountsFor(DareStakeKind.Sp)[0];
            Click(window, "dares-amount:" + amount);
            Click(window, "dares-stake:sp");
            Click(window, "dares-amount:" + amount);
            Assert.NotNull(Find(window, "dares-send"));
            var result = await window.SendAsync();
            Assert.Equal(DareCreateStatus.Writing, result!.Status);
            Assert.Equal("friend", service.To);
            Assert.Equal(DareKind.Bubbles, service.Slip!.What.Kind);
            Assert.NotNull(Find(window, "dares-write-line"));
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task IntroDrawsBundledIconsNotAnIconFont() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Init();
        var intro = DareIntro.Build(() => { });
        Assert.Equal(new[] { IconKind.Mail, IconKind.ArrowRight, IconKind.ArrowSwap, IconKind.ArrowRight, IconKind.Clock, IconKind.ArrowRight, IconKind.Star },
            intro.GetLogicalDescendants().OfType<IconGlyph>().Select(g => g.Kind));
        Assert.DoesNotContain(intro.GetLogicalDescendants().OfType<TextBlock>(), t => t.FontFamily.Name.Contains("MDL2") || (t.Text ?? "").Any(c => c >= '\uE000' && c <= '\uF8FF'));
        return Task.CompletedTask;
    });

    [Fact]
    public Task ComposerDrawsTheSlipPressedTilesAndShortAmountReasons() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Init();
        var window = new DareSlipWindow(new FakeService { Sparkles = 60 }, new DareFriend("friend", "Friend", null), Array.Empty<DareFriend>(), null, true, () => { });
        try
        {
            Assert.Equal(DareSlipLook.SlipPaper, Assert.IsType<SolidColorBrush>(Assert.IsType<Border>(Find(window, "dares-slip")).Background).Color);
            Click(window, "dares-tile:duel");
            Assert.Contains(Find(window, "dares-chip:How")!.GetLogicalDescendants().OfType<TextBlock>(), t => t.FontFamily == DareSlipLook.Hand);
            Click(window, "dares-chip:How");
            Assert.Contains("on", Find(window, "dares-tile:duel")!.Classes);
            Assert.DoesNotContain("on", Find(window, "dares-tile:cant")!.Classes);
            foreach (var tag in new[] { "dares-tile:duel", "dares-tile:bubbles", "dares-when:today", "dares-stake:sp" }) Click(window, tag);
            var shortChip = Assert.IsType<Button>(Find(window, "dares-amount:100:short"));
            var have = Loc.GetF("dares_sp_have", 60);
            Assert.False(shortChip.IsEnabled);
            Assert.True(ToolTip.GetShowOnDisabled(shortChip));
            Assert.Equal(have, ToolTip.GetTip(shortChip));
            Assert.Equal(have, AutomationProperties.GetHelpText(shortChip));
            Assert.True(Assert.IsType<Button>(Find(window, "dares-amount:50")).IsEnabled);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task OutcomeLinesShowTheirWpfMarkers(bool duel) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Init();
        var window = new DareSlipWindow(new FakeService(), new DareFriend("friend", "  Zoe Star", null), Array.Empty<DareFriend>(), null, true, () => { });
        try
        {
            Click(window, duel ? "dares-tile:duel" : "dares-tile:cant");
            Click(window, duel ? "dares-tile:bubbles" : "dares-tile:dare");
            Click(window, "dares-when:today");
            Click(window, "dares-stake:sp");
            Click(window, "dares-amount:" + DareGrammar.AmountsFor(DareStakeKind.Sp)[0]);
            var kinds = duel ? new[] { DareMark.Me, DareMark.Them } : new[] { DareMark.Yes, DareMark.No };
            foreach (var kind in kinds)
            {
                var mark = Assert.IsType<Grid>(Find(window, "dares-mark:" + kind));
                Assert.Same(mark, Assert.IsType<WrapPanel>(mark.GetLogicalParent()).Children[0]);
                Assert.Equal(26, mark.Width);
                Assert.Equal(26, mark.Height);
                var disc = Assert.IsType<Ellipse>(mark.Children[0]);
                var expected = kind == DareMark.Yes ? 0x1FA877 : kind == DareMark.No ? 0xD2265F : 0x3A1D78;
                Assert.Equal(Color.FromRgb((byte)(expected >> 16), (byte)(expected >> 8), (byte)expected), Assert.IsType<SolidColorBrush>(disc.Fill).Color);
                if (kind is DareMark.Yes or DareMark.No)
                    Assert.Equal(kind == DareMark.Yes ? IconKind.Checkmark : IconKind.Dismiss, Assert.IsType<IconGlyph>(mark.Children[1]).Kind);
                else
                {
                    var text = Assert.IsType<TextBlock>(mark.Children[1]);
                    Assert.Equal(kind == DareMark.Me ? Loc.Get("dares_mark_me") : "Z", text.Text);
                    Assert.Equal(DareSlipLook.Hand, text.FontFamily);
                    Assert.Equal(kind == DareMark.Me ? Loc.Get("dares_mark_me") : "  Zoe Star", AutomationProperties.GetName(mark));
                }
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CameraExplanationShowsSampledTicksAndPassLabel() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Init();
        var window = new DareSlipWindow(new FakeService(), new DareFriend("friend", "Friend", null), Array.Empty<DareFriend>(), null, true, () => { });
        try
        {
            foreach (var tag in new[] { "dares-tile:cant", "dares-tile:pics", "dares-target:100", "dares-when:today",
                "dares-stake:sp", "dares-amount:50", "dares-stake:sp", "dares-amount:50" }) Click(window, tag);
            var box = Assert.IsType<Grid>(Find(window, "dares-camera-how"));
            var ticks = Assert.IsType<global::Avalonia.Controls.Primitives.UniformGrid>(Find(window, "dares-camera-ticks"));
            Assert.Equal(30, ticks.Columns);
            Assert.Equal(30, ticks.Children.Count);
            Assert.Equal(16, ticks.Height);
            var missed = new[] { 4, 11, 12, 20, 27 };
            for (int i = 0; i < ticks.Children.Count; i++)
            {
                var tick = Assert.IsType<Border>(ticks.Children[i]);
                if (missed.Contains(i)) Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(tick.Background).Color);
                else Assert.Equal(Color.FromRgb(0x2F, 0xD3, 0x9A), Assert.IsAssignableFrom<ISolidColorBrush>(tick.Background).Color);
                Assert.Equal(new Thickness(missed.Contains(i) ? 1 : 0), tick.BorderThickness);
            }
            Assert.Contains(box.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("dares_cam_pass"));
            Assert.Contains(box.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("dares_cam_look"));
            Assert.Contains(box.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("dares_cam_local"));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HeaderNumbersHelpCanBePinned() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Init();
        var window = new DareSlipWindow(new FakeService(), null, Array.Empty<DareFriend>(), null, true, () => { });
        window.Show();
        try
        {
            var help = Assert.IsType<Button>(Find(window, "dares-help:dares_help_numbers"));
            var explanation = Assert.IsType<TextBlock>(ToolTip.GetTip(help));
            Assert.Equal(Loc.Get("dares_help_numbers"), explanation.Text);
            Assert.Equal(TextWrapping.Wrap, explanation.TextWrapping);
            Assert.Equal(320, explanation.MaxWidth);
            Assert.Equal(Loc.Get("dares_help_numbers"), AutomationProperties.GetName(help));
            Click(window, "dares-help:dares_help_numbers");
            Assert.True(ToolTip.GetIsOpen(help));
            Click(window, "dares-help:dares_help_numbers");
            Assert.False(ToolTip.GetIsOpen(help));
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FriendPhotosLoadAndKeepInitialsOnFailure() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Init();
        byte[] png;
        using (var stream = global::Avalonia.Platform.AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/Phrase_Lock.png")))
        using (var memory = new MemoryStream()) { stream.CopyTo(memory); png = memory.ToArray(); }
        var urls = new List<string>();
        var previous = KnockCard.FetchPicture;
        KnockCard.FetchPicture = url => { urls.Add(url); return url.EndsWith("bad.png") ? Task.FromException<byte[]>(new IOException()) : Task.FromResult(png); };
        var friends = new[] { new DareFriend("one", "Alice", "/friends/avatar/alice.png"), new DareFriend("two", "Bob", "https://example.test/bob.png"),
            new DareFriend("three", "Cara", "/bad.png"), new DareFriend("four", "Dana", null) };
        var window = new DareSlipWindow(new FakeService(), null, friends, null, true, () => { });
        try
        {
            Assert.Contains("https://codebambi-proxy.vercel.app/friends/avatar/alice.png", urls);
            Assert.Contains("https://example.test/bob.png", urls);
            foreach (var id in new[] { "one", "two" })
                Assert.Contains(Find(window, "dares-who:" + id)!.GetLogicalDescendants().OfType<Ellipse>(), e => e.Opacity == 1 && e.Fill is ImageBrush);
            foreach (var (id, letter) in new[] { ("three", "C"), ("four", "D") })
            {
                var tile = Find(window, "dares-who:" + id)!;
                Assert.DoesNotContain(tile.GetLogicalDescendants().OfType<Ellipse>(), e => e.Opacity == 1 && e.Fill is ImageBrush);
                Assert.Contains(tile.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == letter);
            }
        }
        finally { window.Close(); KnockCard.FetchPicture = previous; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task CloseFadesOutWithMotionAndClosesAtOnceWithout() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Init();
        var s = ConditioningControlPanel.CoreSettings.Current;
        var old = (s.MotionLevel, s.GentleVisuals, s.PerformanceMode);
        var windows = new List<Window>();
        try
        {
            (s.MotionLevel, s.GentleVisuals, s.PerformanceMode) = (ConditioningControlPanel.Models.MotionLevel.Off, false, false);
            var still = new DareSlipWindow(new FakeService(), null, Array.Empty<DareFriend>(), null, true, () => { });
            windows.Add(still); still.Show();
            var stillClosed = false; still.Closed += (_, _) => stillClosed = true;
            Click(still, "dares-close");
            Assert.True(stillClosed); // WPF DareLook.cs:1170: no motion, close now

            s.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
            var moving = new DareSlipWindow(new FakeService(), null, Array.Empty<DareFriend>(), null, true, () => { });
            windows.Add(moving); moving.Show();
            var closed = false; moving.Closed += (_, _) => closed = true;
            Click(moving, "dares-close");
            Assert.False(closed); // WPF DareLook.cs:1171-1173: fade 160 ms, then close
            for (var i = 0; i < 30 && !closed; i++) { AvaloniaHeadlessPlatform.ForceRenderTimerTick(16); Dispatcher.UIThread.RunJobs(); }
            Assert.True(closed);
        }
        finally
        {
            (s.MotionLevel, s.GentleVisuals, s.PerformanceMode) = old;
            foreach (var w in windows) w.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FriendsMenuOpensSlipOnlyWhenDaresAreAvailable() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Init();
        var (friends, _) = FriendsDrawerTests.Service();
        var service = new FakeService();
        var drawer = new FriendsDrawer(friends);
        var source = typeof(FriendsDrawer).GetProperty("DaresSource", BindingFlags.Instance | BindingFlags.NonPublic);
        var openField = typeof(DareSlipWindow).GetField("_open", BindingFlags.Static | BindingFlags.NonPublic)!;
        var before = (DareSlipWindow?)openField.GetValue(null);
        DareSlipWindow? opened = null;
        try
        {
            Assert.Null(before);
            Assert.NotNull(source);
            source!.SetValue(drawer, (Func<IDaresService?>)(() => service));
            await friends.RefreshAsync();
            drawer.Render();
            MenuItem? Dare() => Assert.IsType<Border>(Find(drawer, "friends-row:u_on")).ContextMenu!.ItemsSource!
                .OfType<MenuItem>().FirstOrDefault(item => Equals(item.Tag, "friends-menu-item:dare"));
            var item = Assert.IsType<MenuItem>(Dare());
            Assert.Equal(Loc.Get("dares_menu_dare"), item.Header);
            Assert.Equal(IconKind.Mail, Assert.IsType<IconGlyph>(item.Icon).Kind);
            Assert.True(item.IsEnabled);
            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            opened = (DareSlipWindow?)openField.GetValue(null);
            Assert.NotNull(opened);
            Assert.Equal("u_on", opened!.Draft.Who!.Id);
            // Skip the intro without writing its marker file in the test profile.
            typeof(DareSlipWindow).GetMethod("ShowComposer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(opened, null);
            foreach (var tag in new[] { "dares-tile:duel", "dares-tile:bubbles", "dares-when:today", "dares-stake:sp", "dares-amount:50", "dares-stake:sp", "dares-amount:50" }) Click(opened, tag);
            Assert.Equal(DareCreateStatus.Writing, (await opened.SendAsync())!.Status);
            Assert.Equal("u_on", service.To);
            Assert.Equal(DareKind.Bubbles, service.Slip!.What.Kind);
            opened.Close(); opened = null;
            service.Available = false; drawer.Render();
            Assert.Null(Dare());
            service.Available = true; service.BannedUntil = DateTimeOffset.UtcNow.AddDays(1); drawer.Render();
            Assert.False(Assert.IsType<MenuItem>(Dare()).IsEnabled);
        }
        finally
        {
            opened?.Close();
            if (before == null) (openField.GetValue(null) as DareSlipWindow)?.Close();
            drawer.Unsubscribe(); friends.Dispose();
        }
    });

    private sealed class FakeService : IDaresService
    {
        public int? Sparkles { get; init; }
        public bool Available { get; set; } = true;
        public DateTimeOffset? BannedUntil { get; set; }
        public event Action? Changed { add { } remove { } }
        public DaresSnapshot Snapshot => new(Available, new DaresMe(BannedUntil, true, true, 0), Array.Empty<DareBet>());
        public bool IsPreview => false;
        public string? To { get; private set; }
        public DareSlip? Slip { get; private set; }
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<DareCreateOutcome> CreateAsync(string friendId, DareSlip slip, CancellationToken ct = default)
        { To = friendId; Slip = slip; return Task.FromResult(new DareCreateOutcome(DareCreateStatus.Writing, WriteCode: "ABC")); }
        public Task<DareActResult> ActAsync(string betId, DareOp op, CancellationToken ct = default) => Task.FromResult(DareActResult.Done);
    }
}
