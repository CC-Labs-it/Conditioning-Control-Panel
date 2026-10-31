using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>panic-key-linux-hardening. WPF's WH_KEYBOARD_LL hook hears ANY bound key and reads the setting per
/// press (MainWindow.xaml.cs:896-902 capture, :943-960 live setting; GlobalKeyboardHook.cs:165), so on Linux: a bound key X cannot hear is warned
/// about (once per key); and a rebind while the GlobalShortcuts portal is bound re-binds it.</summary>
public sealed class PanicKeyLinuxHardeningTests
{
    [Fact]
    public void AKeyXCannotResolve_IsRaisedToTheUser_NeverOnlyLogged()
    {
        var old = X11PanicKey.Unhearable;
        var heard = new List<string?>();
        try
        {
            X11PanicKey.Unhearable = heard.Add;
            Assert.Equal(9, X11PanicKey.ResolveKeycode("Escape", _ => 9));
            Assert.Empty(heard);
            Assert.Equal(0, X11PanicKey.ResolveKeycode("Oem8", _ => 0));
            Assert.Equal(new string?[] { "Oem8" }, heard);
            // A keymap change (MappingNotify) re-resolves the same dead key: no second toast.
            Assert.Equal(0, X11PanicKey.ResolveKeycode("Oem8", _ => 0));
            Assert.Single(heard);
            // A different dead key is news; a key that fires and then dies again is news too.
            Assert.Equal(0, X11PanicKey.ResolveKeycode("FnLeftArrow", _ => 0));
            Assert.Equal(9, X11PanicKey.ResolveKeycode("Escape", _ => 9));
            Assert.Equal(0, X11PanicKey.ResolveKeycode("Oem8", _ => 0));
            Assert.Equal(new string?[] { "Oem8", "FnLeftArrow", "Oem8" }, heard);
        }
        finally { X11PanicKey.ResolveKeycode("Escape", _ => 9); X11PanicKey.Unhearable = old; }
    }

    [Fact]
    public void UnhearableWarningQueuesLocalizedTrayFallback() => AvaloniaTestDispatcher.Run(() =>
    {
        var notices = global::ConditioningControlPanel.Avalonia.App.Notifications;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var host = notices.GetType().GetField("_host", flags)!;
        var oldHost = host.GetValue(notices);
        var pending = (System.Collections.IList)notices.GetType().GetField("_pending", flags)!.GetValue(notices)!;
        var count = pending.Count;
        try
        {
            host.SetValue(notices, null); // inspect the real notification queue without creating fonts
            MainShellWindow.WarnUnhearable("Oem8");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(count + 1, pending.Count);
            var notice = pending[count]!.ToString()!;
            Assert.Contains(ConditioningControlPanel.Localization.Loc.GetF("panic_key_unhearable", "Oem8"), notice);
            Assert.Contains("Warning", notice);
        }
        finally
        {
            while (pending.Count > count) pending.RemoveAt(pending.Count - 1);
            host.SetValue(notices, oldHost);
        }
    });

