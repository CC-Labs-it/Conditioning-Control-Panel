using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class FriendsLinkPortTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private const string Token = "FAKEFAKEFAKE";
    private const string State = """{"ok":true,"code":"CCP-ABCDE","friends":[],"incoming":[],"outgoing":[],"blocked":[]}""";
    private sealed class Wire : HttpMessageHandler
    {
        public readonly List<string> Ops = new();
        public string StateBody = State, Claim = "friends", TokenBody = Token;
        public bool Offline;
        public TaskCompletionSource? HoldLink;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var op = r.RequestUri!.AbsolutePath.Split('/').Last();
            Ops.Add(op);
            if (op == "link" && HoldLink != null) await HoldLink.Task;
            var body = op switch
            {
                "state" => StateBody,
                "link" => Offline ? "{}" : "{\"ok\":true,\"token\":\"" + TokenBody + "\",\"expires_at\":\"2026-10-24T12:00:00Z\",\"uses_left\":7}",
                "claim" => "{\"ok\":true,\"status\":\"" + Claim + "\"}",
                _ => "{\"ok\":true}",
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }
    private static (FriendsService, Wire) Service()
    {
        var wire = new Wire();
        return (new FriendsService(new FriendsApi(new HttpClient(wire), () => ("me", "fake-token"), "http://127.0.0.1:9"),
            () => "me", () => true, () => Now, () => false, _ => { }), wire);
    }
    private static T? Tag<T>(Control root, string tag) where T : Control => root.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Tag as string == tag);
    private static void Click(Control root, string tag) => Tag<Button>(root, tag)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task Until(Func<bool> done)
    {
        var end = DateTime.UtcNow.AddSeconds(10);
        while (!done() && DateTime.UtcNow < end) await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        Assert.True(done());
    }

    private static Task Run(Func<Task> action) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current == null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        await action();
    });

    [Fact]
    public Task DrawerOffersCopyAndNewLinkAndAcceptsPastedTokens() => Run(async () =>
    {
        var (svc, _) = Service();
        await svc.RefreshAsync();
        var drawer = new FriendsDrawer(svc);
        Assert.NotNull(Tag<Button>(drawer, "friends-copy-link"));
        drawer.ToggleAddBox(true);
        Assert.NotNull(Tag<Button>(drawer, "friends-new-link"));
        var box = Tag<TextBox>(drawer, "friends-code-box")!;
        box.Text = "Add me: (https://app.cclabs.app/f/FAKEFAKEFAKE).";
        await Until(() => box.Text == Token);
        Assert.Equal(Token, box.Text);
        Assert.True(Tag<Button>(drawer, "friends-add-go")!.IsEnabled);
        Assert.False(Tag<TextBlock>(drawer, "friends-code-prefix")!.IsVisible);
        box.Text = "https://elsewhere.example/f/FAKEFAKEFAKE";
        await Until(() => box.Text == "");
        Assert.Equal("", box.Text);
        Assert.False(Tag<Button>(drawer, "friends-add-go")!.IsEnabled);
        box.Text = "CCP-ABCDE";
        await Until(() => box.Text == "ABCDE");
        Assert.Equal("ABCDE", box.Text);
        Assert.True(Tag<TextBlock>(drawer, "friends-code-prefix")!.IsVisible);
        box.Text = "";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        foreach (var c in "CCPABCDEFGHJ")
        {
            box.Text += c;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        }
        Assert.Equal("CCPABCDEFGHJ", box.Text); // hand-typed CCP remains a token
        Assert.True(Tag<Button>(drawer, "friends-add-go")!.IsEnabled);
    });

    [Fact]
    public Task CopyAndNewButtonsUseTheLiveCoreLinkAndActualRemainingNote() => Run(async () =>
    {
        var (svc, wire) = Service();
        await svc.RefreshAsync();
        var copied = new List<string>();
        var drawer = new FriendsDrawer(svc) { LinkClock = () => Now, CopyText = text => { copied.Add(text); return Task.FromResult(true); } };
        Click(drawer, "friends-copy-link");
        await Until(() => drawer.LinkNoteText.Length > 0);
        Assert.Equal(FriendsLinkRules.UrlFor(Token), Assert.Single(copied));
        Assert.Equal(Loc.GetF("friends_link_copied_left", 2, 7), drawer.LinkNoteText);
        Assert.True(await drawer.CopyLinkAsync());
        Assert.Equal(2, wire.Ops.Count(o => o == "link")); // every press asks the server
        wire.TokenBody = "TESTTESTTEST";
        drawer.ToggleAddBox(true);
        Click(drawer, "friends-new-link");
        await Until(() => copied.Count == 3);
        Assert.Equal(FriendsLinkRules.UrlFor(wire.TokenBody), copied.Last());
        Assert.Equal(new[] { "link_revoke", "link" }, wire.Ops.TakeLast(2));
        Assert.Equal(Loc.Get("friends_link_new_done"), Tag<TextBlock>(drawer, "friends-add-result")!.Text);
    });

    [Fact]
    public Task CopyFallsBackToCodeAndReportsClipboardFailureWithoutSuccess() => Run(async () =>
    {
        var (svc, wire) = Service();
        wire.Offline = true;
        await svc.RefreshAsync();
        string? copied = null;
        var drawer = new FriendsDrawer(svc) { CopyText = text => { copied = text; return Task.FromResult(true); } };
        Assert.False(await drawer.CopyLinkAsync());
        Assert.Equal("CCP-ABCDE", copied);
        Assert.Equal(Loc.Get("friends_link_offline"), drawer.LinkNoteText);
        drawer.CopyText = _ => Task.FromResult(false);
        Assert.False(await drawer.CopyLinkAsync());
        Assert.Equal(Loc.Get("friends_add_try_later"), drawer.LinkNoteText);
    });

    [Fact]
    public Task CopyAndRevokeNeverOverlap() => Run(async () =>
    {
        var (svc, wire) = Service();
        wire.HoldLink = new TaskCompletionSource();
        var drawer = new FriendsDrawer(svc) { CopyText = _ => Task.FromResult(true), LinkClock = () => Now };
        var first = drawer.CopyLinkAsync();
        await Until(() => wire.Ops.Contains("link"));
        Assert.False(await drawer.NewLinkAsync());
        Assert.False(await drawer.CopyLinkAsync());
        Assert.Equal(new[] { "link" }, wire.Ops);
        wire.HoldLink.SetResult();
        Assert.True(await first);
    });

    [Fact]
    public Task ClaimButtonRefreshesExactlyOnceAndFailedClaimsStayRetryable() => Run(async () =>
    {
        var (svc, wire) = Service();
        var drawer = new FriendsDrawer(svc);
        drawer.ToggleAddBox(true);
        var box = Tag<TextBox>(drawer, "friends-code-box")!;
        box.Text = FriendsLinkRules.UrlFor(Token);
        await Until(() => Tag<Button>(drawer, "friends-add-go")!.IsEnabled);
        Click(drawer, "friends-add-go");
        await Until(() => box.Text == "");
        Assert.Equal(new[] { "claim", "state" }, wire.Ops);
        Assert.Equal(Loc.Get("friends_add_accepted"), Tag<TextBlock>(drawer, "friends-add-result")!.Text);
        wire.Claim = "full";
        box.Text = Token;
        Assert.Equal(AddResult.Full, await drawer.AddByCodeAsync());
        Assert.Equal(Token, box.Text);
        Assert.True(Tag<Button>(drawer, "friends-add-go")!.IsEnabled);
        Assert.Equal(Loc.Get("friends_link_full"), Tag<TextBlock>(drawer, "friends-add-result")!.Text);
    });

    [Fact]
    public Task OnlineRowsWearStableOrderedFluentPlatformMarksOnly() => Run(async () =>
    {
        var (svc, wire) = Service();
        wire.StateBody = State.Replace("\"friends\":[]", """
            "friends":[{"id":"a","name":"Ann","online":true,"platforms":["web","phone","desktop","phone","unknown"]},
            {"id":"b","name":"Bea","online":false,"platforms":["phone"]}]
            """);
        int changed = 0;
        svc.SnapshotChanged += _ => changed++;
        await svc.RefreshAsync();
        var drawer = new FriendsDrawer(svc);
        var marks = Tag<StackPanel>(drawer, "friends-platforms")!;
        Assert.NotNull(marks);
        Assert.Equal(new[] { "desktop", "phone", "web" }, marks.Children.Select(c => ((string)c.Tag!).Replace("friends-platform:", "")));
        Assert.All(marks.Children, c => Assert.IsType<global::ConditioningControlPanel.Avalonia.Controls.IconGlyph>(c));
        Assert.All(marks.Children, c => Assert.NotNull(ToolTip.GetTip(c)));
        Assert.Single(drawer.GetLogicalDescendants().OfType<StackPanel>(), c => c.Tag as string == "friends-platforms");
        await svc.RefreshAsync();
        Assert.Equal(1, changed);
    });

    [Fact]
    public Task LinkControlsFitTheNarrowDrawer() => Run(async () =>
    {
        var (svc, _) = Service();
        await svc.RefreshAsync();
        var drawer = new FriendsDrawer(svc) { VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top };
        drawer.ToggleAddBox(true);
        var host = new Window { Width = 300, Height = 548, Content = drawer };
        try
        {
            host.Show();
            Dispatcher.UIThread.RunJobs();
            using var frame = host.CaptureRenderedFrame();
            Assert.NotNull(frame);
            foreach (var tag in new[] { "friends-copy-link", "friends-new-link" })
            {
                var button = Tag<Button>(drawer, tag)!;
                Assert.True(button.Focusable);
                Assert.True(button.Bounds.Width > 0);
                var point = button.TranslatePoint(new Point(button.Bounds.Width, 0), drawer);
                Assert.NotNull(point);
                Assert.InRange(point!.Value.X, 0, drawer.Bounds.Width);
            }
            // Optional evidence output; the test has no dependency on the path.
            var png = Environment.GetEnvironmentVariable("CCP_FRIEND_LINK_RENDER");
            if (!string.IsNullOrEmpty(png)) frame!.Save(png);
        }
        finally { host.Close(); }
    });

}
