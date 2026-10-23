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
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Platform;
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
        var owners = new System.Collections.Generic.List<Window?>();
        NativeWindowOwner.SetForTests = (_, owner) => { owners.Add(owner); return true; };
        // The shop opens windowed, its strip offering Fullscreen (WPF HostOwnedFullscreen, Launch(ShopPath)).
        door.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        var shop = GameWindow.OpenForTests(GameWindow.JustDropId)!;
        Assert.NotEqual(WindowState.FullScreen, shop.WindowState);
        Assert.True(shop.JustDropOwnerLinked);
        Assert.Same(shell, owners[^1]);
        shell.WindowState = WindowState.Minimized;
        Dispatcher.UIThread.RunJobs();
        Assert.False(shop.JustDropOwnerLinked);
        Assert.Null(owners[^1]);
        Assert.True(shop.IsVisible);
        Assert.NotEqual(WindowState.Minimized, shop.WindowState);
        shell.WindowState = WindowState.Normal;
        Assert.True(shop.JustDropOwnerLinked);
        shell.Hide();
        Assert.False(shop.JustDropOwnerLinked);
        shell.Show();
        Assert.True(shop.JustDropOwnerLinked);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("btn_fullscreen"), ((TextBlock)shop.FullscreenToggle!.Content!).Text);
        shop.Close();
        int closedCalls = owners.Count;
        shell.WindowState = WindowState.Maximized;
        Assert.Equal(closedCalls, owners.Count); // closed windows must release the owner's event handlers
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
        Assert.False(live.JustDropOwnerLinked); // deliberately ducked owner must never hide its player
        // WPF HostOwnedFullscreen: the strip's toggle is on screen in both modes and names the way it goes.
        var toggle = live.FullscreenToggle!;
        Assert.True(toggle.IsVisible);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("btn_exit_fullscreen"), ((TextBlock)toggle.Content!).Text);
        live.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.Equal(WindowState.Normal, live.WindowState);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("btn_fullscreen"), ((TextBlock)toggle.Content!).Text);
        Assert.Equal(WindowState.Minimized, shell.WindowState);
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
        Assert.False(live.JustDropOwnerLinked);
        // WPF RestoreMainWindow: anything but Maximized comes back Normal (never fullscreen again).
        shell.WindowState = WindowState.FullScreen;
        GameWindow.LaunchJustDropReplay("code1", shell)!.Close();
        Assert.Equal(WindowState.Normal, shell.WindowState);
    });

    [Fact]
    public Task ReceiptAndShopLiftButTrayAndToggleStayStillAndLabelsUsePink() => InShell(async shell =>
    {
        shell.ShowTab("presets"); await shell.TakeawayLoad;
        var tab = shell.Named<PresetsTabView>("PresetsTab")!;
        var shelf = tab.FindControl<StackPanel>("TakeawayShelf")!;
        var receipt = (Border)shelf.Children[0];
        var door = (Border)shelf.Children[^1];
        var more = (Border)shelf.Children[^2];
        var row = (Border)tab.FindControl<StackPanel>("TakeawayTray")!.Children[0];
        var motion = CoreSettings.Current.MotionLevel;
        try
        {
            CoreSettings.Current.MotionLevel = MotionLevel.Off;
            foreach (var chip in new[] { receipt, door })
            {
                chip.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent, chip,
                    new Pointer(1, PointerType.Mouse, true), shell, default, 0, default, KeyModifiers.None));
                Assert.Equal(1.02, Assert.IsType<ScaleTransform>(chip.RenderTransform).ScaleX, 3);
                chip.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent, chip,
                    new Pointer(1, PointerType.Mouse, true), shell, default, 0, default, KeyModifiers.None));
                Assert.Equal(1, ((ScaleTransform)chip.RenderTransform!).ScaleX, 3);
            }
            Assert.Null(row.RenderTransform);
            Assert.Null(more.RenderTransform);
            var pink = (IBrush)Application.Current!.Resources["PinkBrush"]!;
            Assert.Equal(pink, ((TextBlock)more.Child!).Foreground);
            Assert.Equal(pink, ((StackPanel)door.Child!).Children.OfType<TextBlock>().Single().Foreground);
            Assert.Equal(pink, ((StackPanel)door.Child!).Children.OfType<IconGlyph>().Single().Foreground);
            Assert.Equal(FluentIcons.Common.Symbol.Options, ((Grid)row.Child!).Children.OfType<IconGlyph>().First().Kind);
            // Keyboard copy is a nested affordance: focus must reveal its stroke and full opacity.
            var copy = receipt.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "TakeawayCopy");
            Assert.True(copy.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, copy.Opacity);
            Assert.Equal(Application.Current.Resources["TransparentPink40Brush"], copy.BorderBrush);
        }
        finally { CoreSettings.Current.MotionLevel = motion; }
    });

    [Fact]
    public Task JustDropFlagsReachWebView2AndOtherGamesKeepTheirDefaults() => InShell(async shell =>
    {
        shell.ShowTab("presets"); await shell.TakeawayLoad;
        var pending = new GameWindow(GameWindow.Games[GameWindow.JustDropId]);
        Assert.Equal(GameWindow.JustDropBrowserArguments, pending.Web.AdditionalBrowserArguments);
        pending.Close(); // options exist before Show/Load can request the native environment
        var shop = shell.OpenJustDrop()!;
        var windows = EnvironmentArgs<WindowsWebView2EnvironmentRequestedEventArgs>();
        shop.Web.ConfigureEnvironment(windows);
        Assert.Equal("--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-backgrounding-occluded-windows",
            windows.AdditionalBrowserArguments);
        var ordinary = new GameWindow();
        try
        {
            var defaults = EnvironmentArgs<WindowsWebView2EnvironmentRequestedEventArgs>();
            ordinary.Web.ConfigureEnvironment(defaults);
            Assert.True(string.IsNullOrEmpty(defaults.AdditionalBrowserArguments));
            // The Linux adapter has no matching knobs; it must still configure without throwing.
            shop.Web.ConfigureEnvironment(EnvironmentArgs<GtkWebViewEnvironmentRequestedEventArgs>());
        }
        finally { ordinary.Close(); }
    });

    // NativeWebView owns these internal constructors (DeferralManager). These tests only
    // populate/read managed options, never ask for a deferral or initialize an engine.
    private static T EnvironmentArgs<T>() where T : WebViewEnvironmentRequestedEventArgs =>
        (T)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));

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
        var nativeOwner = NativeWindowOwner.SetForTests;
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
            NativeWindowOwner.SetForTests = nativeOwner;
            JustDropOrdersService.Http = http; JustDropOrdersService.Invalidate();
            CoreSecrets.RetrieveProvider = get; CoreAccount.UnifiedUserId = id;
            JustDropService.SetServerEnabledForTests(available);
        }
    });
}
