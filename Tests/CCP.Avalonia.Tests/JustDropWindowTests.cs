using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
using ConditioningControlPanel.Services.EmiDesk;
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
        AvaloniaTestDispatcher.Run(() => WithRestoredEmiUsage(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
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
        }));
    }

    private static readonly FieldInfo EmiSaveTimer = typeof(EmiState).GetField("_saveTimer", BindingFlags.Static | BindingFlags.NonPublic)!;
    private static readonly FieldInfo EmiSaveContext = typeof(EmiState).GetField("_saveCtx", BindingFlags.Static | BindingFlags.NonPublic)!;

    // A shop open mutates the shared singleton and arms a timer whose callback can already be
    // queued on the UI thread. Stop and join the timer, then drain queued saves after restoring
    // the entries, so neither memory nor a delayed write carries the test's usage forward.
    private static void WithRestoredEmiUsage(Action action, SynchronizationContext? saveContext = null)
    {
        const string id = GameWindow.JustDropId;
        var state = EmiState.Current;
        bool hadUsage = state.Usage.TryGetValue(id, out int usage);
        bool hadScore = state.OpenScore.TryGetValue(id, out double score);
        bool hadAt = state.UsageAt.TryGetValue(id, out long at);
        var oldContext = EmiSaveContext.GetValue(null);
        // Every save this test schedules posts back to the dispatcher we can drain.
        EmiSaveContext.SetValue(null, saveContext ?? SynchronizationContext.Current);
        try { action(); }
        finally
        {
            try
            {
                if (EmiSaveTimer.GetValue(null) is Timer timer)
                {
                    using var stopped = new ManualResetEvent(false);
                    if (timer.Dispose(stopped)) stopped.WaitOne();
                    EmiSaveTimer.SetValue(null, null);
                }
                if (hadUsage) state.Usage[id] = usage; else state.Usage.Remove(id);
                if (hadScore) state.OpenScore[id] = score; else state.OpenScore.Remove(id);
                if (hadAt) state.UsageAt[id] = at; else state.UsageAt.Remove(id);
                Dispatcher.UIThread.RunJobs(); // includes saves posted before the timer was joined
                EmiState.SaveNow(); // replace any write that landed while the test was running
            }
            finally { EmiSaveContext.SetValue(null, oldContext); }
        }
    }

    private static GameWindow? Open() => GameWindow.OpenForTests(GameWindow.JustDropId);

    [Fact]
    public void ShopAndReplayStayIndependentWhenThePanelIsMinimizedOrHidden() => Run(shell =>
    {
        JustDropService.SetServerEnabledForTests(true);
        shell.ShowTab("justdrop");
        var shop = Open()!;
        Assert.Null(shop.Owner); // A plain native owner would hide the shop with the panel on Windows.
        Assert.True(shop.ShowInTaskbar);
        shell.WindowState = global::Avalonia.Controls.WindowState.Minimized;
        Dispatcher.UIThread.RunJobs();
        Assert.True(shop.IsVisible);
        Assert.NotEqual(global::Avalonia.Controls.WindowState.Minimized, shop.WindowState);
        shell.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.True(shop.IsVisible);
        shell.Show();
        shell.WindowState = global::Avalonia.Controls.WindowState.Normal;
        shop.Close();
        var replay = GameWindow.LaunchJustDropReplay("kept-order", shell)!;
        Assert.Null(replay.Owner); // Replay ducks the panel itself, so it must not inherit its minimize.
        Assert.True(replay.ShowInTaskbar);
        Assert.Equal(global::Avalonia.Controls.WindowState.Minimized, shell.WindowState);
        Assert.Equal(global::Avalonia.Controls.WindowState.FullScreen, replay.WindowState);
        Assert.True(replay.IsVisible);
        shell.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.True(replay.IsVisible);
    });

    [Fact]
    public void ShopCountsEveryOpenOnceAndUsesItsOwnChromiumProfile() => Run(shell =>
    {
        var state = ConditioningControlPanel.Services.EmiDesk.EmiState.Current;
        state.OpenScore.Remove("justdrop");
        state.UsageAt.Remove("justdrop");
        JustDropService.SetServerEnabledForTests(false);
        shell.ShowTab("justdrop");
        Assert.False(state.OpenScore.ContainsKey("justdrop"));
        JustDropService.SetServerEnabledForTests(true);
        shell.ShowTab("justdrop");
        var w = Open()!;
        double first = state.OpenScore["justdrop"];
        Assert.Equal(1, first);
        Assert.Equal(GameWindow.JustDropBrowserArguments, w.Web.WindowsBrowserArguments);
        Assert.Equal(Path.Combine(CorePaths.UserData, "browser_data_avalonia_justdrop"), w.Web.WindowsUserDataFolder);
        shell.ShowTab("justdrop");
        Assert.Same(w, Open());
        Assert.InRange(state.OpenScore["justdrop"], first + 0.99, first + 1);
    });

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
        Assert.Equal(GameWindow.JustDropEscapeScript, w.Web.WindowsDocumentStartScript);
        Assert.Contains("keydown", w.Web.WindowsDocumentStartScript);
        Assert.Contains("ccp-host-escape", w.Web.WindowsDocumentStartScript);
        Assert.False(w.Web.AllowNavigation!(new Uri("https://evil.example/")));
        Assert.True(w.Web.AllowNavigation!(new Uri("https://app.cclabs.app/express/play?order=A")));

        shell.ShowTab("justdrop");                        // a live window is refocused, never a second shop
        Dispatcher.UIThread.RunJobs();
        Assert.Single(GameWindow.OpenWindowsForTests(), x => x.Spec.Id == GameWindow.JustDropId);

        PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(Open());
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShopTestRestoresEmiUsageAndStopsThePendingSave(bool hadEntries) => AvaloniaTestDispatcher.Run(() =>
        WithRestoredEmiUsage(() =>
        {
            var state = EmiState.Current;
            const string id = GameWindow.JustDropId;
            state.Usage.Remove(id); state.OpenScore.Remove(id); state.UsageAt.Remove(id);
            if (hadEntries) { state.Usage[id] = 12; state.OpenScore[id] = 3.5; state.UsageAt[id] = 1234; }
            Timer? scheduled = null;
            Assert.Throws<InvalidOperationException>(() => Run(shell =>
            {
                JustDropService.SetServerEnabledForTests(true);
                shell.ShowTab("justdrop");
                Assert.NotEqual(hadEntries ? 12 : 0, state.Usage[id]);
                scheduled = Assert.IsType<Timer>(EmiSaveTimer.GetValue(null));
                throw new InvalidOperationException(); // cleanup must run after a failed assertion too
            }));
            Assert.Equal(hadEntries, state.Usage.ContainsKey(id));
            Assert.Equal(hadEntries, state.OpenScore.ContainsKey(id));
            Assert.Equal(hadEntries, state.UsageAt.ContainsKey(id));
            if (hadEntries) { Assert.Equal(12, state.Usage[id]); Assert.Equal(3.5, state.OpenScore[id]); Assert.Equal(1234, state.UsageAt[id]); }
            Assert.Null(EmiSaveTimer.GetValue(null));
            Assert.False(scheduled!.Change(500, Timeout.Infinite));
        }));

    [Fact]
    public void PostedEmiSaveRunsAfterUsageIsRestored() => AvaloniaTestDispatcher.Run(() =>
        WithRestoredEmiUsage(() =>
        {
            var state = EmiState.Current;
            const string id = GameWindow.JustDropId;
            state.Usage[id] = 71;
            bool ran = false;
            int observed = 0;
            using var posted = new ManualResetEvent(false);
            var context = new SaveProbeContext(posted, () =>
            {
                ran = true;
                state.Usage.TryGetValue(id, out observed);
            });
            WithRestoredEmiUsage(() =>
            {
                EmiState.NoteUsage(id);
                Assert.True(posted.WaitOne(TimeSpan.FromSeconds(5))); // callback is queued, UI is still blocked here
                Assert.False(ran);
            }, context);
            Assert.True(ran);
            Assert.Equal(71, observed);
        }));

    [Fact]
    public void EmiSaveThatAlreadyRanIsRewrittenAfterUsageRestoration() => AvaloniaTestDispatcher.Run(() =>
        WithRestoredEmiUsage(() =>
        {
            var state = EmiState.Current;
            const string id = GameWindow.JustDropId;
            state.Usage[id] = 71;
            WithRestoredEmiUsage(() =>
            {
                EmiState.NoteUsage(id);
                EmiState.SaveNow(); // the timer may have fired before cleanup
            });
            var saved = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(EmiState.FilePath));
            Assert.Equal(71, (int?)saved["usage"]?[id]);
        }));

    private sealed class SaveProbeContext(ManualResetEvent posted, Action beforeSave) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Dispatcher.UIThread.Post(() => { beforeSave(); callback(state); });
            posted.Set();
        }
    }

    [Fact]
    public void EscapeDuringTheFirstAllowedNavigationRestoresTheWindow() => Run(shell =>
    {
        JustDropService.SetServerEnabledForTests(true);
        shell.ShowTab("justdrop");
        var w = Open()!;
        const string escape = "{\"type\":\"ccp-host-escape\"}";
        w.WindowState = global::Avalonia.Controls.WindowState.FullScreen;
        w.Web.OnWebMessage(escape); // no accepted navigation
        Assert.Equal(global::Avalonia.Controls.WindowState.FullScreen, w.WindowState);
        w.Web.RecordAllowedNavigation(new Uri("https://app.cclabs.app.evil.example/"));
        w.Web.OnWebMessage(escape);
        Assert.Equal(global::Avalonia.Controls.WindowState.FullScreen, w.WindowState);
        w.Web.RecordAllowedNavigation(new Uri(JustDropHandoff.SiteOrigin + JustDropHandoff.ShopPath));
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        Assert.Equal(global::Avalonia.Controls.WindowState.FullScreen, w.WindowState);
        Assert.Null(w.Web.CurrentUrl); // still loading, not a simulated completion
        w.Web.OnWebMessage(escape);
        Assert.Equal(global::Avalonia.Controls.WindowState.Normal, w.WindowState);
        w.WindowState = global::Avalonia.Controls.WindowState.FullScreen;
        w.Web.OnNavigationCompleted(null); // a completion without a URL clears the in-flight authority
        w.Web.OnWebMessage(escape);
        Assert.Equal(global::Avalonia.Controls.WindowState.FullScreen, w.WindowState);
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
            w.Web.RecordAllowedNavigation(w.PageUrl);
            w.Web.OnWebMessage(complete);   // even an accepted in-flight page cannot send business frames yet
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
    public void TheStripPillOpensTheShopOnlyAfterOpenIt() => Run(shell =>
    {
        JustDropService.SetServerEnabledForTests(true);
        shell.ShowTab("presets");
        var strip = shell.PageStrip!;
        strip.PillFor("justdrop")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Null(Open());                              // the pill only asks
        var yes = global::Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(strip.ConfirmationCard!)
            .OfType<global::Avalonia.Controls.Button>().Single(b => b.Name == "NavOpenWindowYes");
        yes.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(Open());                           // Open it -> ShowTab -> OpenJustDrop -> GameWindow.Launch
        Assert.Equal("presets", shell.CurrentTab);
    });

    [Fact]
    public void ThePremiumShelfCardReachesTheDoor() => Assert.True(ExclusivesTabView.IsOnThisBuild("justdrop"));
}
