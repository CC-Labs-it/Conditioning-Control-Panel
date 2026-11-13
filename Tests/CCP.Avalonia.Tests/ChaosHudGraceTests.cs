using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChaosHudGraceTests
{
    private static void Setup()
    {
        var builder = AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
            .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register);
        if (Application.Current is null)
            builder.UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        else if (!FontManager.Current.TryGetGlyphTypeface(new Typeface(new FontFamily(AppFonts.Inter)), out _))
            builder.AfterSetupCallback(builder);
    }

    [Fact]
    public void NativeWindowsPointerProbeIsPlatformGuarded()
    {
        if (OperatingSystem.IsWindows()) Assert.NotNull(Win32Overlay.CursorPosition());
        else Assert.Null(Win32Overlay.CursorPosition());
    }

    [Fact]
    public void GeometryAndUnavailablePointerFallback()
    {
        var panel = new Rect(100, 0, 300, 400);
        var strip = new Rect(0, 0, 100, 400);
        Assert.True(ChaosHudWindow.CursorInGrace(panel, strip, new Point(50, 50), 26));
        Assert.True(ChaosHudWindow.CursorInGrace(panel, strip, new Point(-20, 50), 26));
        Assert.False(ChaosHudWindow.CursorInGrace(panel, strip, new Point(-30, 50), 26));
        Assert.True(ChaosHudWindow.CursorInGrace(panel, strip, new Point(420, 50), 26));
        Assert.False(ChaosHudWindow.CursorInGrace(panel, strip, new Point(430, 50), 26));
        Assert.True(ChaosHudWindow.CursorInGrace(panel, strip, null, 26, true));
        Assert.False(ChaosHudWindow.CursorInGrace(panel, strip, null, 26, false));
    }

    [Fact]
    public async Task CurrentDesktopPointerControlsGraceAndUnavailablePointerDoesNotPollStaleExit()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            Setup();
            var hud = new ChaosHudWindow();
            try
            {
                hud.Show();
                hud.UpdateLayout();
                // The headless pointer is not over the window, like a popup taking the hit test.
                hud.ReadPointer = () => hud.PointToScreen(new Point(50, 50));
                Assert.True(hud.CursorInHudGrace());
                hud.ReadPointer = () => hud.PointToScreen(new Point(-100, 50));
                Assert.False(hud.CursorInHudGrace());
                hud.ReadPointer = () => hud.PointToScreen(new Point(hud.Bounds.Width + 100, 50));
                Assert.False(hud.CursorInHudGrace());
                hud.ReadPointer = () => hud.PointToScreen(new Point(50, 50));
                Assert.True(hud.CursorInHudGrace());
                hud.ReadPointer = () => null;
                Assert.False(hud.CursorInHudGrace());
            }
            finally { hud.Close(); }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task CollapseTimerKeepsPopupOverlapAndHaloThenClosesBeyondIt()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var hud = new ChaosHudWindow(new ChaosRunState(new ChaosRunConfig()));
            var popup = new Window { Width = 100, Height = 100 };
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var expanded = typeof(ChaosHudWindow).GetField("_expanded", flags)!;
            var recheck = typeof(ChaosHudWindow).GetField("_collapseRecheck", flags)!;
            var expand = typeof(ChaosHudWindow).GetMethod("Expand", flags)!;
            var leave = typeof(ChaosHudWindow).GetMethod("ScheduleCollapseRecheck", flags)!;
            PixelPoint current = default;
            int reads = 0;
            try
            {
                hud.ReadPointer = () => { reads++; return current; };
                hud.Show();
                hud.UpdateLayout();
                expand.Invoke(hud, null);
                await Task.Delay(300);
                hud.UpdateLayout();
                var panel = hud.FindControl<Border>("Panel")!;
                var origin = panel.TranslatePoint(new Point(0, 0), hud)!.Value;
                current = hud.PointToScreen(new Point(origin.X + 50, origin.Y + 50));
                popup.Position = new PixelPoint(current.X - 10, current.Y - 10);
                popup.Show();
                popup.MouseMove(new Point(10, 10));
                Assert.False(panel.IsPointerOver);
                leave.Invoke(hud, null);
                await Task.Delay(1500);
                Assert.True(reads > 0);
                Assert.True((bool)expanded.GetValue(hud)!);
                Assert.True(((DispatcherTimer)recheck.GetValue(hud)!).IsEnabled);
                current = hud.PointToScreen(new Point(origin.X - 20, origin.Y + 50));
                await Task.Delay(700);
                Assert.True((bool)expanded.GetValue(hud)!);
                current = hud.PointToScreen(new Point(origin.X + panel.Bounds.Width + 20, origin.Y + 50));
                await Task.Delay(700);
                Assert.True((bool)expanded.GetValue(hud)!);
                current = hud.PointToScreen(new Point(origin.X + panel.Bounds.Width + 60, origin.Y + 50));
                await Task.Delay(700);
                Assert.False((bool)expanded.GetValue(hud)!);
                Assert.False(((DispatcherTimer)recheck.GetValue(hud)!).IsEnabled);
                expand.Invoke(hud, null);
                leave.Invoke(hud, null);
                Assert.True(((DispatcherTimer)recheck.GetValue(hud)!).IsEnabled);
                hud.Close();
                Assert.False(((DispatcherTimer)recheck.GetValue(hud)!).IsEnabled);
            }
            finally { popup.Close(); hud.Close(); }
        });
    }

    [Fact]
    public async Task CollapseTimerWaitsForUnavailablePointerAndUsesRecoveredPosition()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            Setup();
            var hud = new ChaosHudWindow(new ChaosRunState(new ChaosRunConfig()));
            var popup = new Window { Width = 100, Height = 100 };
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var expanded = typeof(ChaosHudWindow).GetField("_expanded", flags)!;
            var recheck = typeof(ChaosHudWindow).GetField("_collapseRecheck", flags)!;
            var expand = typeof(ChaosHudWindow).GetMethod("Expand", flags)!;
            var leave = typeof(ChaosHudWindow).GetMethod("ScheduleCollapseRecheck", flags)!;
            PixelPoint? current = null;
            int reads = 0;
            try
            {
                hud.ReadPointer = () => { reads++; return current; };
                hud.Show();
                hud.UpdateLayout();
                expand.Invoke(hud, null);
                await Task.Delay(300);
                hud.UpdateLayout();
                var panel = hud.FindControl<Border>("Panel")!;
                var origin = panel.TranslatePoint(new Point(0, 0), hud)!.Value;
                var inside = hud.PointToScreen(new Point(origin.X + 50, origin.Y + 50));
                popup.Position = new PixelPoint(inside.X - 10, inside.Y - 10);
                popup.Show();
                popup.MouseMove(new Point(10, 10));
                Assert.False(panel.IsPointerOver);
                leave.Invoke(hud, null);
                await Task.Delay(1500);
                Assert.True(reads > 0);
                Assert.True((bool)expanded.GetValue(hud)!);
                Assert.True(((DispatcherTimer)recheck.GetValue(hud)!).IsEnabled);
                current = hud.PointToScreen(new Point(origin.X + panel.Bounds.Width + 20, origin.Y + 50));
                await Task.Delay(700);
                Assert.True((bool)expanded.GetValue(hud)!);
                current = hud.PointToScreen(new Point(origin.X + panel.Bounds.Width + 60, origin.Y + 50));
                await Task.Delay(700);
                Assert.False((bool)expanded.GetValue(hud)!);
                Assert.False(((DispatcherTimer)recheck.GetValue(hud)!).IsEnabled);
                expand.Invoke(hud, null);
                current = null;
                leave.Invoke(hud, null);
                hud.Close();
                Assert.False(((DispatcherTimer)recheck.GetValue(hud)!).IsEnabled);
            }
            finally { popup.Close(); hud.Close(); }
        });
    }

}
