using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using System.Linq;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SessionShortcutTests
{
    [Theory]
    [InlineData("PageDown", "Windows+Shift", Key.PageDown, KeyModifiers.Meta | KeyModifiers.Shift)]
    [InlineData("bad-key", "invalid", Key.K, KeyModifiers.Control | KeyModifiers.Alt)]
    [InlineData("K", "", Key.K, KeyModifiers.Control | KeyModifiers.Alt)]
    public void CameraShortcutUsesWpfNamesAndDefaults(string key, string mods, Key expectedKey, KeyModifiers expectedMods)
    {
        var s = CoreSettings.Current.CompanionPrompt;
        var old = (s.CameraShortcutKey, s.CameraShortcutModifiers);
        try
        {
            s.CameraShortcutKey = key; s.CameraShortcutModifiers = mods;
            Assert.Equal((expectedKey, expectedMods), MainShellWindow.SessionShortcut(true));
        }
        finally { (s.CameraShortcutKey, s.CameraShortcutModifiers) = old; }
    }

    [Fact]
    public Task RebindDisposesOldGlobalsAndKeepsOneCameraBindingWhenGlobalIsOff() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var oldBind = MainShellWindow.BindSessionHotkey;
        var s = CoreSettings.Current.CompanionPrompt;
        var old = (s.ChatShortcutGlobal, s.CameraShortcutGlobal);
        var leases = new List<Lease>();
        MainShellWindow.BindSessionHotkey = (_, _, _) => { var lease = new Lease(); leases.Add(lease); return lease; };
        var shell = new MainShellWindow();
        try
        {
            s.ChatShortcutGlobal = s.CameraShortcutGlobal = true;
            shell.Show();
            Assert.Equal(2, leases.Count);
            var count = shell.KeyBindings.Count;
            shell.ApplySessionShortcuts();
            Assert.True(leases[0].Disposed && leases[1].Disposed);
            Assert.Equal(4, leases.Count);
            Assert.Equal(count, shell.KeyBindings.Count);
            s.ChatShortcutGlobal = s.CameraShortcutGlobal = false;
            shell.ApplySessionShortcuts();
            Assert.True(leases[2].Disposed && leases[3].Disposed);
            Assert.Equal(4, leases.Count);
            Assert.Equal(count, shell.KeyBindings.Count);
        }
        finally { shell.Close(); MainShellWindow.BindSessionHotkey = oldBind; (s.ChatShortcutGlobal, s.CameraShortcutGlobal) = old; }
        return Task.CompletedTask;
    });
    [Fact]
    public Task CameraGlobalRestoresTheShellAndAsksForConsentBeforeStarting() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current == null) AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var bind = MainShellWindow.BindSessionHotkey;
        var settings = CoreSettings.Current;
        var old = (settings.WebcamConsentGiven, settings.CompanionPrompt.CameraShortcutGlobal);
        Action? camera = null;
        MainShellWindow.BindSessionHotkey = (key, _, press) =>
        { if (key == MainShellWindow.SessionShortcut(true).Key) camera = press; return new Lease(); };
        var shell = new MainShellWindow();
        try
        {
            settings.WebcamConsentGiven = false;
            settings.CompanionPrompt.CameraShortcutGlobal = true;
            shell.Show(); shell.WindowState = WindowState.Minimized; shell.Hide();
            Assert.NotNull(camera);
            camera(); Dispatcher.UIThread.RunJobs();
            Assert.True(shell.IsVisible);
            Assert.Equal(WindowState.Normal, shell.WindowState);
            Assert.Single(shell.OwnedWindows.OfType<WebcamConsentDialog>());
            Assert.False(ConditioningControlPanel.Avalonia.Platform.WebcamTracker.Instance.IsRunning);
        }
        finally
        {
            foreach (var window in shell.OwnedWindows.ToArray()) window.Close();
            shell.Close(); MainShellWindow.BindSessionHotkey = bind;
            (settings.WebcamConsentGiven, settings.CompanionPrompt.CameraShortcutGlobal) = old;
        }
        return Task.CompletedTask;
    });
    private sealed class Lease : IDisposable { internal bool Disposed; public void Dispose() => Disposed = true; }
}
