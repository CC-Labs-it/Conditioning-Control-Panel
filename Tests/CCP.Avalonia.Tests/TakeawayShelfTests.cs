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
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.JustDrop;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class TakeawayShelfTests
{
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
        // The shop opens windowed, its strip offering Fullscreen (WPF HostOwnedFullscreen, Launch(ShopPath)).
        door.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        var shop = GameWindow.OpenForTests(GameWindow.JustDropId)!;
        Assert.NotEqual(WindowState.FullScreen, shop.WindowState);
        Assert.True(shop.Web.LiveSessionMedia);
        Assert.True(shop.JustDropOwner!.IsLinked);
        shell.WindowState = WindowState.Minimized;
        Assert.False(shop.JustDropOwner.IsLinked);
        shell.WindowState = WindowState.Normal;
        Assert.True(shop.JustDropOwner.IsLinked);
        shell.Hide(); Assert.False(shop.JustDropOwner.IsLinked);
        shell.Show(); Assert.True(shop.JustDropOwner.IsLinked);
        var ownership = shop.JustDropOwner;
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("btn_fullscreen"), ((TextBlock)shop.FullscreenToggle!.Content!).Text);
        shop.Close();
        Assert.False(ownership.IsLinked);
        Dispatcher.UIThread.RunJobs();
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
        Assert.Equal(WindowState.FullScreen, live.WindowState);
        // WPF HostOwnedFullscreen: the strip's toggle is on screen in both modes and names the way it goes.
        var toggle = live.FullscreenToggle!;
        Assert.True(toggle.IsVisible);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("btn_exit_fullscreen"), ((TextBlock)toggle.Content!).Text);
        live.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.Equal(WindowState.Normal, live.WindowState);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("btn_fullscreen"), ((TextBlock)toggle.Content!).Text);
        Assert.Equal(WindowState.Minimized, shell.WindowState);
        Assert.True(live.IsVisible);
        Assert.False(live.JustDropOwner!.IsLinked);
        shell.WindowState = WindowState.Normal;
        Assert.True(live.JustDropOwner.IsLinked);
        shell.WindowState = WindowState.Minimized;
        Assert.False(live.JustDropOwner.IsLinked);
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.FullScreen, live.WindowState);
        Assert.True(live.Topmost);
        // Topmost is rented while active only; headless never (de)activates, so raise it the platform's way.
        static void Raise(Window w, string m) => typeof(WindowBase).GetMethod(m,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(w, null);
        Raise(live, "HandleDeactivated");
        Assert.False(live.Topmost);
        Raise(live, "HandleActivated");
        Assert.True(live.Topmost);
        live.WindowState = WindowState.Normal;   // the window manager leaves fullscreen by itself
        Assert.False(live.Topmost);
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        // Esc route 2: a focused page reports the key; the host consumes it. Through the engine's seams,
        // so the bridge must be injected on load and the same-origin guard must let the page through.
        var scripts = new System.Collections.Generic.List<string>();
        live.Web.ScriptSinkForTests = scripts.Add;
        live.Web.OnNavigationCompleted(live.PageUrl);
        Assert.Contains(GameWindow.EscapeBridgeScript, scripts);
        live.Web.OnWebMessage("{\"type\":\"" + GameWindow.HostEscapeMessageType + "\"}");
        Assert.Equal(WindowState.Normal, live.WindowState);
        // The shared game panic surface closes the real replay host and restores what it ducked.
        var surface = Assert.Single(PanicSurfaces.All, s => s.Id == "games");
        surface.Stop(shell);
        Assert.False(GameWindow.IsAnyOpen());
        Assert.Equal(prior, shell.WindowState);
        // WPF RestoreMainWindow: anything but Maximized comes back Normal (never fullscreen again).
        shell.WindowState = WindowState.FullScreen;
        GameWindow.LaunchJustDropReplay("code1", shell)!.Close();
        Assert.Equal(WindowState.Normal, shell.WindowState);
    });

    [Fact]
    public Task ReceiptAffordancesUseAccentFocusAndOnlyPinnedReceiptsAndShopLift() => InShell(async shell =>
    {
        shell.ShowTab("presets"); await shell.TakeawayLoad;
        var tab = shell.Named<PresetsTabView>("PresetsTab")!;
        var shelf = tab.FindControl<StackPanel>("TakeawayShelf")!;
        var pink = (global::Avalonia.Media.IBrush)Application.Current!.FindResource("PinkBrush")!;
        var more = (Border)shelf.Children[3];
        var label = (TextBlock)more.Child!;
        Assert.Equal(pink, label.Foreground);
        Assert.Equal(11.5, label.FontSize);
        Assert.Equal(global::Avalonia.Media.FontWeight.Bold, label.FontWeight);
        var door = (Border)shelf.Children[^1];
        Assert.All(door.GetVisualDescendants().OfType<TextBlock>(), text => Assert.Equal(pink, text.Foreground));
        var priorMotion = CoreSettings.Current.MotionLevel;
        try
        {
            CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Off;
            var receipt = (Border)shelf.Children[0];
            receipt.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, receipt,
                new Pointer(1, PointerType.Mouse, true), receipt, default, 0, default, default));
            Assert.Equal(1.02, receipt.RenderTransform!.Value.M11, 5);
            Assert.Null(receipt.Transitions);
            receipt.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, receipt,
                new Pointer(1, PointerType.Mouse, true), receipt, default, 0, default, default));
            Assert.Equal(1, receipt.RenderTransform!.Value.M11, 5);
            CoreSettings.Current.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
            receipt.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, receipt,
                new Pointer(1, PointerType.Mouse, true), receipt, default, 0, default, default));
            var transition = Assert.IsType<global::Avalonia.Animation.TransformOperationsTransition>(Assert.Single(receipt.Transitions!));
            Assert.Equal(TimeSpan.FromMilliseconds(150), transition.Duration);
            Assert.IsType<global::Avalonia.Animation.Easings.QuadraticEaseOut>(transition.Easing);
            Assert.Null(((Border)tab.FindControl<StackPanel>("TakeawayTray")!.Children[0]).RenderTransform);
            Assert.Null(more.RenderTransform); // WPF has no lift on the toggle or tray rows
            receipt.Focus(NavigationMethod.Tab); Dispatcher.UIThread.RunJobs();
            Assert.Equal(pink, receipt.BorderBrush);
        }
        finally { CoreSettings.Current.MotionLevel = priorMotion; }
    });

    [Fact]
    public void JustDropEnvironmentKeepsTheLiveClockAndAudioRunningWithoutChangingOtherHosts()
    {
        // The package constructs these args internally; the policy uses only auto properties.
        static global::Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs Args() =>
            (global::Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs)
                System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(global::Avalonia.Platform.WindowsWebView2EnvironmentRequestedEventArgs));
        var live = Args();
        ConditioningControlPanel.Avalonia.Views.Controls.WebHost.ConfigureEnvironment(live, true);
        Assert.Equal("--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-backgrounding-occluded-windows", live.AdditionalBrowserArguments);
        var other = Args();
        other.AdditionalBrowserArguments = "existing-arguments";
        ConditioningControlPanel.Avalonia.Views.Controls.WebHost.ConfigureEnvironment(other, false);
        Assert.Equal("existing-arguments", other.AdditionalBrowserArguments);
    }

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
            JustDropOrdersService.Http = http; JustDropOrdersService.Invalidate();
            CoreSecrets.RetrieveProvider = get; CoreAccount.UnifiedUserId = id;
            JustDropService.SetServerEnabledForTests(available);
        }
    });
}
