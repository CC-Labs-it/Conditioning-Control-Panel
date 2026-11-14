using System;
using System.Collections.Generic;
using System.Reflection;
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
/// screen in pixel coordinates when shown, and cover its full pixel bounds after layout.
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
    [InlineData(124.0 / 96, 124.0 / 96, true)]
    [InlineData(1.3, 1.0, true)]
    [InlineData(1.3, 1.0, false)]
    public void EveryCardIsShownCoveringItsWholeScreen(double portraitScale, double landscapeScale, bool dual) => AvaloniaTestDispatcher.Run(() =>
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
        CoreSettings.Current.DualMonitorEnabled = dual;
        var atShow = new List<(LockCardWindow Window, PixelPoint Position)>();
        // Geometry at Show: a window moved after it is mapped is placed by the WM, not by us.
        using var hook = Window.WindowOpenedEvent.AddClassHandler<LockCardWindow>((w, _) => atShow.Add((w, w.Position)));
        try
        {
            LockCardWindow.ShowOnAllMonitors("obey", 1, strictMode: false, isTest: true);
            Assert.Equal(2, atShow.Count);
            foreach (var (window, position) in atShow)
            {
                var screen = window == LockCardWindow.Primary ? landscape : portrait;
                Assert.Equal(screen.Bounds.Position, position);
                // Headless has no monitor DPI transition. Simulate the backend notification;
                // measure the actual client pixels after layout, not Width * target scale.
                var changed = (Action<double>)typeof(ITopLevelImpl)
                    .GetProperty("ScalingChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                    .GetValue(window.PlatformImpl)!;
                changed(screen.Scaling);
            }
            Dispatcher.UIThread.RunJobs();
            foreach (var (window, _) in atShow)
            {
                var bounds = (window == LockCardWindow.Primary ? landscape : portrait).Bounds;
                var actual = PixelRectOf(window);
                Assert.Equal(bounds.Position, actual.Position);
                // Headless rounds client DIPs upward: permit up to one DIP of overshoot,
                // but never an uncovered pixel along either edge.
                var rounding = (int)Math.Ceiling(window.RenderScaling);
                Assert.InRange(actual.Width, bounds.Width, bounds.Width + rounding);
                Assert.InRange(actual.Height, bounds.Height, bounds.Height + rounding);
            }
        }
        finally
        {
            LockCardWindow.ForceCloseAll();
            ScreenList.QueryOverride = oldQuery;
            CoreSettings.Current.DualMonitorEnabled = oldDual;
        }

        PixelRect PixelRectOf(LockCardWindow w)
        {
            return new PixelRect(w.Position, new PixelSize(
                (int)Math.Round(w.ClientSize.Width * w.RenderScaling),
                (int)Math.Round(w.ClientSize.Height * w.RenderScaling)));
        }
    });

    [Fact]
    public void OpenedFallbackDoesNotOverwritePreparedCoverage() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var target = MakeScreen(1, new PixelRect(-1920, 480, 1920, 1080), primary: true);
        var oldQuery = ScreenList.QueryOverride;
        ScreenList.QueryOverride = _ => new[] { target };
        try
        {
            LockCardWindow.ShowOnAllMonitors("obey", 1, strictMode: false, isTest: true);
            Dispatcher.UIThread.RunJobs();
            var card = LockCardWindow.Primary!;
            // The real headless screen differs from our prepared target. Without _covered,
            // the CLR Opened fallback replaces it even though the at-Show check still passes.
            Assert.NotEqual(target.Bounds, card.Screens.Primary!.Bounds);
            Assert.Equal(target.Bounds.Position, card.Position);
            Assert.Equal(new Size(1920, 1080), card.ClientSize);
        }
        finally
        {
            LockCardWindow.ForceCloseAll();
            ScreenList.QueryOverride = oldQuery;
        }
    });
}
