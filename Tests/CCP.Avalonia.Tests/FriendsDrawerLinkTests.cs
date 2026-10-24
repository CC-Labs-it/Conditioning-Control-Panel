using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Helpers;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class FriendsDrawerTests
{
    private static Task RunLinkTest(Func<Task> test) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options(systemEmoji: false)).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        await test();
    });

    private const string Token = "ABCDEFGHJKLM";
    private static string LinkReply(string token = Token) =>
        "{\"ok\":true,\"token\":\"" + token + "\",\"expires_at\":\"2099-01-01T00:00:00Z\",\"uses_left\":25}";

    [Fact]
    public Task LinkCopyFetchesEveryPressAndNewLinkRevokesBeforeCopying() => RunLinkTest(async () =>
    {
        var (svc, wire) = Service();
        await svc.RefreshAsync();
        wire.LinkBody = LinkReply();
        var copies = new List<string>();
        var d = new FriendsDrawer(svc) { CopyText = text => { copies.Add(text); return Task.FromResult(true); } };
        var oldPill = Tagged<Button>(d, "friends-copy-link")!;
        Assert.True(oldPill.IsEnabled);
        Assert.False(d.AddBoxOpen);
        Assert.True(await d.CopyLinkAsync(oldPill));
        Assert.NotSame(oldPill, Tagged<Button>(d, "friends-copy-link"));
        Assert.Equal(Loc.Get("friends_link_copied"), d.LinkNoteText);
        Assert.Same(FriendsDrawer.Mint, Tagged<TextBlock>(d, "friends-link-note")!.Foreground);
        wire.LinkBody = LinkReply("23456789ABCD");
        Assert.True(await d.CopyLinkAsync());
        Assert.Equal(new[] { FriendsLinkRules.UrlFor(Token), FriendsLinkRules.UrlFor("23456789ABCD") }, copies);
        Assert.Equal(2, wire.Ops.Count(o => o == "link"));
        d.ToggleAddBox(true);
        Assert.NotNull(Tagged<Button>(d, "friends-new-link"));
        wire.Ops.Clear();
        Assert.True(await d.NewLinkAsync());
        Assert.Equal(new[] { "link_revoke", "link" }, wire.Ops);
        Assert.Equal(Loc.Get("friends_link_new_done"), Tagged<TextBlock>(d, "friends-add-result")!.Text);
    });

    [Fact]
    public Task LinkCopyFallsBackToCodeAndClipboardFailureSaysTryLater() => RunLinkTest(async () =>
    {
        var (svc, _) = Service();
        await svc.RefreshAsync();
        string? copied = null;
        var d = new FriendsDrawer(svc) { CopyText = text => { copied = text; return Task.FromResult(true); } };
        Assert.False(await d.CopyLinkAsync());
        Assert.Equal("CCP-ABCDE", copied);
        Assert.Equal(Loc.Get("friends_link_offline"), d.LinkNoteText);
        Assert.Same(FriendsDrawer.Gold, Tagged<TextBlock>(d, "friends-link-note")!.Foreground);
        d.CopyText = _ => throw new InvalidOperationException();
        Assert.False(await d.CopyLinkAsync());
        Assert.Equal(Loc.Get("friends_add_try_later"), d.LinkNoteText);
        Assert.False(await d.NewLinkAsync());
        Assert.Equal(Loc.Get("friends_add_try_later"), Tagged<TextBlock>(d, "friends-add-result")!.Text);
    });

    [Theory]
    [InlineData("friends", AddResult.Accepted, "friends_add_accepted", true, 1)]
    [InlineData("already", AddResult.Already, "friends_add_already", false, 0)]
    [InlineData("self", AddResult.Self, "friends_link_self", false, 0)]
    [InlineData("full", AddResult.Full, "friends_link_full", false, 0)]
    [InlineData("unusable", AddResult.NotFound, "friends_link_unusable", false, 0)]
    [InlineData("try_later", AddResult.TryLater, "friends_add_try_later", false, 0)]
    public Task PastedLinkClaimsAndOnlyTheServiceRefreshes(string status, AddResult result, string key, bool good, int refreshes)
        => RunLinkTest(async () =>
    {
        var (svc, wire) = Service();
        await svc.RefreshAsync();
        wire.ClaimStatus = status;
        var d = new FriendsDrawer(svc);
        d.ToggleAddBox(true);
        var box = Tagged<TextBox>(d, "friends-code-box")!;
        box.Text = FriendsLinkRules.UrlFor(Token);
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(Token, box.Text);
        Assert.False(Tagged<TextBlock>(d, "friends-code-prefix")!.IsVisible);
        Assert.True(Tagged<Button>(d, "friends-add-go")!.IsEnabled);
        wire.Ops.Clear();
        Assert.Equal(result, await d.AddByCodeAsync());
        Assert.Single(wire.Ops.Where(o => o == "claim"));
        Assert.DoesNotContain("add", wire.Ops);
        Assert.Equal(refreshes, wire.Ops.Count(o => o == "state"));
        Assert.Equal(Loc.Get(key), Tagged<TextBlock>(d, "friends-add-result")!.Text);
        Assert.Equal(good ? "" : Token, box.Text);
        Assert.Equal(!good, Tagged<Button>(d, "friends-add-go")!.IsEnabled);
    });

    [Fact]
    public Task CopyPopsTheReplacementPillOnTheHeadlessWindow() => RunLinkTest(async () =>
    {
        var (svc, wire) = Service();
        await svc.RefreshAsync();
        wire.LinkBody = LinkReply();
        var d = new FriendsDrawer(svc) { CopyText = _ => Task.FromResult(true) };
        var host = new Window { Width = 340, Height = 600, Content = d };
        var previous = global::ConditioningControlPanel.CoreSettings.Current.MotionLevel;
        var beforeTime = BeatLoop.Time;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        try
        {
            global::ConditioningControlPanel.CoreSettings.Current.MotionLevel = global::ConditioningControlPanel.Models.MotionLevel.Full;
            host.Show();
            var source = Tagged<Button>(d, "friends-copy-link")!;
            await d.CopyLinkAsync(source);
            var replacement = Tagged<Button>(d, "friends-copy-link")!;
            Assert.NotSame(source, replacement);
            host.UpdateLayout();
            Assert.Same(host, TopLevel.GetTopLevel(replacement));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(replacement.IsEffectivelyVisible);
            Assert.Contains(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, replacement));
            Assert.Equal(global::ConditioningControlPanel.Models.MotionLevel.Full, global::ConditioningControlPanel.CoreSettings.Current.MotionLevel);
            time.Advance(TimeSpan.FromMilliseconds(110));
            BeatLoop.StepAll();
            Assert.True(replacement.RenderTransform!.Value.M11 > 1, "The visible replacement swells during the pop.");
            Assert.True(source.RenderTransform == null || source.RenderTransform.Value.IsIdentity);
            time.Advance(TimeSpan.FromMilliseconds(270));
            BeatLoop.StepAll();
            Assert.Equal(1, replacement.RenderTransform!.Value.M11, 3);
            Assert.DoesNotContain(BeatLoop.Running, loop => ReferenceEquals(loop.Owner, replacement));
        }
        finally
        {
            host.Close();
            BeatLoop.Time = beforeTime;
            global::ConditioningControlPanel.CoreSettings.Current.MotionLevel = previous;
        }
    });

    [Fact]
    public Task LinkNoteExpiresAfterFourSeconds() => RunLinkTest(async () =>
    {
        var (svc, _) = Service();
        await svc.RefreshAsync();
        var d = new FriendsDrawer(svc) { CopyText = _ => Task.FromResult(true) };
        await d.CopyLinkAsync();
        await Task.Delay(300);
        Assert.NotEmpty(d.LinkNoteText);
        d.Render(); // ordinary redraw must preserve the held note
        Assert.Equal(Loc.Get("friends_link_offline"), d.LinkNoteText);
        await Task.Delay(TimeSpan.FromSeconds(4));
        Assert.Empty(d.LinkNoteText);
        Assert.Null(Tagged<TextBlock>(d, "friends-link-note"));
    });

    [Fact]
    public Task HandTypedCcpTokenSurvivesAndPastedCodeKeepsItsPrefix() => RunLinkTest(() =>
    {
        var d = new FriendsDrawer(Service().Svc);
        var box = Tagged<TextBox>(d, "friends-code-box")!;
        foreach (var c in "CCPABCDEFGHJ")
        {
            box.Text += c;
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
        Assert.Equal("CCPABCDEFGHJ", box.Text);
        Assert.False(Tagged<TextBlock>(d, "friends-code-prefix")!.IsVisible);
        box.Text = "CCP-ABCDE";
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal("ABCDE", box.Text);
        Assert.True(Tagged<TextBlock>(d, "friends-code-prefix")!.IsVisible);
        return Task.CompletedTask;
    });
}
