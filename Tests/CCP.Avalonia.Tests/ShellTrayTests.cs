using System;
using ConditioningControlPanel.Services.Notifications;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The shell tray: WPF's menu order, Stop everything, X-to-tray, Show and the real Exit.</summary>
public sealed class ShellTrayTests
{
    [Fact]
    public async Task ASandboxedShellLeavesItsIconUnlisted()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            TrayIcon.SetIcons(Application.Current!, null!);
            MainShellWindow.TraySandboxed = () => true;
            var realQuery = MainShellWindow.TrayItemQuery;
            var shell = new MainShellWindow();
            try
            {
                shell.CreateTray();
                Assert.DoesNotContain(shell.Tray!, TrayIcon.GetIcons(Application.Current!) ?? new TrayIcons());
                // An icon never published has nothing to hide behind, whatever the bus would say.
                MainShellWindow.TrayItemQuery = () => global::ConditioningControlPanel.Services.Notifications.TrayItemState.Held;
                Assert.Equal(global::ConditioningControlPanel.Services.Notifications.TrayItemState.NotHeld, shell.ProbeTrayItem());
            }
            finally
            {
                MainShellWindow.TraySandboxed = () => global::ConditioningControlPanel.Services.SandboxNet.Active;
                MainShellWindow.TrayItemQuery = realQuery;
                shell.Tray?.Dispose();
                shell.Close();
            }
            return Task.CompletedTask;
        });
    }

    private static Task WithListedTray(Func<MainShellWindow, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var realQuery = MainShellWindow.TrayItemQuery;
        var accepted = CoreSettings.Current.HasAcceptedAgeVerification;
        CoreSettings.Current.HasAcceptedAgeVerification = true;   // an open gate keeps the window up
        MainShellWindow.TraySandboxed = () => false;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.CreateTray();
            await body(shell);
        }
        finally
        {
            MainShellWindow.TrayItemQuery = realQuery;
            CoreSettings.Current.HasAcceptedAgeVerification = accepted;
            MainShellWindow.TraySandboxed = () => global::ConditioningControlPanel.Services.SandboxNet.Active;
            shell.TrayHostPresent = () => false;
            shell.Close();
            shell.Tray?.Dispose();
            TrayIcon.SetIcons(Application.Current!, null!);
        }
    });

    private static TrayItemState[] States(params string[] names) => names.Select(Enum.Parse<TrayItemState>).ToArray();

    [Theory]
    [InlineData("Held", false)]
    [InlineData("Unknown", false)]
    [InlineData("NotHeld", true)]
    public Task XExitsOnlyIfTheIconIsStillUnlistedWithTheUiThreadFree(string second, bool exits) => WithListedTray(async shell =>
    {
        // Registration finishes on the UI thread, so its first answer can be a false no.
        var answers = new Queue<TrayItemState>(States("NotHeld", second));
        MainShellWindow.TrayItemQuery = () => answers.Dequeue();
        var closed = false;
        shell.Closed += (_, _) => closed = true;
        shell.Close();
        if (OperatingSystem.IsWindows())   // no watcher to ask: the listing is the answer, so X hides at once
        {
            Assert.False(shell.IsVisible);
            Assert.False(closed);
            return;
        }
        Assert.True(shell.IsVisible);
        Assert.False(closed);
        for (var i = 0; i < 300 && shell.IsVisible; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
        Assert.False(shell.IsVisible);
        Assert.Equal(exits, closed);
        if (!exits) return;
        MainShellWindow.TrayItemQuery = () => throw new InvalidOperationException("a closed shell has no icon to ask about");
        Assert.Equal(TrayItemState.NotHeld, shell.ProbeTrayItem());
    });

    [Fact]
    public Task StartMinimizedWaitsOutASlowRegistration() => WithListedTray(async shell =>
    {
        var answers = new Queue<TrayItemState>(States("NotHeld", "Held"));
        MainShellWindow.TrayItemQuery = () => answers.Dequeue();
        await shell.HideToTrayOnceHeld();
        Assert.False(shell.IsVisible);
    });

    [Fact]
    public Task AnExplicitShowOutranksALateStartMinimized() => WithListedTray(async shell =>
    {
        MainShellWindow.TrayItemQuery = () => TrayItemState.Held;
        shell.ShowFromTray();
        await shell.HideToTrayOnceHeld();
        Assert.True(shell.IsVisible);
    });

    [Fact]
    public Task StartMinimizedOutwaitsADialogThatCloses() => WithListedTray(async shell =>
    {
        MainShellWindow.TrayItemQuery = () => TrayItemState.Held;
        var splash = new Window();
        splash.Show(shell);
        var hiding = shell.HideToTrayOnceHeld();
        await Task.Delay(200);
        Assert.True(shell.IsVisible);   // a hidden age gate or name picker is a skipped question
        splash.Close();
        await hiding;
        Assert.False(shell.IsVisible);
    });

    [Fact]
    public Task StartMinimizedNeverHidesBeforeTheAgeGateIsAnswered() => WithListedTray(async shell =>
    {
        // On Windows the hide runs before the posted gate exists, and the gate reads a hidden shell as a no.
        MainShellWindow.TrayItemQuery = () => TrayItemState.Held;
        CoreSettings.Current.HasAcceptedAgeVerification = false;
        await shell.HideToTrayOnceHeld();
        Assert.True(shell.IsVisible);
    });

    [Theory]
    [InlineData("show", false)]
    [InlineData("launcher", false)]
    [InlineData("key", false)]
    [InlineData("lockdown", false)]
    [InlineData("second X", true)]   // its press is a key or a click too, but it is no change of mind
    public Task WhatComesAfterAnXDuringTheRecheckDecidesIt(string next, bool closes) => WithListedTray(async shell =>
    {
        if (OperatingSystem.IsWindows()) return;   // no recheck there
        var release = new System.Threading.ManualResetEventSlim();
        var calls = 0;
        MainShellWindow.TrayItemQuery = () =>
        {
            if (Interlocked.Increment(ref calls) == 2) release.Wait(TimeSpan.FromSeconds(5));
            return TrayItemState.NotHeld;
        };
        var closed = false;
        shell.Closed += (_, _) => closed = true;
        var escapes = 0;
        var oldLock = LockdownService.Current;
        var lockdown = new LockdownService();
        lockdown.EscapeAttempted += _ => escapes++;
        shell.Close();
        try
        {
            if (next == "show") shell.ShowFromTray();
            if (next == "launcher") shell.HideForLauncher(quiet: true);
            if (next is "key" or "second X") shell.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F4 });
            if (next == "second X") shell.Close();
            if (next == "lockdown") { LockdownService.Current = lockdown; lockdown.Activate(TimeSpan.FromMinutes(30)); }
            release.Set();
            for (var i = 0; i < 60 && !closed; i++) { Dispatcher.UIThread.RunJobs(); await Task.Delay(10); }
            Assert.Equal(closes, closed);
            Assert.Equal(0, escapes);
        }
        finally { lockdown.Dispose(); LockdownService.Current = oldLock; }
    });

    [Fact]
    public Task StartMinimizedStaysUpWithoutAConfirmedIcon() => WithListedTray(async shell =>
    {
        MainShellWindow.TrayItemQuery = () => TrayItemState.Unknown;
        await shell.HideToTrayOnceHeld();
        Assert.Equal(!OperatingSystem.IsWindows(), shell.IsVisible);   // Windows answers from the listing
    });

    [Fact]
    public async Task TrayMenuStopsOverlaysHidesOnCloseAndExits()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var shell = new MainShellWindow();
            shell.Show();
            MainShellWindow.TraySandboxed = () => false;   // whatever a previous test did to the sandbox flag
            try { shell.CreateTray(); }                    // also proves the avares app.ico resource loads
            finally { MainShellWindow.TraySandboxed = () => global::ConditioningControlPanel.Services.SandboxNet.Active; }
            // Unlisted on the Application, Avalonia never publishes the icon: no bar shows it, and
            // the tray is the panic control a desktop without a global hotkey relies on.
            Assert.Contains(shell.Tray!, TrayIcon.GetIcons(Application.Current!)!);
            shell.TrayHostPresent = () => true;
            var items = shell.Tray!.Menu!.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).ToList();
            Assert.Equal(
                new[] { "tray_show", "launcher_back_to_client", CoreSettings.Current.IsBambiMode ? "tray_wake_bambi" : "tray_wake", "leash_cut", "tray_stop_everything", "tray_exit" }
                    .Select(Loc.Get),
                items.Select(i => i.Header));
            Assert.IsType<NativeMenuItemSeparator>(shell.Tray.Menu.Items[4]);
            Assert.False(items[1].IsVisible);   // Back to CC Labs: a panel boot with no launcher built
            Assert.False(items[3].IsVisible);   // Cut leash: only while leashed

            // Stop everything is StopEngine: schedules stop, queued flashes are dropped, the saved
            // flags stay On (the card stays ticked), and the shell stays open. A plain Start never
            // raises the session feature lock.
            try
            {
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                CoreSession.IsEngineRunningProvider = () => CoreEngine.IsRunning;   // as App seeds it
                CoreSettings.Current.FlashEnabled = CoreSettings.Current.SubliminalEnabled = CoreSettings.Current.BouncingTextEnabled = true;
                shell.StartEngine();
                Assert.True(CoreEngine.IsRunning && CoreFlash.IsRunning && CoreSubliminal.IsRunning);
                Assert.False(shell.IsSessionFeatureLockActive);
                Assert.Equal(Loc.Get("label_stop"), shell.Named<TextBlock>("TxtStartLabel")!.Text);
                var card = new BouncingTextFeatureControl();   // a shown card repaints from the flag
                var cardWindow = new Window { Content = card };
                cardWindow.Show();
                var cardEnable = card.GetVisualDescendants().OfType<CheckBox>().First(c => c.Name == "ChkEnable");
                Assert.True(cardEnable.IsChecked);
                Assert.True(cardEnable.IsEnabled);
                var generation = FlashOverlay.Generation;
                items[4].Command!.Execute(null);
                Assert.False(CoreEngine.IsRunning);
                Assert.False(CoreFlash.IsRunning);
                Assert.False(CoreSubliminal.IsRunning);
                Assert.Equal(generation + 2, FlashOverlay.Generation);   // engine stop and the "flash" panic surface each cancel once
                Assert.True(CoreSettings.Current.FlashEnabled && CoreSettings.Current.SubliminalEnabled && CoreSettings.Current.BouncingTextEnabled);
                Assert.Equal(Loc.Get("label_start"), shell.Named<TextBlock>("TxtStartLabel")!.Text);
                Assert.True(shell.IsVisible);
                Dispatcher.UIThread.RunJobs();
                Assert.True(cardEnable.IsChecked);
                cardWindow.Close();
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                CoreSession.IsEngineRunningProvider = null;
                CoreEngine.Stop();
            }

            // X goes to the tray; Show brings it back; Exit really closes.
            var closed = false;
            shell.Closed += (_, _) => closed = true;
            shell.Close();
            Assert.False(closed);
            Assert.False(shell.IsVisible);
            items[0].Command!.Execute(null);
            Assert.True(shell.IsVisible);
            items[5].Command!.Execute(null);
            Assert.True(closed);
            shell.Tray.Dispose();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void FirstTrayMinimizeRequestsOnlyOneBalloon() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previous = global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink;
        var count = 0;
        global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = (_, _) => count++;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.CreateTray();
            shell.TrayHostPresent = () => true;
            shell.Close();
            Assert.False(shell.IsVisible);
            Assert.Equal(1, count);
            var field = typeof(global::ConditioningControlPanel.Avalonia.Platform.OsNotifications)
                .GetField("_windows", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var cleared = 0;
            using var balloon = new global::ConditioningControlPanel.Avalonia.Platform.WindowsNotifications(
                (IntPtr)42, 7, (_, _) => true, () => cleared++);
            Assert.True(balloon.Show("Title", "Body"));
            field.SetValue(null, balloon);
            shell.ShowFromTray();
            Assert.Equal(1, cleared);
            Assert.Null(field.GetValue(null));
            shell.Close();
            Assert.False(shell.IsVisible);
            Assert.Equal(1, count);
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.Sink = previous;
            global::ConditioningControlPanel.Avalonia.Platform.OsNotifications.DismissWindowsBalloon();
            shell.MarkExitRequestedForTest();
            shell.Close();
            shell.Tray?.Dispose();
        }
    });

    [Fact]
    public async Task CloseWithoutATrayHostReallyCloses()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var shell = new MainShellWindow();
            shell.Show();
            shell.CreateTray();
            // WPF FriendsFeedTray: the tooltip adds the unread friends-feed count, and drops it once read.
            var account = "u_me";
            var feed = new global::ConditioningControlPanel.Services.Friends.FriendsFeed(() => account);
            shell.AttachFeedTooltip(feed);
            Assert.Equal("Conditioning Control Panel", shell.Tray!.ToolTipText);
            feed.Add(new global::ConditioningControlPanel.Services.Friends.FriendEvent(
                global::ConditioningControlPanel.Services.Friends.FriendEventKind.RequestReceived, "u_a", "Ann", System.DateTime.UtcNow, "k1"));
            Assert.Contains(Loc.GetF("friends_feed_tray", 1), shell.Tray.ToolTipText);
            // Signed out: the feed switches lazily and raises nothing; the identity change refreshes the tooltip.
            var before = CoreAccount.UnifiedUserId;
            try
            {
                account = null!;
                CoreAccount.UnifiedUserId = "tray-test-" + Guid.NewGuid();
                Assert.Equal("Conditioning Control Panel", shell.Tray.ToolTipText);
            }
            finally { CoreAccount.UnifiedUserId = before; account = "u_me"; }
            feed.MarkAllRead();
            Assert.Equal("Conditioning Control Panel", shell.Tray.ToolTipText);
            shell.TrayHostPresent = () => false;   // Avalonia's tray fell back silently: nothing to hide behind
            var closed = false;
            shell.Closed += (_, _) => closed = true;
            shell.Close();
            Assert.True(closed);
            shell.ShowFromTray();                  // WPF _windowClosed: a late Show is a no-op, not a throw
            shell.Tray!.Dispose();
            return Task.CompletedTask;
        });
    }
}
