using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using ConditioningControlPanel.Localization;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.JustDrop;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TakeawayShelfTests
{
    [Fact]
    public Task ReceiptAndShopHoverUseTheSharedMotionAwareLift() => InShell(async shell =>
    {
        CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
        shell.ShowTab("presets"); await shell.TakeawayLoad;
        var tab = shell.Named<PresetsTabView>("PresetsTab")!;
        var shelf = tab.FindControl<StackPanel>("TakeawayShelf")!;
        foreach (var chip in new[] { shelf.Children[0], shelf.Children[^1] })
        {
            var group = Assert.IsType<global::Avalonia.Media.TransformGroup>(chip.RenderTransform);
            var scale = Assert.Single(group.Children.OfType<global::Avalonia.Media.ScaleTransform>());
            chip.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, chip,
                new Pointer(1, PointerType.Mouse, true), chip, default, 0, default, KeyModifiers.None));
            Assert.Equal(1.02, scale.ScaleX);
            Assert.Null(scale.Transitions);
            chip.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, chip,
                new Pointer(1, PointerType.Mouse, true), chip, default, 0, default, KeyModifiers.None));
            Assert.Equal(1, scale.ScaleX);
        }
    });

    [Fact]
    public Task TabVisitReadsRealDrawerAndPinsThreeWithAllRowsInTheToggleTray() => InShell(async shell =>
    {
        shell.ShowTab("presets"); await shell.TakeawayLoad;
        var tab = shell.Named<PresetsTabView>("PresetsTab")!;
        var shelf = tab.FindControl<StackPanel>("TakeawayShelf")!;
        Assert.Equal(5, shelf.Children.Count); // three receipts, +1 more, shop
        Assert.Equal(4, tab.FindControl<StackPanel>("TakeawayTray")!.Children.Count);
        Assert.Equal(new[] { "code1", "code2", "code3" }, shelf.Children.Take(3).Select(c => ((JustDropOrdersService.Order)c.Tag!).Code));
        var more = Assert.IsType<Border>(shelf.Children[3]);
        more.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.True(tab.FindControl<Border>("TakeawayTrayHost")!.IsVisible);
        Assert.Contains("▴", ((TextBlock)more.Child!).Text);
        Assert.False(GameWindow.IsAnyOpen()); // expanding must not replay
        Dispatcher.UIThread.RunJobs();
        Assert.True(more.Bounds.Right <= shelf.Bounds.Width + 0.1, "The overflow toggle was clipped behind the catalogue/export controls");
        Assert.True(shelf.Children[^1].Bounds.Right <= shelf.Bounds.Width + 0.1, "The shop door was clipped");
        using (var frame = shell.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
            frame.Save(System.IO.Path.Combine(AppContext.BaseDirectory, "TakeawayShelf.png"));
        }
        // WPF: with room to spare the shop sits right after the newest receipt, not against Export.
        shell.PaintTakeawayShelf(new[] { new JustDropOrdersService.Order { Code = "solo", Name = "Solo", SizeId = "S" } });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, shelf.Children.Count);
        Assert.InRange(shelf.Children[1].Bounds.Left - shelf.Children[0].Bounds.Right, 0, 10); // chip margin only
        shell.RefreshTakeawayShelf(); await shell.TakeawayLoad;
        Assert.False(tab.FindControl<Border>("TakeawayTrayHost")!.IsVisible);
        Assert.DoesNotContain(shelf.GetVisualDescendants().OfType<TextBlock>(), t => t.Text?.Contains("https://") == true);
    });

    [Fact]
    public Task CopyUsesPublicTasteLinkAndDoesNotReplayAndShopRechecksTheGate() => InShell(async shell =>
    {
        shell.ShowTab("presets"); await shell.TakeawayLoad;
        var shelf = shell.Named<PresetsTabView>("PresetsTab")!.FindControl<StackPanel>("TakeawayShelf")!;
        var copy = Assert.Single(shelf.Children[0].GetVisualDescendants().OfType<Border>(), b => b.Name == "TakeawayCopy");
        copy.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        await Task.Yield();
        Assert.Equal(JustDropService.TasteUrl("code1"), await shell.Clipboard!.TryGetTextAsync());
        Assert.False(GameWindow.IsAnyOpen());
        var door = (Border)shelf.Children[^1];
        JustDropService.SetServerEnabledForTests(false);
        door.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.False(GameWindow.IsAnyOpen());
    });

    [Fact]
    public Task ReplayUsesBarePlayerAndPanicClosesItRestoringThePanel() => InShell(async shell =>
    {
        shell.ShowTab("presets"); await shell.TakeawayLoad;
        var receipt = (Border)shell.Named<PresetsTabView>("PresetsTab")!.FindControl<StackPanel>("TakeawayShelf")!.Children[0];
        var prior = shell.WindowState;
        receipt.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.True(GameWindow.IsAnyOpen());
        var live = GameWindow.LaunchJustDropReplay("another-order", shell)!;
        Assert.Contains(Uri.EscapeDataString(JustDropHandoff.ReplayPath("code1")), live.PageUrl!.Query);
        Assert.Null(live.Owner);
        Assert.Equal(WindowState.Minimized, shell.WindowState);
        Assert.True(live.IsVisible);
        Assert.True(live.ShowInTaskbar);
        Assert.Equal(WindowState.FullScreen, live.WindowState);
        live.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.Equal(WindowState.Normal, live.WindowState);
        Assert.Equal(WindowState.Minimized, shell.WindowState);
        // The shared game panic surface closes the real replay host and restores what it ducked.
        var surface = Assert.Single(PanicSurfaces.All, s => s.Id == "games");
        surface.Stop(shell);
        Assert.False(GameWindow.IsAnyOpen());
        Assert.Equal(prior, shell.WindowState);
    });

    [Fact]
    public Task NativeOwnershipFollowsVisiblePanelAndUnsubscribesOnClose() => InShell(async shell =>
    {
        shell.ShowTab("presets"); await shell.TakeawayLoad;
        var owners = new System.Collections.Generic.List<Window?>();
        NativeWindowOwner.SetForTests = (_, owner) => { owners.Add(owner); return true; };
        // WPF JustDropHostService.cs:68-75: the shop stays windowed.
        shell.OpenJustDrop();
        var shop = GameWindow.OpenForTests(GameWindow.JustDropId)!;
        Assert.NotEqual(WindowState.FullScreen, shop.WindowState);
        Assert.True(shop.JustDropOwnerLinked);
        Assert.Same(shell, owners[^1]);
        shop.WindowState = WindowState.Maximized;
        shell.WindowState = WindowState.Minimized;
        Dispatcher.UIThread.RunJobs();
        Assert.False(shop.JustDropOwnerLinked);
        Assert.Null(owners[^1]);
        Assert.True(shop.IsVisible);
        Assert.Equal(WindowState.Maximized, shop.WindowState); // owner changes preserve the player state
        shop.WindowState = WindowState.Normal;
        shell.WindowState = WindowState.Normal;
        Assert.True(shop.JustDropOwnerLinked);
        shell.Hide();
        Assert.False(shop.JustDropOwnerLinked);
        shell.Show();
        Assert.True(shop.JustDropOwnerLinked);
        shop.Close();
        int closedCalls = owners.Count;
        shell.WindowState = WindowState.Maximized;
        Assert.Equal(closedCalls, owners.Count);
        var replay = GameWindow.LaunchJustDropReplay("kept", shell)!;
        Assert.False(replay.JustDropOwnerLinked);
        Assert.Null(owners[^1]);
        replay.Close();
    });

    /// <summary>ccp-bugs #1499 (WPF JustDropHostService.cs:176-188): a refused sign-in handoff opens the
    /// signed-out destination once; the site's own pages never trip it.</summary>
    [Fact]
    public Task ARefusedHandoffOpensTheSignedOutDestinationOnce() => InShell(shell =>
    {
        var live = GameWindow.LaunchJustDropReplay("code9", shell)!;   // InShell's finally closes it
        Assert.Equal(JustDropHandoff.HandoffPath, live.PageUrl!.AbsolutePath);
        var before = live.Web.NavigationRequests;
        live.Web.OnNavigationCompleted(new Uri(JustDropHandoff.SiteOrigin + JustDropHandoff.ShopPath));
        Assert.Equal(before, live.Web.NavigationRequests);
        live.Web.OnNavigationCompleted(live.PageUrl);
        Assert.Equal(before + 1, live.Web.NavigationRequests);
        Assert.Equal(new Uri(JustDropHandoff.SiteOrigin + JustDropHandoff.ReplayPath("code9")), live.Web.Source);
        live.Web.OnNavigationCompleted(live.PageUrl);   // once only: no loop if the site refuses again
        Assert.Equal(before + 1, live.Web.NavigationRequests);
        return Task.CompletedTask;
    });

    // WPF JustDropHostService.cs:72-95 and ChaosWebViewHost.cs:620-693.
    [Fact]
    public Task ShopAndReceiptKeepAVisibleFullscreenExitOutsideThePage() => InShell(async shell =>
    {
        shell.ShowTab("presets"); await shell.TakeawayLoad;
        var shelf = shell.Named<PresetsTabView>("PresetsTab")!.FindControl<StackPanel>("TakeawayShelf")!;
        ((Border)shelf.Children[^1]).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        var shop = GameWindow.Launch(GameWindow.JustDropId)!;
        var toggle = Assert.Single(shop.GetVisualDescendants().OfType<Button>(), b => b.Name == "JustDropFullscreen");
        Assert.Equal(WindowState.Normal, shop.WindowState);
        Assert.Contains(shop.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Just Drop");
        Assert.Equal(Loc.Get("tooltip_fill_the_screen_esc_brings_the_window_back"), ToolTip.GetTip(toggle));
        Assert.Equal(Loc.Get("btn_fullscreen"), toggle.Content);
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.FullScreen, shop.WindowState);
        Assert.Equal(Loc.Get("btn_exit_fullscreen"), toggle.Content);
        toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.Equal(WindowState.Normal, shop.WindowState);
        Assert.Equal(Loc.Get("btn_fullscreen"), toggle.Content);
        shop.Close(); await shell.TakeawayLoad;
        ((Border)shelf.Children[0]).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        var replay = GameWindow.LaunchJustDropReplay("code1", shell)!;
        toggle = Assert.Single(replay.GetVisualDescendants().OfType<Button>(), b => b.Name == "JustDropFullscreen");
        Dispatcher.UIThread.RunJobs();
        Assert.True(toggle.IsVisible);
        Assert.True(toggle.Bounds.Height > 0);
        Assert.Equal(Loc.Get("btn_exit_fullscreen"), toggle.Content);
        string? injected = null;
        replay.Web.ScriptInvoker = script => { injected = script; return Task.FromResult<string?>(null); };
        replay.Web.OnNavigationCompleted(replay.PageUrl!);
        Assert.Contains("ccp-host-escape", injected);
        Assert.Contains("},true)", injected); // capture-phase, leaves page handling alone
        replay.Web.OnWebMessage("{\"type\":\"ccp-host-escape\"}");
        Assert.Equal(WindowState.Normal, replay.WindowState);
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.FullScreen, replay.WindowState);
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Normal, replay.WindowState);
        Assert.Equal(Loc.Get("btn_fullscreen"), toggle.Content);
        Assert.Equal(WindowState.Minimized, shell.WindowState); // restored on close, not fullscreen exit
        replay.Close(); await shell.TakeawayLoad;
        Assert.Equal(WindowState.Normal, shell.WindowState);
    });

    // WPF ChaosWebViewHost.cs:746-756,768-776 restores the state captured at each entry.
    [Theory]
    [InlineData("toggle")]
    [InlineData("host-escape")]
    [InlineData("page-escape")]
    public Task FullscreenExitRestoresTheShopStateAtEachEntry(string exit) => InShell(async shell =>
    {
        shell.ShowTab("presets"); await shell.TakeawayLoad;
        shell.ShowTab("justdrop");
        var shop = GameWindow.Launch(GameWindow.JustDropId)!;
        var toggle = Assert.Single(shop.GetVisualDescendants().OfType<Button>(), b => b.Name == "JustDropFullscreen");
        shop.Web.OnNavigationCompleted(shop.PageUrl!);
        foreach (var prior in new[] { WindowState.Maximized, WindowState.Normal, WindowState.Maximized })
        {
            shop.WindowState = prior;
            toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(WindowState.FullScreen, shop.WindowState);
            if (exit == "toggle") toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else if (exit == "host-escape") toggle.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            else shop.Web.OnWebMessage("{\"type\":\"ccp-host-escape\"}");
            Assert.Equal(prior, shop.WindowState);
            Assert.Equal(Loc.Get("btn_fullscreen"), toggle.Content);
        }
    });

    private sealed class Drawer : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Assert.Equal("/v2/justdrop/orders", r.RequestUri!.AbsolutePath);
            var rows = Enumerable.Range(1, 4).Select(i => new { code = "code" + i, name = "Receipt " + i, sizeId = "M", at = 1786492800000L });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(new { orders = rows })) });
        }
    }
    private static Task InShell(Func<MainShellWindow, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var state = EmiState.Current;
        var usage = new Dictionary<string, int>(state.Usage);
        var score = new Dictionary<string, double>(state.OpenScore);
        var usageAt = new Dictionary<string, long>(state.UsageAt);
        var motion = CoreSettings.Current.MotionLevel;
        var getHandle = NativeWindowOwner.HandleForTests;
        var nativeOwner = NativeWindowOwner.SetForTests;
        NativeWindowOwner.HandleForTests = _ => new PlatformHandle(new IntPtr(42), "HWND");
        NativeWindowOwner.SetForTests = (_, _) => true;
        var get = CoreSecrets.RetrieveProvider;
        var id = CoreAccount.UnifiedUserId;
        var http = JustDropOrdersService.Http;
        var available = JustDropService.ServerEnabledForTests;
        using var client = new HttpClient(new Drawer());
        MainShellWindow? shell = null;
        try
        {
            CoreSecrets.RetrieveProvider = name => name == CoreSecrets.AuthToken ? "fake-device-token" : null;
            CoreAccount.UnifiedUserId = "u_takeaway_test";
            JustDropOrdersService.Http = client;
            JustDropOrdersService.Invalidate();
            JustDropService.SetServerEnabledForTests(true);
            shell = new MainShellWindow(); shell.Show(); Dispatcher.UIThread.RunJobs();
            await body(shell);
        }
        finally
        {
            GameWindow.CloseAllForPanic(); shell?.Close();
            CoreSettings.Current.MotionLevel = motion;
            NativeWindowOwner.HandleForTests = getHandle;
            NativeWindowOwner.SetForTests = nativeOwner;
            state.Usage.Clear(); foreach (var entry in usage) state.Usage[entry.Key] = entry.Value;
            state.OpenScore.Clear(); foreach (var entry in score) state.OpenScore[entry.Key] = entry.Value;
            state.UsageAt.Clear(); foreach (var entry in usageAt) state.UsageAt[entry.Key] = entry.Value;
            JustDropOrdersService.Http = http; JustDropOrdersService.Invalidate();
            CoreSecrets.RetrieveProvider = get; CoreAccount.UnifiedUserId = id;
            JustDropService.SetServerEnabledForTests(available);
        }
    });
}
