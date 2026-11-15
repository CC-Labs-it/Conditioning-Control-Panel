using System;
using System.Collections.Generic;
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
using Tmds.DBus.Protocol;
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
    public void LostSessionClosesOnceAndCannotBeReadied()
    {
        int closed = 0;
        var binding = new PortalPauseShortcut("G", () => { }, () => closed++);
        binding.SetSessionForTests("/pause");
        binding.Lost();
        binding.Lost();
        Assert.False(binding.Ready());
        Assert.Equal(1, closed);
    }

    // BindShortcuts responses as KDE and GNOME send them: trigger_description is display text.
    private static Dictionary<string, VariantValue> Response(string id, string description) => new()
    {
        ["shortcuts"] = new Array<Struct<string, Dict<string, VariantValue>>>(new[]
        {
            Struct.Create(id, new Dict<string, VariantValue> { ["trigger_description"] = VariantValue.Variant(description) }),
        }).AsVariantValue(),
    };

    [Theory]
    [InlineData("Page Up")]
    [InlineData("Bild auf")]
    [InlineData("Ctrl+Shift+F9")]
    [InlineData("")]
    public void AnyListedPauseShortcutIsBoundWhateverItsDescription(string description)
    {
        Assert.True(PortalPauseShortcut.Bound(0, Response("pause", description)));
        Assert.False(PortalPauseShortcut.Bound(1, Response("pause", description)));   // refused or cancelled
        Assert.False(PortalPauseShortcut.Bound(0, Response("panic", description)));   // ours not bound
        Assert.False(PortalPauseShortcut.Bound(0, new Dictionary<string, VariantValue>()));
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
            shell.OnPortalPauseKeyPressed("F10");
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
    [Fact]
    public Task StaleActivationCannotPauseAfterRebindOrVideoEnd() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = CoreSettings.Current;
        var old = (s.PanicKeyEnabled, s.PanicKey, s.PauseKey);
        var oldLockdown = LockdownService.Current;
        bool playing = true;
        int graces = 0;
        Action? press = null;
        var registrations = new System.Collections.Generic.List<Registration>();
        var shell = new MainShellWindow
        {
            PauseKeyWayland = true,
            PauseKeyVideoPlaying = () => playing,
            PauseKeyGracePause = () => { graces++; return true; },
        };
        shell.RegisterWaylandPauseKey = (_, pressed, _, _) =>
        {
            press = pressed;
            var r = new Registration();
            registrations.Add(r);
            return Task.FromResult<IDisposable?>(r);
        };
        try
        {
            (s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = (true, "Escape", "F9");
            LockdownService.Current = null;
            shell.StartPauseKeyPortal();
            var oldPress = press!;
            s.PauseKey = "F10"; // no poll has run yet
            oldPress();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0, graces);
            Assert.True(registrations[0].Disposed);
            Assert.Equal(2, registrations.Count);
            oldPress(); // callback queued from the old generation
            press!();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, graces);
            playing = false; // no poll has run yet
            press!();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, graces);
            Assert.True(registrations[1].Disposed);
        }
        finally
        {
            shell.StopPauseKeyPortal();
            shell.Close();
            LockdownService.Current = oldLockdown;
            (s.PanicKeyEnabled, s.PanicKey, s.PauseKey) = old;
        }
        return Task.CompletedTask;
    });

}
