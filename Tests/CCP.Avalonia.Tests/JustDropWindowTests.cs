using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
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
    private static void Run(Action<MainShellWindow> body)
    {
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
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
                body(shell);
            }
            finally
            {
                GameWindow.CloseAllForPanic();
                shell.Close();
                JustDropService.SetServerEnabledForTests(false);
                (CreditedOrders.FilePathOverride, CoreProgression.AddXPProvider, CoreAccount.IsLoggedInProvider, CoreSecrets.RetrieveProvider) = (oldLedger, oldXp, oldIn, oldSecret);
                CoreSettings.ServiceProvider = null;
                try { File.Delete(ledger); } catch { }
            }
        });
    }

    private static GameWindow? Open() => GameWindow.OpenForTests(GameWindow.JustDropId);

    [Fact]
    public void ShowTab_OpensTheShopOnlyWhenTheDoorIsOpen_AndPanicClosesIt() => Run(shell =>
    {
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
        Assert.Equal(tab, shell.CurrentTab);              // a window, not a tab
        Assert.Equal("https://app.cclabs.app/dashboard/express", w!.PageUrl!.ToString());   // signed out: no handoff
        Assert.True(w.Web.InjectCarrier);                 // the remote page gets the shared chrome.webview carrier
        Assert.False(w.Web.AllowNavigation!(new Uri("https://evil.example/")));
        Assert.True(w.Web.AllowNavigation!(new Uri("https://app.cclabs.app/express/play?order=A")));

        shell.ShowTab("justdrop");                        // a live window is refocused, never a second shop
        Dispatcher.UIThread.RunJobs();
        Assert.Single(GameWindow.OpenWindowsForTests(), x => x.Spec.Id == GameWindow.JustDropId);

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

    /// <summary>WPF JustDropHostService.cs:105-108,249-253 keeps the site bridge after auth redirects;
    /// ChaosWebViewHost.cs:238,1704 applies its document fence only to NavigatePage transfers.</summary>
    [Theory]
    [InlineData(JustDropHandoff.ShopPath)]
    [InlineData(JustDropHandoff.PlayerPath)]
    public void SignedInHandoffRedirect_StillDeliversSiteMessages(string destination) => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        CoreAccount.UnifiedUserId = "handoff-test-user";
        CoreSecrets.RetrieveProvider = name => name == CoreSecrets.AuthToken ? "test-token" : null;
        (settings.Current.PlayerLevel, settings.Current.JustDropXpDayKey, settings.Current.JustDropCreditedToday) = (1, "", 0);
        var root = Directory.CreateTempSubdirectory("jd-redirect-").FullName;
        CreditedOrders.FilePathOverride = Path.Combine(root, "credited.json");
        using var server = new WebAssetServer(root);
        var w = new GameWindow(GameWindow.Games[GameWindow.JustDropId] with { Query = destination });
        try
        {
            w.Load(server);
            Assert.Equal(JustDropHandoff.HandoffPath, w.PageUrl!.AbsolutePath);
            Assert.Contains(Uri.EscapeDataString(destination), w.PageUrl.Query);
            double paid = 0;
            CoreProgression.AddXPProvider = (xp, _) => paid += xp;
            var complete = "{\"source\":\"justdrop\",\"v\":1,\"type\":\"session-complete\",\"payload\":{\"orderCode\":\"JD-"
                + Guid.NewGuid().ToString("N") + "\",\"sizeId\":\"S\"}}";
            w.Web.OnWebMessage(complete);   // no completed navigation yet
            Assert.Equal(0, paid);
            foreach (var foreign in new[]
            {
                "https://evil.example" + destination,
                "https://app.cclabs.app.evil.example" + destination,
                "http://app.cclabs.app" + destination,
                "https://app.cclabs.app:444" + destination,
            })
            {
                var url = new Uri(foreign);
                Assert.False(w.Web.AllowNavigation!(url));
                w.Web.OnNavigationCompleted(url);
                w.Web.OnWebMessage(complete);
                Assert.Equal(0, paid);
            }
            var redirected = new Uri(JustDropHandoff.SiteOrigin + destination);
            Assert.True(w.Web.AllowNavigation!(redirected));
            w.Web.OnNavigationCompleted(redirected);
            w.Web.OnWebMessage(complete);
            Assert.Equal(40, paid);   // Core's untimed first-play settlement proves completion reached it
            Assert.Equal(JustDropHandoff.HandoffPath, w.PageUrl.AbsolutePath);   // the requested URL stays the handoff
        }
        finally { w.Close(); Directory.Delete(root, true); }
    });

    [Fact]
    public void ThePremiumShelfCardReachesTheDoor() => Assert.True(ExclusivesTabView.IsOnThisBuild("justdrop"));
}
