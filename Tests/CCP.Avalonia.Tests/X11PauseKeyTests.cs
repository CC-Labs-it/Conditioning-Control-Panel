using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class X11PauseKeyTests
{
    [Fact]
    public void RawPausePressHonoursBindingRepeatPanicCaptureAndLockdown()
    {
        var s = CoreSettings.Current;
        var old = (s.PauseKey, s.PanicKey, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey);
        var oldLd = LockdownService.Current;
        using var ld = new LockdownService();
        LockdownService.Current = ld;
        try
        {
            (s.PauseKey, s.PanicKey, s.PanicKeyEnabled) = ("F8", "F9", true);
            (s.LockdownForceStrictLock, s.LockdownDisablePanicKey) = (false, false);
            bool down = false, escapeDown = false;
            int queued = 0;
            void Route(bool press, int code, int bound = 42, int panic = 43) =>
                X11PanicKey.RouteRawKeys(press, code, 0, ref escapeDown, () => false, () => { }, bound, panic, ref down, () => queued++);
            Route(true, 42, 0); Assert.Equal(0, queued);
            Route(true, 42); Route(true, 42); Assert.Equal(1, queued);
            Route(false, 42); Route(true, 42); Assert.Equal(2, queued);
            Route(false, 42); Route(true, 42, panic: 42); Assert.Equal(2, queued);
            Route(false, 42);
            DevicesSettingsSection.CapturingPauseKey = true;
            Route(true, 42); Assert.Equal(2, queued);
            DevicesSettingsSection.CapturingPauseKey = false;
            Route(false, 42);
            MainShellWindow.CapturingPanicKey = true;
            Route(true, 42); Assert.Equal(2, queued);
            MainShellWindow.CapturingPanicKey = false;
            Route(false, 42);
            ld.Activate(TimeSpan.FromMinutes(30));
            Route(true, 42); Assert.Equal(2, queued);
        }
        finally
        {
            if (ld.IsActive) ld.Deactivate();
            LockdownService.Current = oldLd;
            DevicesSettingsSection.CapturingPauseKey = false;
            MainShellWindow.CapturingPanicKey = false;
            (s.PauseKey, s.PanicKey, s.PanicKeyEnabled, s.LockdownForceStrictLock, s.LockdownDisablePanicKey) = old;
        }
    }
}
