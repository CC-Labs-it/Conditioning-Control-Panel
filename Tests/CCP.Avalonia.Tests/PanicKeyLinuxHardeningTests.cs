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
/// press (MainWindow.xaml.cs:897-911 capture, GlobalKeyboardHook), so on Linux: a bound key X cannot hear is warned
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
        finally { X11PanicKey.Unhearable = old; }
    }

    [Fact]
    public void RebindWhilePortalBound_RebindsAfterCapture_ARefusalIsTheNotice_LastEffectUnbinds() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
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

            // Last effect gone: the grab is released.
            MainShellWindow.PortalWatchTick(keepBound: false, holdLive: false);
            Assert.Equal(3, unbinds);
        }
        finally
        {
            MainShellWindow.ResetPortalForTest();
            (MainShellWindow.PortalBind, MainShellWindow.PortalUnbind) = (oldBind, oldUnbind);
            MainShellWindow.CapturingPanicKey = oldCapturing;
            CoreSettings.ServiceProvider = null;
        }
    });
}
