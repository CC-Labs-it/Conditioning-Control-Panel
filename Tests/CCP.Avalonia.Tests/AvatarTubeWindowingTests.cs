using System;
using System.Linq;
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
    /// <summary>WPF's left dock: tube's right art edge (353*s in, less 3 px daylight) on main's left edge.</summary>
    private static PixelPoint Docked(Window main, AvatarTubeWindow tube)
    {
        double s = tube.Width / 780;   // art px scale (headless DesktopScaling is 1)
        int w = (int)Math.Round(tube.Width), h = (int)Math.Round(tube.Height);
        int mainH = (int)Math.Round(main.ClientSize.Height);
        return new PixelPoint(main.Position.X - w + (int)Math.Round(353 * s - 3),
                              (int)Math.Round(main.Position.Y + (mainH - h) / 2.0) + (int)Math.Round(20 * s));
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
        Assert.True(f.Width > tube.Width / 4);
    });

    /// <summary>The painted-pixel shape never reaches over the shell either, on either dock side.</summary>
    [Fact]
    public Task InputShapeStaysInsideTheDockCut() => Run(detached: false, (main, tube) =>
    {
        void AssertClear()
        {
            tube.ApplyInputShape();
            var clip = tube.InputRect!.Value;
            var shell = new PixelRect(main.Position, PixelSize.FromSize(main.ClientSize, 1));
            Assert.NotEmpty(tube.InputRects!);
            foreach (var r in tube.InputRects!)
            {
                Assert.Equal(r, clip.Intersect(r));
                Assert.False(r.Translate(new PixelVector(tube.Position.X, tube.Position.Y)).Intersects(shell), $"{r} over the shell");
            }
        }
        AssertClear();
        main.Position = new PixelPoint(0, 200);   // right dock
        Dispatcher.UIThread.RunJobs();
        AssertClear();
    });

    /// <summary>Owner, Linux beta: the transparent 780x1080 box swallowed clicks meant for the desktop.
    /// WPF's layered window only takes the mouse on painted pixels (TubeArtAlpha.cs:8-18); the X11
    /// input shape is the art's alpha plus the visible elements over it, and follows the bubble.</summary>
    [Fact]
    public Task InputShapeIsThePaintedPartsAndFollowsTheBubble() => Run(detached: false, (main, tube) =>
    {
        PixelRect[]? sent = null;
        int writes = 0;
        tube.InputShapeWriter = r => { sent = r; writes++; };
        tube.ApplyInputShape();
        var rects = tube.InputRects!;
        Assert.Same(rects, sent);
        int w = (int)Math.Round(tube.Width), h = (int)Math.Round(tube.Height);
        // The canvas corners and the clear band above the glass are empty: clicks fall through.
        Assert.False(TubeInputShape.Hits(rects, 2, 2));
        Assert.False(TubeInputShape.Hits(rects, w - 3, h - 3));
        Assert.False(TubeInputShape.Hits(rects, w / 2, 40));
        // ...while her, her plate and the glass take them.
        Assert.True(TubeInputShape.Hits(rects, Center(tube, "ImgAvatar").X, Center(tube, "ImgAvatar").Y));
        Assert.True(TubeInputShape.Hits(rects, Center(tube, "TitleBox").X, Center(tube, "TitleBox").Y));
        long area = 0;
        foreach (var r in rects) area += (long)r.Width * r.Height;
        Assert.True(area < (long)w * h / 2, $"{area} of {w * h} px take input");

        tube.ApplyInputShape();
        Assert.Equal(1, writes);   // unchanged: the server is not asked again

        var bubble = tube.FindControl<Border>("SpeechBubble")!;
        tube.FindControl<TextBlock>("TxtSpeech")!.Text = "Could not load those pictures. Try again in a moment?";
        bubble.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        tube.ApplyInputShape();
        var box = new PixelRect(PixelPoint.FromPoint(bubble.TranslatePoint(default, tube)!.Value, 1),
            PixelPoint.FromPoint(bubble.TranslatePoint(new Point(bubble.Bounds.Width, bubble.Bounds.Height), tube)!.Value, 1));
        var withBubble = tube.InputRects!;
        Assert.True(TubeInputShape.Hits(withBubble, box.Center.X, box.Center.Y));
        Assert.True(TubeInputShape.Hits(withBubble, box.X + 4, box.Y + 4));
        Assert.Equal(2, writes);

        bubble.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        tube.ApplyInputShape();
        Assert.False(TubeInputShape.Hits(tube.InputRects!, box.X + 4, box.Y + 4));   // its corner is clear air again
    });

    [Fact]
    public Task DetachedShapeHasNoDockCutButStillPassesTheClearAir() => Run(detached: true, (main, tube) =>
    {
        tube.InputShapeWriter = _ => { };
        tube.ApplyInputShape();
        Assert.Null(tube.InputRect);
        Assert.NotEmpty(tube.InputRects!);
        Assert.False(TubeInputShape.Hits(tube.InputRects!, 2, 2));
        Assert.True(TubeInputShape.Hits(tube.InputRects!, Center(tube, "ImgAvatar").X, Center(tube, "ImgAvatar").Y));
    });

    [Fact]
    public void ArtMaskMergesRowsAndDropsTheHaze()
    {
        var px = new byte[4 * 3 * 4];
        void A(int x, int y, byte a) => px[(y * 4 + x) * 4 + 3] = a;
        A(1, 0, 255); A(2, 0, 255);
        A(1, 1, 200); A(2, 1, 9);
        A(0, 2, 8); A(3, 2, 9);   // 8 is TubeArtAlpha's haze floor: gone; 9 is art
        var mask = TubeArtMask.FromPixels(px, 4, 3, 16);
        Assert.Equal(new[] { new MaskRun(1, 0, 3, 2), new MaskRun(3, 2, 4, 3) },
            mask.Runs.OrderBy(r => r.Y0).ToArray());

        var into = new System.Collections.Generic.List<PixelRect>();
        TubeInputShape.AddArt(into, mask, new Rect(10, 20, 8, 6));   // 2x scale, offset
        Assert.Contains(new PixelRect(12, 20, 4, 4), into);
        Assert.Contains(new PixelRect(16, 24, 2, 2), into);
    }

    /// <summary>The owner's WSLg desk in X coordinates (xdpyinfo XINERAMA): the 1680x1050 monitor at
    /// 0,235 and the 2048x1152@125% primary as 2560x1440 at 1680,0. Docking only ever looks at MAIN's
    /// monitor, as WPF's DockWorkAreaPx does: no room left of main on its own monitor means a right
    /// dock, even with another monitor on that side. Tube 768x1005, insets 232/345 as UpdatePosition
    /// computes them for that size.</summary>
    [Theory]
    [InlineData(1998, 229, ConditioningControlPanel.AvatarTubeLayout.DockSide.Left, 1575)]   // live state 11-06: X has the tube at +1575
    [InlineData(1700, 229, ConditioningControlPanel.AvatarTubeLayout.DockSide.Right, 3129)]  // main near the primary's left edge
    public void OwnersTwoMonitorDeskDocksLikeWpf(int mainX, int mainY, ConditioningControlPanel.AvatarTubeLayout.DockSide side, int left)
    {
        var primary = new ConditioningControlPanel.AvatarTubeLayout.Box(1680, 0, 2560, 1440);
        var plan = ConditioningControlPanel.AvatarTubeLayout.TubeDockPlacement.Place(
            new ConditioningControlPanel.AvatarTubeLayout.Box(mainX, mainY, 1661, 1002), 768, 1005, 232, 345, 20, primary);
        Assert.Equal(side, plan.Side);
        Assert.Equal(left, plan.Left);
    }

    private static PixelPoint Center(AvatarTubeWindow tube, string name)
    {
        var c = tube.FindControl<Control>(name)!;
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), tube)!.Value;
        return new PixelPoint((int)p.X, (int)p.Y);
    }

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
