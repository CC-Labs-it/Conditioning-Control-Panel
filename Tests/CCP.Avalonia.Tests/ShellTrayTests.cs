using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Features;
using Xunit;
using ConditioningControlPanel.Avalonia.Platform;
using System.Runtime.ExceptionServices;

namespace CCP.Avalonia.Tests;

/// <summary>The shell tray: WPF's menu order, Stop everything, X-to-tray, Show and the real Exit.</summary>
public sealed class ShellTrayTests
{
    [Fact]
    public async Task DeniedBusNameIsHandledButOtherDispatcherExceptionsAreNot()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            MainShellWindow.InstallDeniedBusNameFilter();
            MainShellWindow.InstallDeniedBusNameFilter(); // repeated tray creation must be harmless
            var errors = new Exception[]
            {
                TrayDenial(),
                new InvalidOperationException("wrapper", new Exception("nested",
                    TrayDenial())),
                new Tmds.DBus.Protocol.DBusErrorReplyException("org.freedesktop.DBus.Error.Failed", "RequestName refused"),
                new InvalidOperationException("org.freedesktop.DBus.Error.AccessDenied"),
                new Tmds.DBus.Protocol.DBusErrorReplyException("org.freedesktop.DBus.Error.AccessDenied", "RequestName refused"),
                new Exception("unrelated wrapper", new Tmds.DBus.Protocol.DBusErrorReplyException("org.freedesktop.DBus.Error.AccessDenied", "ReadSecret refused")),
                TrayDenial("   at Other.Service.CreateTrayIcon()\n   at Tmds.DBus.Protocol.DBusConnection.RequestNameAsync(String name)"),
                TrayDenial("   at Avalonia.FreeDesktop.DBusTrayIconImpl.CreateTrayIcon()\n   at Avalonia.FreeDesktop.DBus.StatusNotifierWatcher.RegisterStatusNotifierItemAsync(String name)"),
                new Exception("wrapper", new Tmds.DBus.Protocol.DBusErrorReplyException("org.freedesktop.DBus.Error.NoReply", "timeout")),
            };
            var handled = new System.Collections.Generic.List<bool>();
            void Observe(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                handled.Add(e.Handled); // observe the production handler's result first
                e.Handled = true;       // keep negative cases from killing the test dispatcher
            }
            Dispatcher.UIThread.UnhandledException += Observe;
            try
            {
                foreach (var error in errors)
                {
                    Dispatcher.UIThread.Post(() => throw error);
                    Dispatcher.UIThread.RunJobs();
                }
                Assert.Equal(new[] { true, true, false, false, false, false, false, false, false }, handled);
            }
            finally { Dispatcher.UIThread.UnhandledException -= Observe; }
            return Task.CompletedTask;
        });
    }

    // The proxy need not include a service name or operation in its error body. Replay the
    // pinned Avalonia 12.1.2 / Tmds 0.94.1 async RequestName stack, not a message-based heuristic.
    private static Exception TrayDenial(string? stack = null)
    {
        var error = new Tmds.DBus.Protocol.DBusErrorReplyException(
            "org.freedesktop.DBus.Error.AccessDenied", "org.freedesktop.DBus.Error.AccessDenied");
        ExceptionDispatchInfo.SetRemoteStackTrace(error, stack ??
            "   at Tmds.DBus.Protocol.DBusConnection.RequestNameAsync(String name, RequestNameOptions options)\n" +
            "   at Avalonia.FreeDesktop.DBusTrayIconImpl.CreateTrayIcon()");
        return error;
    }

    [Fact]
    public async Task DeniedRegistrationRestoresHiddenShellAndWatcherCannotHideItAgain()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var shell = new MainShellWindow();
            shell.TrayHostPresent = () => true; // watcher survives the registration failure
            shell.Show();
            shell.CreateTray();
            var closed = false;
            shell.Closed += (_, _) => closed = true;
            try
            {
                Assert.True(shell.CanHideToTray);
                shell.Close(); // a real X-to-tray before the watcher restarts
                Assert.False(closed);
                Assert.False(shell.IsVisible);
                Dispatcher.UIThread.Post(() => throw new Exception("async wrapper", TrayDenial()));
                Dispatcher.UIThread.RunJobs();
                Assert.True(shell.IsVisible);
                Assert.Equal(WindowState.Normal, shell.WindowState);
                Assert.False(shell.CanHideToTray);
                Assert.False(shell.HideToTrayIfAvailable()); // delayed StartMinimized callback
                Assert.True(shell.IsVisible);
                shell.OnTrayActivated(); // a delivered SNI activation proves the icon recovered
                Assert.True(shell.CanHideToTray);
                Assert.True(shell.HideToTrayIfAvailable());
                // Repeated denials must restore too, even though the warning was already logged.
                Dispatcher.UIThread.Post(() => throw TrayDenial());
                Dispatcher.UIThread.RunJobs();
                Assert.True(shell.IsVisible);
                shell.Close();
                Assert.True(closed); // a present watcher cannot hide behind the lost icon
            }
            finally
            {
                shell.MarkExitRequestedForTest();
                if (!closed) shell.Close();
                shell.Tray?.Dispose();
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task TrayMenuStopsOverlaysHidesOnCloseAndExits()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var shell = new MainShellWindow();
            shell.Show();
            shell.CreateTray();   // also proves the avares app.ico resource loads
            shell.TrayHostPresent = () => true;
            var items = shell.Tray!.Menu!.Items.OfType<NativeMenuItem>().Where(i => i is not NativeMenuItemSeparator).ToList();
            Assert.Equal(
                new[] { "tray_show", "launcher_back_to_client", CoreSettings.Current.IsBambiMode ? "tray_wake_bambi" : "tray_wake", "tray_stop_everything", "tray_exit" }
                    .Select(Loc.Get),
                items.Select(i => i.Header));
            Assert.IsType<NativeMenuItemSeparator>(shell.Tray.Menu.Items[3]);
            Assert.False(items[1].IsVisible);   // Back to CC Labs: a panel boot with no launcher built

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
                items[3].Command!.Execute(null);
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
            items[4].Command!.Execute(null);
            Assert.True(closed);
            shell.Tray.Dispose();
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task CloseWithoutATrayHostReallyCloses()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
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
