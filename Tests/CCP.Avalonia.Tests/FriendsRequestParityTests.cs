using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsRequestParityTests
{
    private static void SetupApp()
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont().With(AppFonts.Options())
                .ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static (FriendsService Svc, FriendsDrawerTests.Wire Wire) Service() => FriendsDrawerTests.Service();
    private static T? Tagged<T>(Control root, string tag) where T : Control =>
        root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Tag as string == tag);
    private static void Click(Control root, string tag) => Tagged<Button>(root, tag)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Task Until(Func<bool> done) => FriendsDrawerTests.Until(done);
    // Regression: the copy label previously stayed on "Copied" indefinitely.
    [Fact]
    public Task FriendsCopiedCodeReturnsToItsLabelAfterTwoSeconds() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SetupApp();
        var (svc, _) = Service();
        await svc.RefreshAsync();
        var d = new FriendsDrawer(svc);
        var timers = new StepTimers();
        d.After = timers.After;
        var host = new Window { Content = d, Width = 500, Height = 650 };
        try
        {
            host.Show(); d.OnOpened();
            var label = Tagged<TextBlock>(d, "friends-copy-label")!;
            Click(d, "friends-copy");
            await Until(() => label.Text == Loc.Get("friends_copied"));
            Assert.Equal(Loc.Get("friends_copied"), label.Text);
            timers.Step(1900);
            Assert.Equal(Loc.Get("friends_copied"), label.Text);
            timers.Step(200);
            Assert.Equal(Loc.Get("friends_my_code"), label.Text);
        }
        finally { d.OnClosed(); host.Close(); }
    });

    [Fact]
    public Task IncomingMoreOpensTheRequestMenu() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SetupApp();
        var (svc, _) = Service();
        await svc.RefreshAsync();
        var d = new FriendsDrawer(svc);
        var host = new Window { Content = d };
        try
        {
            host.Show();
            var row = Tagged<Border>(d, "friends-request-in:u_req")!;
            Click(row, "friends-request-more");
            var menu = Tagged<Button>(row, "friends-request-more")!.ContextMenu!;
            Assert.True(menu.IsOpen);
            Assert.Equal(2, menu.Items.Count);
            menu.Close();
        }
        finally { d.OnClosed(); host.Close(); }
    });

    [Fact]
    public Task FriendsIncomingRequestMenuReportsInItsRowAndBlocksWithoutAConfirm() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SetupApp();
        var (svc, wire) = Service();
        await svc.RefreshAsync();
        var d = new FriendsDrawer(svc) { After = new StepTimers().After };
        var row = Tagged<Border>(d, "friends-request-in:u_req")!;
        Assert.NotNull(Tagged<Button>(row, "friends-request-more"));
        var menu = row.ContextMenu!;
        var entries = menu.Items.Cast<MenuItem>().ToArray();
        Assert.Equal(new[] { "friends-menu-item:block", "friends-menu-item:report" }, entries.Select(m => m.Tag));
        var reports = entries[1].Items.Cast<MenuItem>().ToArray();
        Assert.Equal(ReportReason.All.Count, reports.Length);
        reports[0].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await Until(() => Tagged<TextBlock>(d, "friends-result") != null);
        Assert.Equal(Loc.Get("friends_report_done"), Tagged<TextBlock>(d, "friends-result")!.Text);
        wire.ActSucceeds = false;
        Assert.Equal(ActResult.TryLater, await d.BlockRequesterAsync(svc.Snapshot.Incoming[0]));
        Assert.Equal(Loc.Get(FriendsDrawerRules.ActResultKey(ActResult.TryLater)), Tagged<TextBlock>(d, "friends-result")!.Text);
        wire.ActSucceeds = true;
        Assert.Equal(ActResult.Done, await d.BlockRequesterAsync(svc.Snapshot.Incoming[0]));
        Assert.Contains("block", wire.Ops);
        Assert.Null(Tagged<Border>(d, "friends-confirm:block"));
    });


    [Fact]
    public Task FriendsRepeatedResultsKeepTheFullReadTime() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SetupApp();
        var (svc, wire) = Service();
        await svc.RefreshAsync();
        wire.ActSucceeds = false;
        var d = new FriendsDrawer(svc);
        var timers = new StepTimers();
        d.After = timers.After;
        var request = svc.Snapshot.Incoming[0];
        await d.AnswerRequestAsync(request, "accept");
        timers.Step(3000);
        await d.AnswerRequestAsync(request, "accept");
        timers.Step(2300);
        Assert.NotNull(Tagged<TextBlock>(d, "friends-result"));
        timers.Step(3000);
        Assert.Null(Tagged<TextBlock>(d, "friends-result"));
    });

    /// <summary>Stepped one-shot timers for FriendsDrawer.After (P08: no real waits).</summary>
    private sealed class StepTimers
    {
        private readonly List<(TimeSpan Due, Action Fire, bool[] Dead)> _queue = new();
        private TimeSpan _now;
        public IDisposable After(Action fire, TimeSpan delay)
        {
            var dead = new bool[1];
            _queue.Add((_now + delay, fire, dead));
            return new Off(dead);
        }
        public void Step(double ms)
        {
            _now += TimeSpan.FromMilliseconds(ms);
            foreach (var t in _queue.Where(t => t.Due <= _now && !t.Dead[0]).ToList()) { t.Dead[0] = true; t.Fire(); }
        }
        private sealed class Off(bool[] dead) : IDisposable { public void Dispose() => dead[0] = true; }
    }

    [Fact]
    public Task FriendsGermanRequestPillsLeaveRoomForTheAge() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SetupApp();
        string previous = LocalizationManager.Instance.CurrentLanguage;
        try
        {
            LocalizationManager.Instance.SetLanguage("de");
            var (svc, _) = Service();
            await svc.RefreshAsync();
            var d = new FriendsDrawer(svc);
            Assert.Equal(new Thickness(5, 3, 5, 3), Tagged<Button>(d, "friends-accept")!.Padding);
            Assert.Equal(new Thickness(5, 3, 5, 3), Tagged<Button>(d, "friends-decline")!.Padding);
        }
        finally { LocalizationManager.Instance.SetLanguage(previous); }
    });

    [Theory]
    [InlineData("friends-row:u_on")]
    [InlineData("friends-request-in:u_req")]
    public Task RowsShowAndClearTheHoverWash(string tag) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SetupApp();
        var (svc, _) = Service();
        await svc.RefreshAsync();
        var d = new FriendsDrawer(svc);
        var row = Tagged<Border>(d, tag)!;
        var rest = row.Background;
        row.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, row,
            new Pointer(1, PointerType.Mouse, true), row, default, 0, new PointerPointProperties(), KeyModifiers.None));
        Assert.Equal(Color.Parse("#14FFFFFF"), Assert.IsType<SolidColorBrush>(row.Background).Color);
        row.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, row,
            new Pointer(1, PointerType.Mouse, true), row, default, 0, new PointerPointProperties(), KeyModifiers.None));
        Assert.Same(rest, row.Background);
    });
    [Fact]
    public Task RequestPaddingRespondsToRealPointerMovement() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SetupApp();
        var (svc, _) = Service();
        await svc.RefreshAsync();
        var d = new FriendsDrawer(svc);
        var host = new Window
        {
            Width = 400, Height = 700,
            Content = new Border { Padding = new Thickness(20), Child = d },
        };
        try
        {
            host.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            using var frame = host.CaptureRenderedFrame();
            var row = Tagged<Border>(d, "friends-request-in:u_req")!;
            // The left padding, clear of content and rounded corners (WPF FriendsDrawer.cs:752).
            var padding = new Point(2, row.Bounds.Height / 2);
            Assert.True(row.Bounds.Height > 12);
            Assert.True(padding.X < row.Padding.Left);
            var at = row.TranslatePoint(padding, host)!.Value;
            host.MouseMove(new Point(2, 2));
            host.MouseMove(at);
            Assert.True(row.IsPointerOver);
            Assert.Equal(Color.Parse("#14FFFFFF"), Assert.IsType<SolidColorBrush>(row.Background).Color);
            host.MouseMove(new Point(2, 2));
            Assert.False(row.IsPointerOver);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(row.Background).Color);
        }
        finally { d.OnClosed(); host.Close(); }
    });

}
