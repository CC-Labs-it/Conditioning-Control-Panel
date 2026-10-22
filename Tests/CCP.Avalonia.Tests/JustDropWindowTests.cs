using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.JustDrop;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Just Drop's door on this head (WPF MainWindow.TabNavigation.cs:264-272 -> JustDropHostService.LaunchShop):
/// ShowTab("justdrop") is a no-op while the server flag is shut, opens the shop's GameWindow when it is open,
/// the token rides the handoff request once, page frames reach Core JustDropService, and a panic closes it.
/// No network: the window never gets an engine headless and the door flag is set directly.</summary>
public sealed class JustDropWindowTests
{
    private static void Run(Action<MainShellWindow> body) =>
        RunAsync(shell => { body(shell); return Task.CompletedTask; }).GetAwaiter().GetResult();

    private static Task RunAsync(Func<MainShellWindow, Task> body)
    {
        return AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var usage = EmiState.Current.Usage.ToDictionary(p => p.Key, p => p.Value);
            var score = EmiState.Current.OpenScore.ToDictionary(p => p.Key, p => p.Value);
            var usageAt = EmiState.Current.UsageAt.ToDictionary(p => p.Key, p => p.Value);
            var service = new SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            (s.Welcomed, s.HasAcceptedAgeVerification, s.WhatMovedCardShown) = (true, true, 1);   // P52/P76
            var ledger = Path.Combine(Path.GetTempPath(), "jd-ledger-" + Guid.NewGuid().ToString("N") + ".json");
            var (oldLedger, oldXp, oldIn, oldSecret) = (CreditedOrders.FilePathOverride, CoreProgression.AddXPProvider, CoreAccount.IsLoggedInProvider, CoreSecrets.RetrieveProvider);
            CreditedOrders.FilePathOverride = ledger;
            CoreAccount.IsLoggedInProvider = () => false;
            var shell = new MainShellWindow();
            try
            {
                shell.Show();
                Dispatcher.UIThread.RunJobs();
                await body(shell);
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                shell.Close();
                JustDropService.SetServerEnabledForTests(false);
                (CreditedOrders.FilePathOverride, CoreProgression.AddXPProvider, CoreAccount.IsLoggedInProvider, CoreSecrets.RetrieveProvider) = (oldLedger, oldXp, oldIn, oldSecret);
                CoreSettings.ServiceProvider = null;
                EmiState.Current.Usage.Clear(); foreach (var p in usage) EmiState.Current.Usage[p.Key] = p.Value;
                EmiState.Current.OpenScore.Clear(); foreach (var p in score) EmiState.Current.OpenScore[p.Key] = p.Value;
                EmiState.Current.UsageAt.Clear(); foreach (var p in usageAt) EmiState.Current.UsageAt[p.Key] = p.Value;
                try { File.Delete(ledger); } catch { }
            }
        });
    }

    private static GameWindow? Open() => GameWindow.OpenForTests(GameWindow.JustDropId);

    private sealed class OrdersHandler : System.Net.Http.HttpMessageHandler
    {
        public int Calls;
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new System.Net.Http.StringContent("{\"orders\":[{\"code\":\"JD-LIVE\",\"sizeId\":\"S\",\"name\":\"From the drawer\"}]}") });
        }
    }

    /// <summary>P04: the user path, not PaintTakeaway - showing the Session door fetches the drawer and draws it.</summary>
    [Fact]
    public Task ShowingThePresetsTabLoadsTheDrawerAndDrawsItsReceipts() => RunAsync(async shell =>
    {
        var previous = (CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, JustDropOrdersService.HttpOverride);
        var handler = new OrdersHandler();
        using var client = new System.Net.Http.HttpClient(handler);
        try
        {
            JustDropService.SetServerEnabledForTests(false);
            CoreAccount.UnifiedUserId = "u-tab";
            CoreSecrets.RetrieveProvider = key => key == CoreSecrets.AuthToken ? "t" : null;
            JustDropOrdersService.HttpOverride = client;
            JustDropOrdersService.Invalidate();
            shell.ShowTab("studio");
            shell.ShowTab("presets");
            var shelf = shell.FindControl<PresetsTabView>("PresetsTab")!.FindControl<StackPanel>("TakeawayShelf")!;
            var until = DateTime.UtcNow.AddSeconds(5);   // the fetch hops the thread pool; pump until it paints
            while (shelf.Children.Count == 0 && DateTime.UtcNow < until) { Dispatcher.UIThread.RunJobs(); await Task.Yield(); }
            Assert.True(handler.Calls >= 1);
            var receipt = Assert.IsType<Border>(Assert.Single(shelf.Children));
            Assert.Equal("JD-LIVE", Assert.IsType<JustDropOrdersService.Order>(receipt.Tag).Code);
            Assert.True(shelf.IsVisible);
        }
        finally
        {
            (CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, JustDropOrdersService.HttpOverride) = previous;
            JustDropOrdersService.Invalidate();
        }
    });

    [Fact]
    public void ShowTab_OpensTheShopOnlyWhenTheDoorIsOpen_AndPanicClosesIt() => Run(shell =>
    {
        int opened = (EmiState.Current.Usage.TryGetValue(GameWindow.JustDropId, out var n) ? n : 0);
        var tab = shell.CurrentTab;
        JustDropService.SetServerEnabledForTests(false);
        shell.ShowTab("justdrop");
        Dispatcher.UIThread.RunJobs();
        Assert.Null(Open());                              // a door nobody can see is a no-op
        Assert.Equal(tab, shell.CurrentTab);              // and never a redirect

        JustDropService.SetServerEnabledForTests(true);
        shell.ShowTab("justdrop");
        Dispatcher.UIThread.RunJobs();
        var w = Open();
        Assert.NotNull(w);
        Assert.Equal(opened + 1, EmiState.Current.Usage[GameWindow.JustDropId]);
        Assert.Equal(tab, shell.CurrentTab);              // a window, not a tab
        Assert.Equal("https://app.cclabs.app/dashboard/express", w!.PageUrl!.ToString());   // signed out: no handoff
        Assert.True(w.Web.InjectCarrier);                 // the remote page gets the shared chrome.webview carrier
        Assert.False(w.Web.AllowNavigation!(new Uri("https://evil.example/")));
        Assert.True(w.Web.AllowNavigation!(new Uri("https://app.cclabs.app/express/play?order=A")));

        shell.ShowTab("justdrop");                        // a live window is refocused, never a second shop
        Dispatcher.UIThread.RunJobs();
        Assert.Single(GameWindow.OpenWindowsForTests(), x => x.Spec.Id == GameWindow.JustDropId);
        Assert.Equal(opened + 2, EmiState.Current.Usage[GameWindow.JustDropId]);

        PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(Open());
    });

    [Fact]
    public void TheTokenRidesTheHandoffOnce_AndPageFramesReachCore() => Run(shell =>
    {
        JustDropService.SetServerEnabledForTests(true);
        shell.ShowTab("justdrop");
        Dispatcher.UIThread.RunJobs();
        var w = Open()!;
        var handoff = new Uri("https://app.cclabs.app/api/auth/desktop-session?unified_id=u1&next=%2Fdashboard%2Fexpress");
        var s = CoreSettings.Current;
        CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "tok" : null;   // AuthToken lives in CoreSecrets
        (s.PlayerLevel, s.JustDropXpDayKey, s.JustDropCreditedToday) = (1, null, 0);   // P02: the 40 below needs both
        (string, string)? sent = null;
        bool Take(string n, string v) { sent = (n, v); return true; }
        Assert.False(w.Web.Stamp(new Uri("https://app.cclabs.app/dashboard/express"), Take));
        Assert.False(w.Web.Stamp(handoff, (_, _) => false));   // the engine refused it: not spent
        Assert.True(w.Web.Stamp(handoff, Take));               // the window's own rule, as wired
        Assert.Equal(("X-CCP-Auth-Token", "tok"), sent);
        Assert.False(w.Web.Stamp(handoff, Take));              // spent: a reload or a frame never gets it again

        double paid = 0;
        int posted = 0;
        CoreProgression.AddXPProvider = (xp, _) => paid += xp;
        w.Posted += _ => posted++;
        w.HandleMessage("{\"source\":\"justdrop\",\"v\":1,\"type\":\"ready\",\"payload\":{}}");
        w.HandleMessage("{\"source\":\"justdrop\",\"v\":1,\"type\":\"session-complete\",\"payload\":{\"orderCode\":\"JD-" + Guid.NewGuid().ToString("N") + "\",\"sizeId\":\"S\"}}");
        Assert.Equal(40, paid);                           // untimed first play: a taste (Core settlement)
        Assert.Equal(0, posted);                          // the site never gets the local games' init
        Assert.True(w.IsVisible);                         // and no game frame closes it
    });

    [Fact]
    public void ThePremiumShelfCardReachesTheDoor() => Assert.True(ExclusivesTabView.IsOnThisBuild("justdrop"));

    [Fact]
    public void TakeawayReplayOwnsFullscreenAndRestoresOnlyItsOwnMinimize() => Run(shell =>
    {
        JustDropService.SetServerEnabledForTests(false); // kept receipts outlive the shop flag
        shell.ShowTab("presets");
        var tab = shell.FindControl<PresetsTabView>("PresetsTab")!;
        tab.PaintTakeaway(new[] { new JustDropOrdersService.Order { Code = "A B", Name = "My drop", SizeId = "S" } });
        var shelf = tab.FindControl<StackPanel>("TakeawayShelf")!;
        var receipt = Assert.IsType<Border>(Assert.Single(shelf.Children));
        receipt.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        var window = Open()!;
        Assert.NotNull(window);
        Assert.Equal("https://app.cclabs.app/express/play?order=A B", window.PageUrl!.ToString());
        Assert.Same(shell, window.Owner);
        Assert.Equal(WindowState.FullScreen, window.WindowState);
        Assert.Equal(WindowState.Minimized, shell.WindowState);
        var frame = Assert.IsType<DockPanel>(window.Content);
        var strip = Assert.IsType<Border>(frame.Children[0]);
        var toggle = Assert.IsType<Button>(Assert.IsType<DockPanel>(strip.Child).Children[0]);
        toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(WindowState.Normal, window.WindowState);
        window.SetJustDropFullscreen(true);
        window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.Equal(WindowState.Normal, window.WindowState);
        PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
        Assert.Equal(WindowState.Normal, shell.WindowState);
        Assert.Null(Open());

        shell.WindowState = WindowState.Minimized;
        window = GameWindow.LaunchJustDropReplay("A", shell)!;
        window.Close();
        Assert.Equal(WindowState.Minimized, shell.WindowState); // the user's minimize is theirs
    });

    [Fact]
    public void TakeawayOverflowContainsAllOrdersAndShopLast_AndRingTracksServerFlag() => Run(shell =>
    {
        var target = EmiTargets.Find("justdrop")!;
        JustDropService.SetServerEnabledForTests(false);
        Assert.False(target.Available);
        Assert.False(target.Locked);
        JustDropService.SetServerEnabledForTests(true);
        Assert.True(target.Available);
        shell.ShowTab("presets");
        var tab = shell.FindControl<PresetsTabView>("PresetsTab")!;
        var orders = Enumerable.Range(1, 5).Select(i => new JustDropOrdersService.Order { Code = "JD-" + i }).ToArray();
        tab.PaintTakeaway(orders);
        var shelf = tab.FindControl<StackPanel>("TakeawayShelf")!;
        Assert.Equal(5, shelf.Children.Count); // three receipts, overflow, shop
        Assert.Equal("JD-1", Assert.IsType<JustDropOrdersService.Order>(((Border)shelf.Children[0]).Tag).Code);
        Assert.Equal("TakeawayShop", shelf.Children[4].Name);
        var tray = tab.FindControl<StackPanel>("TakeawayTray")!;
        Assert.Equal(5, tray.Children.Count);
        shelf.Children[3].RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.True(tab.FindControl<Border>("TakeawayTrayHost")!.IsVisible);
        tab.PaintTakeaway(orders);
        Assert.False(tab.FindControl<Border>("TakeawayTrayHost")!.IsVisible);
        JustDropService.SetServerEnabledForTests(false);
        tab.PaintTakeaway(Array.Empty<JustDropOrdersService.Order>());
        Assert.False(shelf.IsVisible);
        Assert.True(tab.FindControl<TextBlock>("TxtTakeawayEmpty")!.IsVisible);
    });

    [Fact]
    public Task CopyingAReceiptCopiesOnlyTheTasteLinkAndDoesNotReplay() => RunAsync(async shell =>
    {
        shell.ShowTab("presets");
        var tab = shell.FindControl<PresetsTabView>("PresetsTab")!;
        tab.PaintTakeaway(new[] { new JustDropOrdersService.Order { Code = "JD-COPY" } });
        var receipt = Assert.IsType<Border>(tab.FindControl<StackPanel>("TakeawayShelf")!.Children[0]);
        var line = Assert.IsType<StackPanel>(receipt.Child);
        var copy = Assert.IsType<Border>(line.Children.Last());
        copy.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.Null(Open());
        Assert.Equal(JustDropService.TasteUrl("JD-COPY"), await shell.Clipboard!.TryGetTextAsync());
    });
}
