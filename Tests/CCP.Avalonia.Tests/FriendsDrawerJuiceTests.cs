using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class FriendsDrawerTests
{
    // Every drawer test: request rows measure their pills, which needs a font manager whatever test ran first.
    private static void JuiceApp()
    {
        if (Application.Current == null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }
    private static void Step(FriendsDrawer drawer, double ms) => drawer.AdvanceJuice(TimeSpan.FromMilliseconds(ms));

    [Theory]
    [InlineData(MotionLevel.Full, 14, 0.94, 220)]
    [InlineData(MotionLevel.Reduced, 7, 0.97, 352)]
    [InlineData(MotionLevel.Off, 0, 1, 0)]
    public Task FriendsPageEntranceAndRowsRespectMotion(MotionLevel level, double rise, double scale, double duration) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            JuiceApp();
            var previous = (CoreSettings.Current.MotionLevel, CoreSettings.Current.PerformanceMode);
            (CoreSettings.Current.MotionLevel, CoreSettings.Current.PerformanceMode) = (level, false);
            var (svc, _) = Service();
            await svc.RefreshAsync();
            var page = new FriendsTabView(svc);
            var host = new Window { Content = page, Width = 800, Height = 700 };
            var d = page.Drawer;
            try
            {
                host.Show();
                page.OnShown(); // same path as ShowTab("friends")
                var rows = new[] { "friends-row:u_on", "friends-request-in:u_req", "friends-row:u_off" }
                    .Select(tag => Tagged<Border>(d, tag)!).ToArray();
                if (level == MotionLevel.Off)
                {
                    Assert.Null(d.RenderTransform);
                    Assert.All(rows, row => Assert.Equal(1, row.Opacity));
                    Assert.False(d.JuiceRunning);
                    return;
                }
                var group = Assert.IsType<TransformGroup>(d.RenderTransform);
                Assert.Equal(scale, Assert.IsType<ScaleTransform>(group.Children[0]).ScaleX, 6);
                Assert.Equal(rise, Assert.IsType<TranslateTransform>(group.Children[1]).Y, 6);
                Assert.Equal(new RelativePoint(0, 1, RelativeUnit.Relative), d.RenderTransformOrigin);
                Assert.Equal(0, d.Opacity);
                Step(d, 40);
                Assert.True(rows[0].Opacity > 0);
                Assert.Equal(0, rows[1].Opacity);
                Assert.Equal(0, rows[2].Opacity);
                Step(d, 40);
                Assert.True(rows[1].Opacity > 0);
                Assert.Equal(0, rows[2].Opacity);
                Step(d, duration + 400);
                Assert.Equal(1, d.Opacity);
                Assert.All(rows, row => Assert.Equal(1, row.Opacity));
                Assert.Equal(0, Assert.IsType<TranslateTransform>(group.Children[1]).Y, 6);
                Assert.Equal(level == MotionLevel.Full, d.JuiceRunning); // only the online dot remains
                d.Toggle("u_on");
                Assert.Equal(0, Tagged<Border>(d, "friends-row:u_on")!.Opacity);
                Step(d, 260);
                Assert.Equal(1, Tagged<Border>(d, "friends-row:u_on")!.Opacity);
            }
            finally { page.OnHidden(); host.Close(); (CoreSettings.Current.MotionLevel, CoreSettings.Current.PerformanceMode) = previous; }
        });

    [Fact]
    public Task FriendsPageSheenBreathAndOnlineHopStopOnHideDetachAndPanic() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        JuiceApp();
        var before = (CoreSettings.Current.MotionLevel, CoreSettings.Current.PerformanceMode);
        (CoreSettings.Current.MotionLevel, CoreSettings.Current.PerformanceMode) = (MotionLevel.Full, false);
        var (svc, wire) = Service();
        await svc.RefreshAsync();
        var page = new FriendsTabView(svc);
        var host = new Window { Content = page, Width = 800, Height = 700 };
        var d = page.Drawer;
        try
        {
            host.Show(); page.OnShown(); Step(d, 1000);
            var dot = Tagged<Ellipse>(d, "friends-dot-on")!;
            Assert.InRange(dot.Opacity, 0.45, 1);
            double first = dot.Opacity;
            Step(d, 400);
            Assert.NotEqual(first, dot.Opacity);
            foreach (var tag in new[] { "friends-row:u_on", "friends-request-in:u_req" })
            {
                var row = Tagged<Border>(d, tag)!;
                row.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, row, null!, row, default, 0, default, default));
                Assert.Equal(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), Assert.IsType<SolidColorBrush>(row.Background).Color);
                var sheen = Tagged<Rectangle>(row, "friends-sheen");
                Assert.NotNull(sheen);
                Step(d, 260);
                Assert.True(Assert.IsType<TranslateTransform>(sheen!.RenderTransform).X > -90);
                Step(d, 260);
                Assert.Null(Tagged<Rectangle>(row, "friends-sheen"));
            }
            wire.StateBody = State.Replace("\"id\":\"u_off\",\"name\":\"Zed\",\"tier\":0,\"online\":false", "\"id\":\"u_off\",\"name\":\"Zed\",\"tier\":0,\"online\":true");
            await svc.RefreshAsync();
            var avatar = Tagged<Border>(d, "friends-row:u_off")!.GetLogicalDescendants().OfType<Grid>().First(g => g.Width == 38);
            Step(d, 170);
            Assert.Equal(-10, Assert.IsType<TranslateTransform>(avatar.RenderTransform).Y, 5);
            Assert.NotEmpty(Tagged<Canvas>(d, "friends-fx")!.Children);
            Step(d, 600);
            Assert.Equal(0, Assert.IsType<TranslateTransform>(avatar.RenderTransform).Y, 5);
            Assert.Empty(Tagged<Canvas>(d, "friends-fx")!.Children);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(d.JuiceRunning);
            Assert.Equal(1, Tagged<Ellipse>(d, "friends-dot-on")!.Opacity);
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(d.JuiceRunning);
            Assert.Single(PanicSurfaces.All, s => s.Id == "friends-motion").Stop(null);
            Assert.False(d.JuiceRunning);
            d.Render(); // a service repaint must not restart panic-parked motion
            Assert.False(d.JuiceRunning);
            page.OnShown();
            Assert.True(d.JuiceRunning);
            page.OnHidden();
            Assert.False(d.JuiceRunning);
            Assert.Equal(1, d.Opacity);
            page.OnShown();
            host.Content = null;
            Assert.False(d.JuiceRunning);
        }
        finally { page.OnHidden(); host.Close(); (CoreSettings.Current.MotionLevel, CoreSettings.Current.PerformanceMode) = before; }
    });

    [Fact]
    public Task FriendsPickerClicksBurstAndOnlySuccessfulAddAndAcceptCheer() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        JuiceApp();
        var before = CoreSettings.Current.MotionLevel;
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        var (svc, wire) = Service();
        await svc.RefreshAsync();
        var page = new FriendsTabView(svc);
        var host = new Window { Content = page, Width = 800, Height = 700 };
        var d = page.Drawer;
        try
        {
            host.Show(); page.OnShown(); Step(d, 1000);
            var fx = Tagged<Canvas>(d, "friends-fx")!;
            d.Toggle("u_on"); Click(d, "friends-action:poke");
            var chip = Tagged<Button>(d, "friends-poke:" + PokeSet.Shipped[0])!;
            wire.HoldSends = new TaskCompletionSource();
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(6, fx.Children.Count);
            Step(d, 110);
            Assert.Equal(1.11, Assert.IsType<ScaleTransform>(chip.RenderTransform).ScaleX, 6);
            wire.HoldSends.TrySetResult();
            await Until(() => Tagged<TextBlock>(d, "friends-result") != null);
            Step(d, 1000);
            Assert.Empty(fx.Children);
            foreach (var picker in new[] { "invite", "watch" })
            {
                d.OpenPickerFor("u_on", picker);
                var tag = picker == "invite" ? "friends-invite:backroom" : "friends-flavour:trance";
                Click(d, tag);
                Assert.NotEmpty(fx.Children);
                Step(d, 1600);
            }
            d.ToggleAddBox(true);
            Assert.Equal(0, Tagged<TextBox>(d, "friends-code-box")!.GetLogicalAncestors().OfType<Border>().First(b => b.RenderTransform is TranslateTransform).Opacity);
            Step(d, 260);
            wire.MutationBody = """{"ok":true,"status":"sent"}""";
            Assert.Equal(AddResult.Sent, await d.AddByCodeAsync("FGHJK"));
            Assert.Equal(2, fx.Children.OfType<Ellipse>().Count());
            Assert.Equal(7, fx.Children.OfType<Border>().Count());
            var rings = fx.Children.OfType<Ellipse>().ToArray();
            Assert.Equal(0, rings[1].Opacity);
            Step(d, 120);
            Assert.True(rings[1].Opacity > 0);
            Step(d, 1500);
            Assert.Empty(fx.Children);
            Click(d, "friends-accept");
            await Until(() => fx.Children.OfType<Ellipse>().Any());
            Assert.Equal(2, fx.Children.OfType<Ellipse>().Count());
            Step(d, 1600);
            wire.MutationBody = """{"ok":false,"reason":"try_later"}""";
            Assert.Equal(AddResult.TryLater, await d.AddByCodeAsync("FGHJK"));
            Assert.Equal(ActResult.TryLater, await d.AnswerRequestAsync(svc.Snapshot.Incoming[0], "accept", Tagged<Button>(d, "friends-accept")));
            Assert.Empty(fx.Children);
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            d.OpenPickerFor("u_on", "poke");
            Click(d, "friends-poke:" + PokeSet.Shipped[1]);
            Assert.Empty(fx.Children);
            Assert.NotEmpty(wire.Sends);
        }
        finally { page.OnHidden(); host.Close(); CoreSettings.Current.MotionLevel = before; }
    });


    [Fact]
    public Task FriendsLongListStaggerCapsAtSixSlots() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        JuiceApp();
        var before = CoreSettings.Current.MotionLevel;
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        var (svc, wire) = Service();
        var state = System.Text.Json.Nodes.JsonNode.Parse(State)!;
        state["friends"] = System.Text.Json.JsonSerializer.SerializeToNode(Enumerable.Range(0, 8).Select(i =>
            new { id = "f" + i, name = "Friend" + i, tier = 0, online = true, activity = "panel", squelched = false }));
        wire.StateBody = state.ToJsonString();
        await svc.RefreshAsync();
        var d = new FriendsTabView(svc).Drawer;
        try
        {
            d.OnOpened();
            Step(d, 240);
            Assert.Equal(0, Tagged<Border>(d, "friends-row:f6")!.Opacity);
            Assert.Equal(0, Tagged<Border>(d, "friends-row:f7")!.Opacity);
            Step(d, 40);
            Assert.True(Tagged<Border>(d, "friends-row:f6")!.Opacity > 0);
            Assert.Equal(Tagged<Border>(d, "friends-row:f6")!.Opacity, Tagged<Border>(d, "friends-row:f7")!.Opacity);
            Step(d, 500);
            Assert.False(d.JuiceRunning);
        }
        finally { d.OnClosed(); CoreSettings.Current.MotionLevel = before; }
    });

    [Theory]
    [InlineData("flavour")]
    [InlineData("catalogue")]
    [InlineData("ht")]
    public Task FriendsWatchSendControlsPopBeforeTheReply(string tab) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        JuiceApp();
        var before = CoreSettings.Current.MotionLevel;
        var catalogue = FriendsInviteCodes.CatalogueWatches;
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        FriendsInviteCodes.CatalogueWatches = () => new[] { ("watch-test", "Test watch") };
        var (svc, wire) = Service();
        wire.HoldSends = new TaskCompletionSource();
        await svc.RefreshAsync();
        var d = new FriendsDrawer(svc);
        var host = new Window { Content = d, Width = 800, Height = 700 };
        try
        {
            host.Show(); d.OnOpened(); Step(d, 1000);
            d.Toggle("u_on"); Click(d, "friends-action:watch");
            Click(d, "friends-watch-tab:" + tab);
            if (tab == "ht") Tagged<TextBox>(d, "friends-ht-box")!.Text = "12345";
            var button = Tagged<Button>(d, tab switch
            {
                "flavour" => "friends-flavour:trance", "catalogue" => "friends-catalogue:watch-test", _ => "friends-ht-send",
            })!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Step(d, 110);
            Assert.Equal(1.11, Assert.IsType<ScaleTransform>(button.RenderTransform).ScaleX, 6);
            Assert.NotEmpty(Tagged<Canvas>(d, "friends-fx")!.Children);
            wire.HoldSends.TrySetResult();
            await Until(() => Tagged<TextBlock>(d, "friends-result") != null);
            Step(d, 1000);
            Assert.Empty(Tagged<Canvas>(d, "friends-fx")!.Children);
        }
        finally
        {
            wire.HoldSends.TrySetResult(); d.OnClosed(); host.Close();
            CoreSettings.Current.MotionLevel = before;
            FriendsInviteCodes.CatalogueWatches = catalogue;
        }
    });

    // Regression: the copy label previously stayed on "Copied" indefinitely.
    [Fact]
    public Task FriendsCopiedCodeReturnsToItsLabelAfterTwoSeconds() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        JuiceApp();
        var motion = CoreSettings.Current.MotionLevel;
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
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
            Assert.NotEmpty(Tagged<Canvas>(d, "friends-fx")!.Children);
            timers.Step(1900);
            Assert.Equal(Loc.Get("friends_copied"), label.Text);
            timers.Step(200);
            Assert.Equal(Loc.Get("friends_my_code"), label.Text);
        }
        finally { d.OnClosed(); host.Close(); CoreSettings.Current.MotionLevel = motion; }
    });

    [Fact]
    public Task FriendsIncomingRequestMenuReportsInItsRowAndBlocksWithoutAConfirm() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        JuiceApp();
        var (svc, wire) = Service();
        await svc.RefreshAsync();
        var d = new FriendsDrawer(svc);
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
        wire.MutationBody = """{"ok":false,"reason":"try_later"}""";
        Assert.Equal(ActResult.TryLater, await d.BlockRequesterAsync(svc.Snapshot.Incoming[0]));
        Assert.Equal(Loc.Get(FriendsDrawerRules.ActResultKey(ActResult.TryLater)), Tagged<TextBlock>(d, "friends-result")!.Text);
        wire.MutationBody = """{"ok":true}""";
        Assert.Equal(ActResult.Done, await d.BlockRequesterAsync(svc.Snapshot.Incoming[0]));
        Assert.Contains("block", wire.Ops);
        Assert.Null(Tagged<Border>(d, "friends-confirm:block"));
    });


    [Fact]
    public Task FriendsRepeatedResultsKeepTheFullReadTime() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        JuiceApp();
        var (svc, wire) = Service();
        await svc.RefreshAsync();
        wire.MutationBody = """{"ok":false,"reason":"try_later"}""";
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
        JuiceApp();
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

    private sealed class PhotoWire(byte[] bytes) : HttpMessageHandler
    {
        internal int Calls;
        internal Uri? Last;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++; Last = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(request.RequestUri!.AbsolutePath.EndsWith("missing") ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                { Content = new ByteArrayContent(bytes) });
        }
    }

    [Fact]
    public Task FriendsPhotosUseHttpsProxyCacheAndKeepInitialsOnFailure() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        JuiceApp();
        using var bitmap = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96));
        using var stream = new System.IO.MemoryStream();
        bitmap.Save(stream);
        var wire = new PhotoWire(stream.ToArray());
        using var http = new HttpClient(wire);
        var previous = FriendsDrawer.PhotoHttp;
        FriendsDrawer.PhotoHttp = http;
        try
        {
            string path = "/test-avatar-" + Guid.NewGuid().ToString("N");
            var avatar = (Grid)FriendsDrawer.Avatar("Test", 38, true, path);
            var disc = (Border)avatar.Children[0];
            await Until(() => disc.Child == null);
            Assert.IsType<ImageBrush>(disc.Background);
            Assert.Equal("https", wire.Last!.Scheme);
            Assert.Equal(path, wire.Last.AbsolutePath);
            FriendsDrawer.Avatar("Test", 38, null, path);
            Assert.Equal(1, wire.Calls);
            var unsafeAvatar = (Grid)FriendsDrawer.Avatar("Test", 38, null, "http://invalid.example/avatar");
            Assert.IsType<TextBlock>(((Border)unsafeAvatar.Children[0]).Child);
            var missing = (Grid)FriendsDrawer.Avatar("Test", 38, null, path + "/missing");
            await Until(() => wire.Calls == 2);
            Assert.IsType<TextBlock>(((Border)missing.Children[0]).Child);
        }
        finally { FriendsDrawer.PhotoHttp = previous; }
    });
}
