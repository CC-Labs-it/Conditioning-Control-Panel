using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>User report: the tube never moved on Detach and never sat on main's left edge.</summary>
public sealed class AvatarTubeWindowingTests
{
    /// <summary>WPF's left dock: tube's measured right art edge (less 3 px daylight) on main's left edge.</summary>
    private static PixelPoint Docked(Window main, AvatarTubeWindow tube)
    {
        double s = tube.Width / 780;   // art px scale (headless DesktopScaling is 1)
        int w = (int)Math.Round(tube.Width), h = (int)Math.Round(tube.Height);
        int mainH = (int)Math.Round(main.ClientSize.Height);
        return new PixelPoint(main.Position.X - w + Math.Max(0, StockInsets(tube).Right - 3),
                              (int)Math.Round(main.Position.Y + (mainH - h) / 2.0) + (int)Math.Round(20 * s));
    }

    // Golden alpha columns of Assets/tube.png at alpha >12: [636,1129) of 2048.
    // Independently project the shipped art and avatar; do not call the production measurement.
    private static (int Left, int Right) StockInsets(AvatarTubeWindow tube)
    {
        var frame = tube.FindControl<Image>("ImgTubeFrame")!;
        var avatar = tube.FindControl<Border>("AvatarBorder")!;
        double sc = Math.Min(frame.Bounds.Width / 2048, frame.Bounds.Height / 2048);
        double off = (frame.Bounds.Width - 2048 * sc) / 2;
        // These expectations are used on the unmirrored left/float dock.
        var a = frame.TranslatePoint(new Point(off + 636 * sc, 0), tube)!.Value;
        var b = frame.TranslatePoint(new Point(off + 1129 * sc, 0), tube)!.Value;
        var al = avatar.TranslatePoint(default, tube)!.Value;
        var ar = avatar.TranslatePoint(new Point(avatar.Bounds.Width, 0), tube)!.Value;
        return ((int)Math.Floor(Math.Min(a.X, al.X)), (int)Math.Floor(tube.Width - Math.Max(b.X, ar.X)));
    }

    [Fact]
    public Task AttachedDocksLeftFollowsAndToggles() => Run(detached: false, (main, tube) =>
    {
        Assert.False(tube.Topmost);
        Assert.Equal(Docked(main, tube), tube.Position);

        main.Position = new PixelPoint(2100, 300);   // follow a shell move
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Docked(main, tube), tube.Position);

        tube.FindControl<MenuItem>("MenuItemDetach")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.True(tube.IsDetached);
        Assert.True(tube.Topmost);
        Assert.True(CoreSettings.Current.AvatarTubeDetached);
        Assert.True(tube.FindControl<MenuItem>("MenuItemAttach")!.IsVisible);

        tube.Position = new PixelPoint(50, 60);      // what the WM drag ends in
        Dispatcher.UIThread.RunJobs();
        Assert.True(double.IsNaN(CoreSettings.Current.AvatarTubeLeft) || CoreSettings.Current.AvatarTubeLeft == 123);   // not on every move
        tube.PersistTubePlacement();                 // the pointer release after it
        Assert.Equal(50, CoreSettings.Current.AvatarTubeLeft, 3);
        Assert.Equal(60, CoreSettings.Current.AvatarTubeTop, 3);

        new CompanionHeroCardViewModel().DetachCommand.Execute(null);   // hero chip re-attaches
        Assert.False(tube.IsDetached);
        Assert.False(tube.Topmost);
        Assert.False(CoreSettings.Current.AvatarTubeDetached);
        Assert.Equal(Docked(main, tube), tube.Position);
    });

    /// <summary>audit #1847: the input cut follows the dock side, so her clear margin never sits over
    /// (and steals clicks from) the shell, docked left or right.</summary>
    [Fact]
    public Task InputRectNeverCoversTheShellOnEitherDockSide() => Run(detached: false, (main, tube) =>
    {
        void AssertClear()
        {
            var r = tube.InputRect!.Value;
            var shell = new PixelRect(main.Position, PixelSize.FromSize(main.ClientSize, 1));
            Assert.False(r.Translate(new PixelVector(tube.Position.X, tube.Position.Y)).Intersects(shell));
            Assert.True(r.Width > tube.Width / 4);   // her art still takes clicks
        }
        AssertClear();
        Assert.True(tube.Position.X < main.Position.X);           // left dock
        main.Position = new PixelPoint(0, 200);                    // no room on the left: right dock
        Dispatcher.UIThread.RunJobs();
        Assert.True(tube.Position.X > main.Position.X);
        AssertClear();

        // Float (no room either side, she hangs over the shell): both clear margins are cut, her art keeps input.
        main.WindowState = WindowState.Maximized; // WPF never makes room by resizing a maximized shell
        main.Width = 4000;
        Dispatcher.UIThread.RunJobs();
        var f = tube.InputRect!.Value;
        Assert.True(f.X > 0 && f.Right < (int)Math.Round(tube.Width), $"float rect {f}");
        var insets = StockInsets(tube);
        Assert.Equal((int)Math.Round(tube.Width) - Math.Max(0, insets.Left - 3) - Math.Max(0, insets.Right - 3), f.Width);
    });

    [Fact]
    public Task AttachFromMenu_RestoresTheShellFirst() => Run(detached: true, (main, tube) =>
    {
        main.WindowState = WindowState.Minimized;
        tube.FindControl<MenuItem>("MenuItemAttach")!.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(WindowState.Normal, main.WindowState);
        Assert.False(tube.IsDetached);
        Assert.Equal(Docked(main, tube), tube.Position);
    });

    [Fact]
    public Task DetachedRestoresSavedPlacement() => Run(detached: true, (main, tube) =>
    {
        Assert.True(tube.IsDetached);
        Assert.True(tube.Topmost);
        Assert.Equal(new PixelPoint(123, 45), tube.Position);
    });

    /// <summary>WPF RestoreSavedPlacement (Windowing.cs:2715-2720): a free tube comes back at its saved size.</summary>
    [Fact]
    public Task DetachedRestoresSavedScale() => Run(detached: true, (main, tube) =>
    {
        Assert.Equal(0.75, tube.CurrentScale);
        Assert.Equal(0.75, CoreSettings.Current.AvatarTubeScale);
    }, scale: 0.75);

    private static Task Run(bool detached, Action<Window, AvatarTubeWindow> body, double scale = 1.0) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreSettings.Current.AvatarTubeDetached = detached;
        CoreSettings.Current.AvatarTubeLeft = 123;
        CoreSettings.Current.AvatarTubeTop = 45;
        CoreSettings.Current.AvatarTubeScale = scale;
        var main = new Window { Width = 1000, Height = 700, Position = new PixelPoint(2000, 200) };
        AvatarTubeWindow? tube = null;
        try
        {
            main.Show();
            tube = new AvatarTubeWindow(main);
            tube.Show();
            Dispatcher.UIThread.RunJobs();
            body(main, tube);
        }
        finally
        {
            tube?.Close();
            main.Close();
            Dispatcher.UIThread.RunJobs();
            CoreSettings.Current.AvatarTubeScale = 1.0;
            service.SaveImmediate();
            CoreSettings.ServiceProvider = null;
        }
        return Task.CompletedTask;
    });
}
