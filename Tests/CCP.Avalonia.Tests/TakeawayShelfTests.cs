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
using Avalonia.Threading;
using Avalonia.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Controls;
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
    public Task ArrivalKeepsReceiptsUntilTheRealDrawerRefreshLands() => InShell(async shell =>
    {
        shell.ShowTab("presets"); await shell.TakeawayLoad;
        var tab = shell.Named<PresetsTabView>("PresetsTab")!;
        var shelf = tab.FindControl<StackPanel>("TakeawayShelf")!;
        var receipt = shelf.Children[0];
        shell.ShowTab("quests");
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        using var client = new HttpClient(new PendingDrawer(pending.Task));
        JustDropOrdersService.Http = client;
        JustDropOrdersService.Invalidate();
        try
        {
            shell.ShowTab("presets");
            Assert.False(shell.TakeawayLoad.IsCompleted);
            Assert.Same(receipt, shelf.Children[0]);
            Assert.True(shelf.IsVisible);
            Assert.False(tab.FindControl<TextBlock>("TxtTakeawayEmpty")!.IsVisible);
        }
        finally
        {
            pending.TrySetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"orders\":[{\"code\":\"fresh\",\"name\":\"Fresh\"}]}") });
            await shell.TakeawayLoad;
        }
        Assert.Equal("fresh", ((JustDropOrdersService.Order)shelf.Children[0].Tag!).Code);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ReceiptAndShopHoverUseTheExistingChipLift(bool shop) => InShell(async shell =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        shell.ShowTab("presets"); await shell.TakeawayLoad;
        Dispatcher.UIThread.RunJobs();
        var shelf = shell.Named<PresetsTabView>("PresetsTab")!.FindControl<StackPanel>("TakeawayShelf")!;
        foreach (var chip in new[] { shop ? shelf.Children[^1] : shelf.Children[0] })
        {
            var point = chip.TranslatePoint(new Point(chip.Bounds.Width / 2, chip.Bounds.Height / 2), shell)!.Value;
            shell.MouseMove(point); Dispatcher.UIThread.RunJobs();
            var scale = Assert.IsType<TransformGroup>(chip.RenderTransform).Children.OfType<ScaleTransform>().Single();
            Assert.Equal(1.02, scale.ScaleX); Assert.Equal(1.02, scale.ScaleY);
            shell.MouseMove(new Point(1, 1)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, scale.ScaleX); Assert.Equal(1, scale.ScaleY);
        }
    });

    private sealed class PendingDrawer(Task<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => response;
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
