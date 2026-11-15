using System;
using System.Threading;
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

public class PortalPauseShortcutTests
{
    [Fact]
    public void SignalsAreScopedAndHeldPressesAreSuppressed()
    {
        int presses = 0, closed = 0;
        var binding = new PortalPauseShortcut("G", () => presses++, () => closed++);
        binding.SetSessionForTests("/pause");
        Assert.True(binding.Ready());
        var now = DateTime.UtcNow;
        binding.Route("/panic", "pause", true, now);
        binding.Route("/pause", "panic", true, now);
        binding.Route("/pause", "pause", true, now);
        binding.Route("/pause", "pause", true, now.AddMilliseconds(100));
        Assert.Equal(1, presses);
        binding.Route("/pause", "pause", false, now.AddMilliseconds(150));
        binding.Route("/pause", "pause", true, now.AddMilliseconds(200));
        Assert.Equal(2, presses);
        binding.Dispose();
        binding.Route("/pause", "pause", true, now.AddSeconds(3));
        Assert.Equal(2, presses);
        Assert.Equal(0, closed);
    }

    [Fact]
    public void ReassignedShortcutClosesAndCannotBeReadied()
    {
        int closed = 0;
        var binding = new PortalPauseShortcut("G", () => { }, () => closed++);
        binding.SetSessionForTests("/pause");
        binding.ShortcutChanged("/other", "K");
        binding.ShortcutChanged("/pause", "g");
        Assert.True(binding.IsSafeTrigger("G"));
        Assert.False(binding.IsSafeTrigger("Ctrl+G"));
        binding.ShortcutChanged("/pause", "K");
        Assert.False(binding.Ready());
        Assert.Equal(1, closed);
    }

    [Theory]
    [InlineData("Space", "space")]
    [InlineData("D1", "1")]
    [InlineData("PageUp", "Prior")]
    public void TriggerUsesX11KeysymNames(string key, string trigger)
    {
        var binding = new PortalPauseShortcut(key, () => { }, () => { });
        Assert.True(binding.IsSafeTrigger(trigger));
        binding.Dispose();
    }

    private sealed class Registration : IDisposable
    {
        internal bool Disposed;
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public Task ShellBindsOnlyDuringVideoAndRoutesToGracePause() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var old = (s.PanicKeyEnabled, s.PanicKey, s.PauseKey);
        var oldLockdown = LockdownService.Current;
        bool playing = false;
        int graces = 0;
        var keys = new System.Collections.Generic.List<string>();
        var registrations = new System.Collections.Generic.List<Registration>();
        Action? press = null, closed = null;
        var shell = new MainShellWindow
        {
            PauseKeyWayland = true,
            PauseKeyVideoPlaying = () => playing,
            PauseKeyGracePause = () => { graces++; return true; },
        };
        shell.RegisterWaylandPauseKey = (key, pressed, lost, _) =>
        {
            keys.Add(key); press = pressed; closed = lost;
            var r = new Registration(); registrations.Add(r);
            return Task.FromResult<IDisposable?>(r);
        };
        try
        {
            (s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = (true, "Escape", "F9");
            LockdownService.Current = null;
            shell.StartPauseKeyPortal();
            Assert.Empty(keys);                       // no video: no desktop-wide grab
            playing = true;
            shell.ApplyPauseKeyPortal();
            Assert.Equal(new[] { "F9" }, keys);
            Assert.True(shell.PauseKeyArmed);
            press!();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, graces);

            s.PauseKey = "F10";                       // rebind follows the setting
            shell.ApplyPauseKeyPortal();
            Assert.True(registrations[0].Disposed);
            Assert.Equal("F10", keys[^1]);

            s.PanicKey = "F10";                       // panic wins a shared key: released, press refused
            shell.ApplyPauseKeyPortal();
            Assert.True(registrations[1].Disposed);
            Assert.False(shell.PauseKeyArmed);
            shell.OnPortalPauseKeyPressed();
            Assert.Equal(1, graces);
            s.PanicKey = "Escape";
            shell.ApplyPauseKeyPortal();
            Assert.True(shell.PauseKeyArmed);

            var ld = LockdownService.Current = new LockdownService();
            ld.Activate(TimeSpan.FromMinutes(30));
            press!();                                 // a press racing the poll is refused under Lockdown
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, graces);
            Assert.False(shell.PauseKeyArmed);
            ld.Deactivate();
            LockdownService.Current = null;
            shell.ApplyPauseKeyPortal();
            Assert.True(shell.PauseKeyArmed);

            closed!();                                // portal dropped it: no rebind loop
            Dispatcher.UIThread.RunJobs();
            Assert.False(shell.PauseKeyArmed);
            int count = keys.Count;
            shell.ApplyPauseKeyPortal();
            Assert.Equal(count, keys.Count);

            playing = false;                          // video ended, then the next one rebinds
            shell.ApplyPauseKeyPortal();
            playing = true;
            shell.ApplyPauseKeyPortal();
            Assert.Equal(count + 1, keys.Count);
            shell.StopPauseKeyPortal();
            Assert.True(registrations[^1].Disposed);
        }
        finally
        {
            shell.StopPauseKeyPortal();
            shell.Close();
            LockdownService.Current = oldLockdown;
            (s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = old;
        }
        await Task.CompletedTask;
    });
}
