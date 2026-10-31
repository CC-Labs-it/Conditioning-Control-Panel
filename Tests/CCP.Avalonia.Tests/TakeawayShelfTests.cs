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

    [Fact]
    public Task ReceiptAndShopLiftAndResetWhileTrayAndToggleStayStill() => InShell(async shell =>
    {
        shell.ShowTab("presets"); await shell.TakeawayLoad;
        var tab = shell.Named<PresetsTabView>("PresetsTab")!;
        var shelf = tab.FindControl<StackPanel>("TakeawayShelf")!;
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        foreach (var chip in new[] { (Border)shelf.Children[0], (Border)shelf.Children[^1] })
        {
            void PointerEvent(global::Avalonia.Interactivity.RoutedEvent<PointerEventArgs> ev) =>
                chip.RaiseEvent(new PointerEventArgs(ev, chip, new Pointer(1, PointerType.Mouse, true),
                    shell, default, 0, default, KeyModifiers.None));
            PointerEvent(InputElement.PointerEnteredEvent);
            var scale = Assert.IsType<ScaleTransform>(chip.RenderTransform);
            Assert.Equal(1.02, scale.ScaleX, 3);
            Assert.Equal(scale.ScaleX, scale.ScaleY);
            Assert.Null(scale.Transitions);
            PointerEvent(InputElement.PointerExitedEvent);
            Assert.Equal(1, scale.ScaleX);
            PointerEvent(InputElement.PointerEnteredEvent);
            shelf.Children.Remove(chip);
            Assert.Equal(1, scale.ScaleX);
        }
        Assert.Null(shelf.Children.OfType<Border>().Single(b => b.Name == "TakeawayMore").RenderTransform);
        Assert.Null(tab.FindControl<StackPanel>("TakeawayTray")!.Children[0].RenderTransform);
    });

    [Fact]
    public void EmiJustDropDoorTracksServerAvailabilityWithoutAPremiumLock()
    {
        var available = JustDropService.ServerEnabledForTests;
        try
        {
            var door = EmiTargets.Find("justdrop")!;
            Assert.NotNull(door);
            JustDropService.SetServerEnabledForTests(false);
            Assert.False(door.Available);
            JustDropService.SetServerEnabledForTests(true);
            Assert.True(door.Available);
            Assert.False(door.Locked);
        }
        finally { JustDropService.SetServerEnabledForTests(available); }
    }

    [Fact]
    public void ChromiumOptionsKeepTheExactWpfFlagsInADedicatedProfile()
    {
        var args = (WindowsWebView2EnvironmentRequestedEventArgs)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WindowsWebView2EnvironmentRequestedEventArgs));
        GameWindow.ConfigureJustDropEnvironment(args);
        Assert.Equal("--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-backgrounding-occluded-windows",
            args.AdditionalBrowserArguments);
        Assert.Equal(System.IO.Path.Combine(CorePaths.UserData, "browser_data_avalonia_justdrop"), args.UserDataFolder);
    }

    [Fact]
    public Task ShopOptionsAndEmiDoorAreScopedAndRefocusCountsOnce() => InShell(async shell =>
    {
        var door = EmiTargets.Find("justdrop")!;
        Assert.True(door.Available);
        Assert.False(door.Locked);
        var before = EmiState.Current.Usage.GetValueOrDefault("justdrop");
        var shop = shell.OpenJustDrop()!;
        var after = EmiState.Current.Usage["justdrop"];
        Assert.True(after > before);
        Assert.Same(shop, shell.OpenJustDrop());
        Assert.Equal(after - before, EmiState.Current.Usage["justdrop"] - after);
        JustDropService.SetServerEnabledForTests(false);
        Assert.False(door.Available);
        Assert.Null(shell.OpenJustDrop());
        Assert.Equal(after + after - before, EmiState.Current.Usage["justdrop"]);

        var args = (WindowsWebView2EnvironmentRequestedEventArgs)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WindowsWebView2EnvironmentRequestedEventArgs));
        // Invoke the actual per-host subscriber, just as NativeWebView's environment event does.
        var field = typeof(ConditioningControlPanel.Avalonia.Views.Controls.WebHost).GetField("EnvironmentRequested",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        ((Action<WebViewEnvironmentRequestedEventArgs>)field.GetValue(shop.Web)!)(args);
        Assert.Equal("--autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-backgrounding-occluded-windows",
            args.AdditionalBrowserArguments);
        Assert.Equal(System.IO.Path.Combine(CorePaths.UserData, "browser_data_avalonia_justdrop"), args.UserDataFolder);
        var ordinary = new GameWindow();
        try { Assert.Null(field.GetValue(ordinary.Web)); }
        finally { ordinary.Close(); }
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
