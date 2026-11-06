using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
    /// input shape is the art's alpha plus the visible elements over it. The speech bubble is its own
    /// window now (WPF Speech.cs:1060), so a line going up leaves the tube's region alone.</summary>
    [Fact]
    public Task InputShapeIsThePaintedPartsAndTheBubbleLeavesIt() => Run(detached: false, (main, tube) =>
    {
        // Hold the idle bob (the CPU-path tier): it moves her on the real clock between the reads, which
        // is not what this test is about (it flaked on CI at c1a1ef199f, linux shard 0).
        var settings = CoreSettings.Current;
        settings.LighterEffects = ConditioningControlPanel.Models.LighterEffectsMode.On;
        global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
        try
        {
            Assert.False(tube.IsFloating);
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

            tube.GigglePriority("I'm right here, sweetie. Just say the word.", playSound: false, aiGenerated: false);
            Dispatcher.UIThread.RunJobs();
            Assert.True(tube.BubbleWindow!.IsVisible);
            tube.ApplyInputShape();
            Assert.True(TubeInputShape.SameRects(rects, tube.InputRects));   // not a pixel more over the desktop
            Assert.Equal(1, writes);
        }
        finally
        {
            settings.LighterEffects = ConditioningControlPanel.Models.LighterEffectsMode.Auto;
            global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
        }
    });

    /// <summary>Owner decision 11-06: WPF's bubble window (Speech.cs:1060-1140) on Linux too - its own
    /// non-topmost window owned by the tube, placed beside her and off main, real size.</summary>
    [Fact]
    public Task BubbleIsItsOwnWindowBesideHerAndOffTheShell() => Run(detached: false, (main, tube) =>
    {
        var bubble = tube.FindControl<Border>("SpeechBubble")!;
        Assert.NotNull(tube.BubbleWindow);
        Assert.False(tube.BubbleWindow!.IsVisible);   // created on open, hidden until she speaks

        tube.GigglePriority("I'm right here, sweetie. Just say the word.", playSound: false, aiGenerated: false);
        Dispatcher.UIThread.RunJobs();
        var w = tube.BubbleWindow!;
        Assert.True(w.IsVisible);
        Assert.False(w.Topmost);                                    // attached: behind other apps with the tube
        Assert.False(w.ShowActivated);
        Assert.False(w.ShowInTaskbar);
        Assert.Same(w, TopLevel.GetTopLevel(bubble));               // drawn by its own window...
        Assert.Same(tube, TopLevel.GetTopLevel(tube.FindControl<Border>("InputPanel")!));   // ...the chat input stays in the tube
        Assert.Equal(15, tube.FindControl<TextBlock>("TxtSpeech")!.FontSize);

        // Placed by Core's SpeechBubblePlacement in physical px: off main (she is docked left of it),
        // the window at the bubble less its 16 DIP margin.
        var plan = tube.LastBubblePlan!.Value;
        Assert.True(plan.Bubble.Right <= main.Position.X - ConditioningControlPanel.AvatarTubeLayout.SpeechBubblePlacement.Gap + 0.5,
            $"bubble {plan.Bubble} reaches main at {main.Position.X}");
        Assert.True(plan.Bubble.W <= AvatarTubeWindow.SpeechBubbleMaxWidth + 0.5);
        Assert.Equal(new PixelPoint((int)Math.Round(plan.Bubble.X - 16), (int)Math.Round(plan.Bubble.Y - 16)), w.Position);

        tube.EndSpeechBubble();
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.IsVisible);
    });

    /// <summary>Its own input shape: only the bubble (and its tail) takes the mouse; the margin
    /// around it, room for the shadow, is click-through like WPF's per-pixel layered window.</summary>
    [Fact]
    public Task BubbleWindowTakesInputOnlyOnTheBubble() => Run(detached: false, (main, tube) =>
    {
        PixelRect[]? sent = null;
        tube.BubbleShapeWriter = r => sent = r;
        tube.GigglePriority("Could you tell me what you want to see next?", playSound: false, aiGenerated: false);
        Dispatcher.UIThread.RunJobs();
        tube.ApplyBubbleShape();
        var w = tube.BubbleWindow!;
        var bubble = tube.FindControl<Border>("SpeechBubble")!;
        var c = bubble.TranslatePoint(new Point(bubble.Bounds.Width / 2, bubble.Bounds.Height / 2), w)!.Value;
        Assert.NotNull(sent);
        Assert.True(TubeInputShape.Hits(sent!, (int)c.X, (int)c.Y));
        Assert.False(TubeInputShape.Hits(sent!, 2, 2));                                         // the margin
        Assert.False(TubeInputShape.Hits(sent!, (int)w.ClientSize.Width - 3, 2));
        Assert.InRange(sent!.Length, 1, 2);                                                     // the bubble, the tail
    });

    /// <summary>WPF ReassertTopmost / DemoteSpeechBubble (Windowing.cs:1928-1975) and
    /// SpeechBubbleVisibility: the bubble rides in the tube's band and goes with the tube (and,
    /// attached, with main).</summary>
    [Fact]
    public Task BubbleWindowFollowsTheTubesBandAndVisibility() => Run(detached: false, (main, tube) =>
    {
        tube.GigglePriority("Still with you.", playSound: false, aiGenerated: false);
        Dispatcher.UIThread.RunJobs();
        var w = tube.BubbleWindow!;
        Assert.True(w.IsVisible);

        tube.Detach();
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.Topmost);           // a free tube is topmost; its bubble sits in that band above it
        Assert.True(w.IsVisible);
        tube.Attach();
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.Topmost);

        main.WindowState = WindowState.Minimized;   // attached: the bubble goes with main
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.IsVisible);
        main.WindowState = WindowState.Normal;
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.IsVisible);

        tube.Hide();
        Dispatcher.UIThread.RunJobs();
        Assert.False(w.IsVisible);
        tube.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(w.IsVisible);         // the line is still up: it comes back with her
    });

    /// <summary>The buttons riding the bubble still take real clicks in its window: the chat log's
    /// close button, hit-tested by a pointer press and release at its centre.</summary>
    [Fact]
    public Task ButtonsInTheBubbleWindowStillClick() => Run(detached: false, (main, tube) =>
    {
        tube.ShowChatHistory();
        Dispatcher.UIThread.RunJobs();
        var w = tube.BubbleWindow!;
        Assert.True(w.IsVisible);
        Assert.Equal(AvatarTubeWindow.SpeechBubbleHistoryWidth, tube.FindControl<Border>("SpeechBubble")!.MaxWidth);
        var close = tube.FindControl<Button>("BtnCloseChatHistory")!;
        Assert.Same(w, TopLevel.GetTopLevel(close));
        var p = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), w)!.Value;
        w.MouseDown(p, MouseButton.Left);
        w.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
        Assert.False(w.IsVisible);
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