    [Fact]
    public void RebindWhilePortalBound_RebindsAfterCapture_ARefusalIsTheNotice_LastEffectUnbinds() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var notices = global::ConditioningControlPanel.Avalonia.App.Notifications;
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var host = notices.GetType().GetField("_host", flags)!;
        var oldHost = host.GetValue(notices);
        var pending = (System.Collections.IList)notices.GetType().GetField("_pending", flags)!.GetValue(notices)!;
        var pendingCount = pending.Count;
        host.SetValue(notices, null);
        var settings = new SettingsService();
        CoreSettings.ServiceProvider = () => settings;
        var (oldBind, oldUnbind) = (MainShellWindow.PortalBind, MainShellWindow.PortalUnbind);
        var oldCapturing = MainShellWindow.CapturingPanicKey;
        var binds = new List<string>();
        int unbinds = 0;
        string? answer = "Escape";
        MainShellWindow.PortalBind = (key, _, _, _) => { binds.Add(key); return Task.FromResult(answer); };
        MainShellWindow.PortalUnbind = () => { unbinds++; return Task.CompletedTask; };
        MainShellWindow.ResetPortalForTest();
        try
        {
            CoreSettings.Current.PanicKey = "Escape";
            MainShellWindow.BindPortalAsync().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            MainShellWindow.PortalWatchTick(keepBound: true, holdLive: false);
            Assert.Equal(new[] { "Escape" }, binds);

            // Mid-capture nothing moves; once the capture is over the old grab closes and the new key binds.
            MainShellWindow.CapturingPanicKey = true;
            CoreSettings.Current.PanicKey = "F8";
            MainShellWindow.PortalWatchTick(keepBound: true, holdLive: false);
            Assert.Equal(new[] { "Escape" }, binds);
            Assert.Equal(0, unbinds);
            MainShellWindow.CapturingPanicKey = false;
            MainShellWindow.PortalWatchTick(keepBound: true, holdLive: true);   // a leash hold on the old key: wait
            Assert.Equal(new[] { "Escape" }, binds);
            MainShellWindow.PortalWatchTick(keepBound: true, holdLive: false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Escape", "F8" }, binds);
            Assert.Equal(1, unbinds);
            Assert.False(MainShellWindow.PortalFailed);
            MainShellWindow.PortalWatchTick(keepBound: true, holdLive: false);
            Assert.Equal(2, binds.Count);                    // same key: no second round trip

            // Failure path: the compositor refuses the new key. The portal is marked failed (the warning toast,
            // XI2 still listening) and nothing loops on it.
            answer = null;
            CoreSettings.Current.PanicKey = "F9";
            MainShellWindow.PortalWatchTick(keepBound: true, holdLive: false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Escape", "F8", "F9" }, binds);
            Assert.Equal(2, unbinds);
            Assert.True(MainShellWindow.PortalFailed);
            MainShellWindow.PortalWatchTick(keepBound: true, holdLive: false);
            Assert.Equal(3, binds.Count);

            // A slow portal close must serialize ticks and must not bind after the effects stop.
            MainShellWindow.ResetPortalForTest();
            answer = "F9";
            var closing = new TaskCompletionSource();
            MainShellWindow.PortalUnbind = () => { unbinds++; return closing.Task; };
            MainShellWindow.BindPortalAsync().GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.Current.PanicKey = "F10";
            MainShellWindow.PortalWatchTick(keepBound: true, holdLive: false);
            MainShellWindow.PortalWatchTick(keepBound: true, holdLive: false);
            Assert.Equal(3, unbinds); // one close despite two ticks
            MainShellWindow.PortalWatchTick(keepBound: false, holdLive: false);
            closing.SetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("F10", binds); // no late rebind after stop
            MainShellWindow.PortalUnbind = () => { unbinds++; return Task.CompletedTask; };

            // Last effect gone: the grab is released.
            MainShellWindow.PortalWatchTick(keepBound: false, holdLive: false);
            Assert.Equal(5, unbinds);
        }
        finally
        {
            MainShellWindow.ResetPortalForTest();
            (MainShellWindow.PortalBind, MainShellWindow.PortalUnbind) = (oldBind, oldUnbind);
            MainShellWindow.CapturingPanicKey = oldCapturing;
            CoreSettings.ServiceProvider = null;
            while (pending.Count > pendingCount) pending.RemoveAt(pending.Count - 1);
            host.SetValue(notices, oldHost);
        }
    });
    [Fact]
    public Task StopDuringReplacementBindDrainsCleanupBeforeRestart() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var scope = new PortalScope();
        var oldReply = new TaskCompletionSource();
        var keys = new List<string>();
        bool newSession = false, closedNewSession = false;
        MainShellWindow.PortalUnbind = () => { closedNewSession |= newSession; return Task.CompletedTask; };
        MainShellWindow.PortalBind = async (key, _, _, _) =>
        {
            keys.Add(key);
            if (keys.Count == 2)
            {
                await oldReply.Task;
                await MainShellWindow.PortalUnbind(); // backend failure cleanup uses shared portal state
                return null;
            }
            if (keys.Count == 3) newSession = true;
            return key;
        };
        try
        {
            CoreSettings.Current.PanicKey = "Escape";
            await MainShellWindow.BindPortalAsync();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.Current.PanicKey = "F8";
            MainShellWindow.PortalWatchTick(true, false); // replacement bind waits for the portal
            Assert.Equal(new[] { "Escape", "F8" }, keys);
            MainShellWindow.PortalWatchTick(false, false);
            CoreSettings.Current.PanicKey = "F9";
            var restart = MainShellWindow.BindPortalAsync();
            Assert.Equal(2, keys.Count); // newer session must wait for old failure cleanup
            oldReply.SetResult();
            await restart.WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "Escape", "F8", "F9" }, keys);
            Assert.False(MainShellWindow.PortalFailed);
            Assert.False(closedNewSession);
        }
        finally { oldReply.TrySetResult(); Dispatcher.UIThread.RunJobs(); }
    });

    [Fact]
    public void StopBeforePostedBindFailureDoesNotPoisonRestart() => AvaloniaTestDispatcher.Run(() =>
    {
        using var scope = new PortalScope();
        MainShellWindow.PortalUnbind = () => Task.CompletedTask;
        MainShellWindow.PortalBind = (_, _, _, _) => Task.FromResult<string?>(null);
        Assert.True(MainShellWindow.BindPortalAsync().IsCompletedSuccessfully); // failure posted, not applied
        MainShellWindow.PortalWatchTick(false, false);
        MainShellWindow.PortalBind = (key, _, _, _) => Task.FromResult<string?>(key);
        Assert.True(MainShellWindow.BindPortalAsync().IsCompletedSuccessfully);
        Dispatcher.UIThread.RunJobs();
        Assert.False(MainShellWindow.PortalFailed);
    });

    // These tests exercise real notices but must restore their shared queue and host (TestIsolationTests).
    private sealed class PortalScope : IDisposable
    {
        private readonly Func<string, string, Action, Action, Task<string?>> _bind = MainShellWindow.PortalBind;
        private readonly Func<Task> _unbind = MainShellWindow.PortalUnbind;
        private readonly global::ConditioningControlPanel.Avalonia.Helpers.NotificationService _notices =
            global::ConditioningControlPanel.Avalonia.App.Notifications;
        private readonly System.Reflection.FieldInfo _host;
        private readonly object? _oldHost;
        private readonly System.Collections.IList _pending;
        private readonly int _count;
        public PortalScope()
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            _host = _notices.GetType().GetField("_host", flags)!;
            _oldHost = _host.GetValue(_notices);
            _pending = (System.Collections.IList)_notices.GetType().GetField("_pending", flags)!.GetValue(_notices)!;
            _count = _pending.Count;
            _host.SetValue(_notices, null);
            MainShellWindow.ResetPortalForTest();
        }
        public void Dispose()
        {
            MainShellWindow.ResetPortalForTest();
            MainShellWindow.PortalBind = _bind;
            MainShellWindow.PortalUnbind = _unbind;
            while (_pending.Count > _count) _pending.RemoveAt(_pending.Count - 1);
            _host.SetValue(_notices, _oldHost);
        }
    }

}
