using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Platform;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// User report 2026-10-31: the lock card covered the portrait 1.3x monitor but only the top of the
/// landscape 1.0x primary; the bottom stayed clickable. Live X geometry showed the primary card at
/// 2496x1404+1440+178 instead of +480: it was shown first, covered the monitor it spawned on, then
/// moved while mapped and KWin clamped the move. Every card must therefore already sit on its own
/// screen, full size, in the pixel space Position uses, at the moment it is shown.
/// </summary>
public sealed class LockCardMultiMonitorTests
{
    private static Screen MakeScreen(double scaling, PixelRect bounds, bool primary)
    {
        // Same Avalonia 12.1 internals as MultiMonitorResultTests.MakeScreen.
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(new IntPtr(bounds.X + 1), "headless-test") })!;
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, scaling);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, primary);
        return screen;
    }

    // The reporter's desk in X coordinates (xrandr under XWayland): DP-1 portrait 1440x2560+0+0,
    // DP-3 landscape primary 2496x1404+1440+480. XWayland reports one scale (Xft.dpi 124 -> 1.2917)
    // for both; a per-monitor backend would report kscreen's 1.3 and 1.0.
    [Theory]
    [InlineData(124.0 / 96, 124.0 / 96)]
    [InlineData(1.3, 1.0)]
    public void EveryCardIsShownCoveringItsWholeScreen(double portraitScale, double landscapeScale) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var portrait = MakeScreen(portraitScale, new PixelRect(0, 0, 1440, 2560), primary: false);
        var landscape = MakeScreen(landscapeScale, new PixelRect(1440, 480, 2496, 1404), primary: true);
        var oldQuery = ScreenList.QueryOverride;
        var oldDual = CoreSettings.Current.DualMonitorEnabled;
        ScreenList.QueryOverride = _ => new[] { portrait, landscape };
        CoreSettings.Current.DualMonitorEnabled = true;
        var atShow = new List<(LockCardWindow Window, PixelRect Rect)>();
        // Geometry at Show: a window moved after it is mapped is placed by the WM, not by us.
        using var hook = Window.WindowOpenedEvent.AddClassHandler<LockCardWindow>((w, _) => atShow.Add((w, PixelRectOf(w))));
        try
        {
            LockCardWindow.ShowOnAllMonitors("obey", 1, strictMode: false, isTest: true);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(2, atShow.Count);
            var primaryRect = atShow.Single(s => s.Window == LockCardWindow.Primary).Rect;
            Assert.Equal(landscape.Bounds, primaryRect);   // the typing card is on the primary, all of it
            Assert.Equal(portrait.Bounds, atShow.Single(s => s.Window != LockCardWindow.Primary).Rect);
        }
        finally
        {
            LockCardWindow.ForceCloseAll();
            ScreenList.QueryOverride = oldQuery;
            CoreSettings.Current.DualMonitorEnabled = oldDual;
        }

        PixelRect PixelRectOf(LockCardWindow w)
        {
            // Width/Height are DIPs of the target screen; Position is in that same pixel space.
            var k = w == LockCardWindow.Primary ? landscape.Scaling : portrait.Scaling;
            return new PixelRect(w.Position, new PixelSize((int)Math.Round(w.Width * k), (int)Math.Round(w.Height * k)));
        }
    });
}
