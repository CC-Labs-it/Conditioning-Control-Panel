// PORTED from ConditioningControlPanel/Services/Notifications/TrayIconService.cs plus the tray
// half of MainWindow.WindowChrome.cs OnClosing (X -> tray) and MainWindow.Launcher.cs RequestExit.
//
// Avalonia's TrayIcon is StatusNotifierItem over D-Bus on Linux and a native icon on Windows.
// Divergences, on purpose (docs/avalonia-decisions.html, tray row):
//   - The icon stays visible while the window is up (WPF shows it only while hidden). On this head
//     the tray is the panic control a takeover needs without a global hotkey, so it must always be
//     reachable.
//   - "Stop everything" is an extra item for that reason: it is StopEngine - overlays down, saved
//     flags untouched, so the next Start brings them back. The app stays up.
//   - With no tray host, or a watcher that refused the item, Avalonia's tray fails silently, so X
//     would hide the window behind nothing. X is gated on the watcher holding this item, else X exits.
// The first-minimize balloon (TrayIconService.cs:165-171) goes through Platform/OsNotifications.

using System;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Services.Notifications;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Leash;
using Tmds.DBus.Protocol;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Set by App on the desktop path; null in renders and tests, where X closes.</summary>
        internal TrayIcon? Tray { get; private set; }

        /// <summary>Is this app's icon in a tray, to hide behind? Re-probed on every X; tests inject it.</summary>
        internal Func<bool> TrayHostPresent
        {
            get => _trayHostSeam ?? ProbeTrayItemRegistered;
            set => _trayHostSeam = value;
        }
        private Func<bool>? _trayHostSeam;

        /// <summary>`--tray-check`: a deliberate live probe, the one sandboxed run that may publish.</summary>
        internal static bool TrayCheck { get; set; }

        /// <summary>A failure until a verdict lands, so a signal or a closed window cannot pass the check.</summary>
        internal static int TrayCheckExit { get; private set; } = 1;

        internal static Func<bool> TraySandboxed { get; set; } = () => global::ConditioningControlPanel.Services.SandboxNet.Active;

        private volatile bool _trayListed;

        private bool _exitRequested;
        // Test seam: headless cannot raise an OS/app shutdown, so tests set the shutdown flag directly.
        internal void MarkExitRequestedForTest() => _exitRequested = true;
        // WPF TrayIconService._windowClosed: Show() on a closed window throws.
        private bool _windowClosed;
        // WPF TrayIconService._hasShownFirstMinimizeNotification.
        private bool _shownFirstMinimizeNotification;

        internal TrayIcon CreateTray()
        {
            var tray = new TrayIcon
            {
                ToolTipText = "Conditioning Control Panel",
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/app.ico"))),
                Menu = BuildTrayMenu(),
                IsVisible = true,
            };
            // Avalonia publishes only a TrayIcon listed on the Application: unlisted, the tray menu
            // objects exist on the bus but no StatusNotifierItem does, so no bar ever shows it.
            // Tray is set last: publishing throws here on a bad platform, and X must not hide to nothing.
            var publish = TrayItemRule.ShouldPublish(TraySandboxed(), TrayCheck);
            if (publish) TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });
            _trayListed = publish;
            Tray = tray;
            // SNI Activate (left click) arrives as Clicked; WPF restores on single and double click.
            Tray.Clicked += (_, _) => ShowFromTray();
            // Avalonia raises Opening only on macOS, so WPF's refresh on open never runs on Linux or Windows.
            if (publish)
            {
                var refresh = new global::Avalonia.Threading.DispatcherTimer(TimeSpan.FromSeconds(2),
                    global::Avalonia.Threading.DispatcherPriority.Background, (_, _) => RefreshTrayMenu());
                refresh.Start();
                Closed += (_, _) => refresh.Stop();
            }
            Closed += (_, _) => _trayListed = false;
            AttachFeedTooltip(Platform.FriendsHead.Feed, Platform.FriendsHead.Service);
            return Tray;
        }

        /// <summary>WPF FriendsFeedTray: the tray tooltip adds how many friends-feed lines are unread.
        /// Refreshed on every feed change, and on an account change (the feed switches accounts lazily and
        /// raises nothing; WPF refreshes on hover, which Avalonia's tray does not have).</summary>
        internal void AttachFeedTooltip(global::ConditioningControlPanel.Services.Friends.FriendsFeed? feed,
            global::ConditioningControlPanel.Services.Friends.IFriendsService? service = null)
        {
            if (Tray == null || feed == null) return;
            var baseText = Tray.ToolTipText ?? "";
            void Refresh()
            {
                if (!global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) { global::Avalonia.Threading.Dispatcher.UIThread.Post(Refresh); return; }
                try
                {
                    var n = feed.Unread;
                    if (Tray != null) Tray.ToolTipText = global::ConditioningControlPanel.Services.Friends.FriendsFeedRules.TrayText(baseText, n, n > 0 ? Loc.GetF("friends_feed_tray", n) : "");
                }
                catch (Exception ex) { Serilog.Log.Debug("[Friends] tray tooltip failed: {E}", ex.Message); }
            }
            void OnIdentity(object? s, EventArgs e) => Refresh();
            void OnSnapshot(global::ConditioningControlPanel.Services.Friends.FriendsSnapshot _) => Refresh();
            feed.Changed += Refresh;
            CoreAccount.UnifiedIdentityChanged += OnIdentity;
            if (service != null) service.SnapshotChanged += OnSnapshot;
            Closed += (_, _) =>
            {
                feed.Changed -= Refresh;
                CoreAccount.UnifiedIdentityChanged -= OnIdentity;
                if (service != null) service.SnapshotChanged -= OnSnapshot;
            };
            Refresh();
        }

        private Action? _refreshTrayMenu;
        /// <summary>Applies the live state to the items that depend on it.</summary>
        internal void RefreshTrayMenu() => _refreshTrayMenu?.Invoke();

        /// <summary>WPF order: Show, Back to CC Labs, Wake, Cut leash, separator, Exit - with Stop everything above Exit.</summary>
        internal NativeMenu BuildTrayMenu()
        {
            var menu = new NativeMenu();
            menu.Add(Item("tray_show", ShowFromTray));
            // WPF TrayIconService.cs:102: the way back to the launcher, only while it is part of this
            // run, greyed under Lockdown (BackToLauncher refuses anyway).
            var back = Item("launcher_back_to_client", () => LauncherWindow.BackToLauncher(this));
            // WPF TrayIconService.cs:119: one click, only while leashed, never greyed under any lock.
            var cut = Item(LeashTrayRule.CutActionId, () => _ = CutLeashAsync());
            void RefreshItems(object? s, EventArgs e)
            {
                back.IsVisible = LauncherWindow.SurfaceInPlay;
                back.IsEnabled = !LockdownActive;
                cut.IsVisible = LeashHoldLeashed;
            }
            _refreshTrayMenu = () => RefreshItems(null, EventArgs.Empty);
            RefreshItems(null, EventArgs.Empty);
            menu.Opening += RefreshItems;
            menu.NeedsUpdate += RefreshItems;
            menu.Add(back);
            // WPF reads the label once at tray creation (TrayIconService.cs Initialize);
            // App.Mods.IsBambiMode there is the active-mod check AppSettings.IsBambiMode makes here.
            menu.Add(Item(CoreSettings.Current.IsBambiMode ? "tray_wake_bambi" : "tray_wake", WakeBambiUp));
            menu.Add(cut);
            menu.Add(new NativeMenuItemSeparator());
            menu.Add(Item("tray_stop_everything", StopEverything));
            menu.Add(Item("tray_exit", RequestExit));
            return menu;
        }

        private static NativeMenuItem Item(string key, Action action) =>
            new(Loc.Get(key)) { Command = new CompanionRelayCommand(action) };

        /// <summary>The panic item: WPF StopEngine (a running session is paused, as the panic key does).
        /// Saved flags stay as the user set them.</summary>
        internal static void StopEverything()
        {
            Serilog.Log.Information("Tray: Stop everything");
            if (RefuseStopUnderLockdown()) return;   // WPF refuses every Stop under Lockdown (StartStop.cs:45)
            PanicSurfaces.StopAll("tray");   // decision C: the only panic control on Windows, camera included
        }

        /// <summary>TrayIconService.ShowWindow + MainWindow's OnShowRequested (ShowAvatarTube).</summary>
        public void ShowFromTray()
        {
            _shownSinceStart = true;   // asked for: a late StartMinimized must not undo it
            _trayRecheckStale = true;
            if (_windowClosed) return;
            Platform.OsNotifications.DismissWindowsBalloon();
            RestoreLauncherPanelOpacity();
            Show();
            // WPF LauncherHost.ShowPanel (:265): the panel on screen releases the boot launcher hold, whichever door showed it.
            Platform.StartupLadder.Release();
            WindowState = WindowState.Normal;
            Activate();
            ShowAvatarTube();
        }

        /// <summary>WPF MainWindow.Launcher.cs:28 and Notifications/TrayIconService.cs:180: keep the engine alive, suppress the balloon only at boot.</summary>
        internal void HideForLauncher(bool quiet = false)
        {
            if (LockdownActive) return;
            Hide();
            HideAvatarTube();
            RestoreLauncherPanelOpacity();
            _trayRecheckStale = true;
            if (!quiet && !_shownFirstMinimizeNotification && TrayIconConfirmed()) NotifyFirstMinimize();
        }

        private void NotifyFirstMinimize()
        {
            if (_shownFirstMinimizeNotification) return;
            _shownFirstMinimizeNotification = true;
            Platform.OsNotifications.Show(Loc.Get("app_title"), Loc.Get("tray_balloon_body"), ShowFromTray);
        }

        /// <summary>MainWindow.Launcher.cs RequestExit: the one real exit; OnDesktopExit saves. Circe's bill
        /// shows only on the user's own exits - the tray's Exit and Settings' Exit button (both land
        /// here); double panic and the 18+ refusals come through <see cref="ExitWithoutBill"/>.</summary>
        public void RequestExit()
        {
            if (LockdownActive) { Serilog.Log.Information("Lockdown: Exit refused"); return; }   // WPF Launcher.cs:184
            _trayRecheckStale = true;
            if (CoreEngine.IsRunning) StopEngine();                  // WPF Launcher.cs:187
            // Circe's bill holds the exit for a few seconds, then calls back in here (Launcher.cs:188).
            if (TryShowExitBill(RequestExit)) return;
            _exitRequested = true;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
            else
                Close();
        }

        /// <summary>WPF OnClosing: X goes to the tray (WindowChrome.cs:337-349); only RequestExit, or
        /// a missing tray host, really closes.</summary>
        protected override void OnClosing(WindowClosingEventArgs e)
        {
            WelcomeShow.FirstShowService.Stop();   // WPF WindowChrome.cs:128, before the Lockdown refusal
            // WPF WindowChrome.cs:131: under Lockdown the user's close is refused (not even hidden),
            // after the Close tripwire. OS / application shutdown still goes through: the recovery
            // file puts the real panic key and Strict Lock back on the next start.
            if (LockdownActive && e.CloseReason == WindowCloseReason.WindowClosing && !_exitRequested)
            {
                try { Services.LockdownService.Current?.NotifyEscapeAttempt(Services.Possession.EscapeKinds.Close); } catch { }
                e.Cancel = true;
                return;
            }
            var byUser = Tray is not null && !_exitRequested && e.CloseReason == WindowCloseReason.WindowClosing;
            // The press behind a repeat X was no change of mind.
            if (byUser && _trayRechecking) { _trayRecheckStale = false; e.Cancel = true; return; }
            var verdict = _trayXVerdict;
            _trayXVerdict = null;
            var mayHide = byUser && (verdict ?? TrayHostPresent());
            if (byUser && !mayHide && verdict is null && TrayAnswerMayChange) { e.Cancel = true; RecheckTrayThenClose(); return; }
            // WPF WindowChrome.cs:140: closing (real exit OR minimize-to-tray) always ends a Chaos run.
            try { Views.Chaos.ChaosRunHost.ForceShutdown(); } catch { }
            if (mayHide)
            {
                e.Cancel = true;
                // WPF WindowChrome.cs:345: quizzes are unowned topmost windows that would stay on screen.
                PopQuizWindow.ForceCloseAll();
                QuizWindow.CloseAll();
                Hide();
                HideAvatarTube();
                // ponytail: WPF fires App.EmiDesk "minimizedToTray" here (WindowChrome.cs:362); this head has no EmiDesk Fire moment bus yet.
                NotifyFirstMinimize();
            }
            // A tray hide is not an exit: preserve the session and public Closing subscriptions.
            if (e.Cancel) return;
            EnsureSessionRestoredForExit();
            // WPF WindowChrome.cs:180: a confirmed library delete still in its undo grace is honoured.
            try { CommitPendingDeeperDeletesOnExit(); } catch { }
            // WPF WindowChrome.cs:176: the Companion drawers' state, when quitting from that tab.
            try { PersistCompanionDrawerStatesOnExit(); } catch { }
            base.OnClosing(e);
        }

        private bool _trayRechecking;
        private bool _trayRecheckStale;   // anything the user did after that X outranks it
        private bool _shownSinceStart;
        private bool? _trayXVerdict;

        private bool TrayAnswerMayChange => _trayHostSeam is null && _trayListed && !OperatingSystem.IsWindows();

        // Unlisted, as seen from the UI thread, can just mean registration is still queued on this thread or
        // a new watcher took over. X ends the session only if that is still so with this thread free.
        private async void RecheckTrayThenClose()
        {
            _trayRechecking = true;
            _trayRecheckStale = false;
            var endWatch = WatchInput(() => _trayRecheckStale = true);
            var state = await Task.Run(ProbeTrayItem);
            await Task.Yield();   // a probe already done would otherwise run Close() inside this OnClosing
            endWatch();
            _trayRechecking = false;
            // Lockdown here is not a refused X but a fresh Close that would read as an escape attempt.
            if (_windowClosed || _exitRequested || _trayRecheckStale || LockdownActive) return;
            _trayXVerdict = TrayItemRule.XMayHide(state);
            Close();
            _trayXVerdict = null;   // Lockdown can refuse that Close before it reads the verdict
        }

        // The window stays usable while the tray is asked: any key or click means the user moved on.
        private Action WatchInput(Action tookIt)
        {
            void Took(object? s, global::Avalonia.Interactivity.RoutedEventArgs e) => tookIt();
            AddHandler(KeyDownEvent, Took, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            AddHandler(PointerPressedEvent, Took, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
            return () => { RemoveHandler(KeyDownEvent, Took); RemoveHandler(PointerPressedEvent, Took); };
        }

        /// <summary>WPF MainWindow.xaml.cs:3594: StartMinimized sends the shown window to the tray, here once
        /// the icon is confirmed, because staying visible is harmless and vanishing is not. The icon registers
        /// after CreateTray returns, on the UI thread, which a busy startup can hold for a while.</summary>
        internal async Task HideToTrayOnceHeld()
        {
            if (!_trayListed) return;
            var endWatch = WatchInput(() => _shownSinceStart = true);
            try
            {
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(6);   // an autostart can beat the bar
                do
                {
                    if (_shownSinceStart || _windowClosed) return;
                    // Windows knows at once, so its window never shows.
                    var state = OperatingSystem.IsWindows() ? ProbeTrayItem() : await Task.Run(ProbeTrayItem);
                    if (state == TrayItemState.Held && !_shownSinceStart)
                    {
                        // The age gate is only posted at this point on Windows, and it reads a hidden shell as a
                        // no. Once up, it or a name picker is an owned dialog, and hiding one skips the question.
                        // A splash is owned too and soon gone, so this waits rather than gives up.
                        var asking = !CoreSettings.Current.HasAcceptedAgeVerification || OwnedWindows.Count > 0;
                        if (!asking) { if (IsVisible) HideForLauncher(quiet: true); return; }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
                while (DateTime.UtcNow < deadline);
                Serilog.Log.Information("StartMinimized: no tray icon confirmed, leaving the window up");
            }
            finally
            {
                endWatch();
            }
        }

        /// <summary>For work that belongs to startup: Opened fires on every Show, so a restore from the tray
        /// would rerun it, which for Takeover means stopping a live run as if its flag were stale.</summary>
        private void OnceOpened(Action startup)
        {
            void Run(object? s, EventArgs e) { Opened -= Run; startup(); }
            Opened += Run;
        }

        /// <summary>For a hide nothing depends on, unlike X: only a confirmed icon counts.</summary>
        internal bool TrayIconConfirmed() => _trayHostSeam?.Invoke() ?? ProbeTrayItem() == TrayItemState.Held;

        /// <summary>The X answer: hide unless the tray said no. See <see cref="TrayItemRule.XMayHide"/>.</summary>
        internal bool ProbeTrayItemRegistered() => TrayItemRule.XMayHide(ProbeTrayItem());

        /// <summary>Is this process's icon really in the tray? A watcher can own its name and still refuse the
        /// item, and Avalonia only logs that, so we ask the watcher rather than trusting that it exists.</summary>
        internal TrayItemState ProbeTrayItem()
        {
            if (!_trayListed) return TrayItemState.NotHeld;
            if (OperatingSystem.IsWindows()) return TrayItemState.Held;
            return TrayItemQuery();
        }

        /// <summary>Test seam: what the watcher says. Tests must not read the desktop's bus.</summary>
        internal static Func<TrayItemState> TrayItemQuery { get; set; } = AskTrayWatcher;

        // Registration is asynchronous, hence the short retry. Avalonia finishes it on the UI thread, so a
        // retry there could never see it and would only freeze the window: that caller gets one attempt.
        private static TrayItemState AskTrayWatcher()
        {
            var onUiThread = global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess();
            var attempts = onUiThread ? 1 : 5;
            var refusals = 0;
            try
            {
                var probe = Task.Run(async () =>
                {
                    var bus = DBusConnection.Session;
                    await bus.ConnectAsync();
                    for (var attempt = 0; attempt < attempts; attempt++)
                    {
                        if (attempt > 0) await Task.Delay(250);
                        var (listed, noWatcher) = await TrayItemListedAsync(bus);
                        // No proper answer, which outdates any earlier no.
                        if (listed is null) { Interlocked.Exchange(ref refusals, 0); continue; }
                        if (listed.Value) return true;
                        Interlocked.Increment(ref refusals);
                        if (noWatcher) return false;    // no tray: nothing to wait for
                    }
                    return false;
                });
                var finished = probe.Wait(TimeSpan.FromSeconds(onUiThread ? 2 : 6));
                if (finished && probe.Result) return TrayItemState.Held;
                if (Volatile.Read(ref refusals) > 0) return TrayItemState.NotHeld;   // slow, but it did say no
                Serilog.Log.Warning("Tray item probe got no proper answer from the watcher");
                return TrayItemState.Unknown;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "No session bus, so no tray; X will exit instead of hiding");
                return TrayItemState.NotHeld;
            }
        }

        // Names, not PIDs: in a Flatpak the bus and the app do not agree on PIDs. Avalonia owns the name on a
        // private connection, so only the name's PID part ties it to us, and two Flatpaks can share one.
        // A watcher can run with no host at all (kded with no tray-capable bar), and then nothing draws the icon.
        private static async Task<(bool? Listed, bool NoWatcher)> TrayItemListedAsync(DBusConnection bus)
        {
            try
            {
                if (!await TrayWatcherOwnedAsync(bus)) return (false, true);
                var items = await WatcherPropertyAsync(bus, "RegisteredStatusNotifierItems", v => v.GetArray<string>());
                var prefix = TrayItemRule.ItemNamePrefix(Environment.ProcessId);
                var mine = (await bus.ListServicesAsync()).Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).ToList();
                foreach (var name in mine.ToArray()) mine.Add(await BusNameOwnerAsync(bus, name));
                return (TrayItemRule.Holds(items, mine)
                    && await WatcherPropertyAsync(bus, "IsStatusNotifierHostRegistered", v => v.GetBool()), false);
            }
            catch (Exception ex)   // the watcher went away mid-probe, or errored: that is not a refusal
            {
                Serilog.Log.Debug(ex, "Tray item probe: no proper answer");
                return (null, false);
            }
        }

        private static async Task<bool> TrayWatcherOwnedAsync(DBusConnection bus)
        {
            var w = bus.GetMessageWriter();
            w.WriteMethodCallHeader(destination: "org.freedesktop.DBus", path: "/org/freedesktop/DBus",
                @interface: "org.freedesktop.DBus", member: "NameHasOwner", signature: "s");
            w.WriteString("org.kde.StatusNotifierWatcher");
            return await bus.CallMethodAsync(w.CreateMessage(), (Message m, object? _) => m.GetBodyReader().ReadBool(), null);
        }

        private static async Task<T> WatcherPropertyAsync<T>(DBusConnection bus, string name, Func<VariantValue, T> read)
        {
            var w = bus.GetMessageWriter();
            w.WriteMethodCallHeader(destination: "org.kde.StatusNotifierWatcher", path: "/StatusNotifierWatcher",
                @interface: "org.freedesktop.DBus.Properties", member: "Get", signature: "ss");
            w.WriteString("org.kde.StatusNotifierWatcher");
            w.WriteString(name);
            return await bus.CallMethodAsync(w.CreateMessage(), (Message m, object? _) => read(m.GetBodyReader().ReadVariantValue()), null);
        }

        private static async Task<string> BusNameOwnerAsync(DBusConnection bus, string name)
        {
            var w = bus.GetMessageWriter();
            w.WriteMethodCallHeader(destination: "org.freedesktop.DBus", path: "/org/freedesktop/DBus",
                @interface: "org.freedesktop.DBus", member: "GetNameOwner", signature: "s");
            w.WriteString(name);
            return await bus.CallMethodAsync(w.CreateMessage(), (Message m, object? _) => m.GetBodyReader().ReadString(), null);
        }

        /// <summary>`--tray-check`: exit 0 only if the watcher confirmed this process's item. Unlike X, no
        /// answer is a failure: a proof that cannot fail is not a proof.</summary>
        internal void RunTrayCheck() => Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(2));
            var state = ProbeTrayItem();
            Console.WriteLine($"tray item registered: {state}");
            TrayCheckExit = state == TrayItemState.Held ? 0 : 1;
            // Through the lifetime: Environment.Exit from here segfaults in native teardown (exit 139).
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown(TrayCheckExit));
        });

        /// <summary>Linux has a tray host only while a StatusNotifierWatcher owns its name.</summary>
        internal static bool ProbeTrayHost()
        {
            if (OperatingSystem.IsWindows()) return true;
            try
            {
                var probe = Task.Run(async () =>
                {
                    var bus = DBusConnection.Session;
                    await bus.ConnectAsync();
                    return await TrayWatcherOwnedAsync(bus);
                });
                return probe.Wait(TimeSpan.FromSeconds(1)) && probe.Result;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Tray host probe failed; X will exit instead of hiding");
                return false;
            }
        }
    }
}
