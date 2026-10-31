using System;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using System.Linq;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF EmiDeskWindow is a layered window: a click on the transparent pad around her falls through to
/// the desktop. On X11 the desk's input region does that: BodyRoot's rect, following every resize, and the
/// whole window while the ask bubble (WPF Bubble.cs:666) takes clicks.</summary>
public sealed class EmiDeskInputRegionTests
{
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task InputIsHerBodyNotThePadAndTheAskBubbleOpensTheWindow() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var st = EmiState.Current;
        var (left, top, monitor) = (st.WinLeftPx, st.WinTopPx, st.Monitor);
        EmiDeskWindow? desk = null;
        try
        {
            desk = new EmiDeskWindow();
            var requests = new List<PixelRect[]?>();
            desk.InputRegionWriter = rect => requests.Add(rect);
            desk.Show();
            Dispatcher.UIThread.RunJobs();
            AssertBody(desk);
            Assert.Equal(desk.InputRects, requests.Last());
            requests.Clear();

            desk.ApplyBodyWidth(300);
            Dispatcher.UIThread.RunJobs();
            AssertBody(desk);   // the region follows a resize
            Assert.Equal(desk.InputRects, requests.Last());
            requests.Clear();

            // Drive the backend callback so the event subscription and pixel conversion are tested.
            var scaleChanged = (Action<double>)typeof(global::Avalonia.Platform.ITopLevelImpl)
                .GetProperty("ScalingChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(desk.PlatformImpl)!;
            scaleChanged(2);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, desk.RenderScaling);
            AssertBody(desk);
            Assert.NotEmpty(requests);
            Assert.Equal(desk.InputRects, requests[^1]);
            requests.Clear();

            desk.BubbleHost.IsHitTestVisible = true;
            Assert.Null(requests.Last());
            requests.Clear();
            Assert.Null(desk.InputRects);   // ask chips live in the pad: whole window
            desk.BubbleHost.IsHitTestVisible = false;
            AssertBody(desk);
            Assert.Equal(desk.InputRects, requests.Last());
        }
        finally
        {
            desk?.Close();
            st.WinLeftPx = left; st.WinTopPx = top; st.Monitor = monitor;
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public Task TransformedControlsStayClickableAndResetRestoresThePad(int scale) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        var desk = new EmiDeskWindow();
        try
        {
            desk.ApplyBodyWidth(300);
            var requests = new List<PixelRect[]?>();
            desk.InputRegionWriter = r => requests.Add(r);
            desk.Show(); Dispatcher.UIThread.RunJobs();
            var callback = (Action<double>)typeof(global::Avalonia.Platform.ITopLevelImpl)
                .GetProperty("ScalingChanged", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(desk.PlatformImpl)!;
            callback(scale); Dispatcher.UIThread.RunJobs();
            var body = desk.FindControl<Control>("BodyRoot")!;
            var squash = Slot<ScaleTransform>(desk, "_squashScale");
            var rotate = Slot<RotateTransform>(desk, "_wobbleRotate");
            var shift = Slot<TranslateTransform>(desk, "_moveShift");
            var crt = Slot<ScaleTransform>(desk, "_crtScale");
            var land = Slot<TranslateTransform>(desk, "_landShift");
            void Check()
            {
                Assert.Same(desk.InputRects, requests.Last());
                bool Takes(Point p) => desk.InputRects!.Any(r => r.Contains(new PixelPoint(
                    (int)Math.Floor(p.X * scale), (int)Math.Floor(p.Y * scale))));
                foreach (var name in new[] { "BtnClose", "BtnGear", "BtnHelp", "ResizeGrip" })
                {
                    var control = desk.FindControl<Control>(name)!;
                    var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), desk)!.Value;
                    Assert.True(Takes(center), name);
                }
                var toWindow = body.TransformToVisual(desk)!.Value;
                // Midpoints of all four body edges remain covered, including stretched controls.
                foreach (var p in new[] { new Point(1, body.Bounds.Height / 2), new Point(body.Bounds.Width - 1, body.Bounds.Height / 2),
                    new Point(body.Bounds.Width / 2, 1), new Point(body.Bounds.Width / 2, body.Bounds.Height - 1) })
                    Assert.True(Takes(toWindow.Transform(p)));
                // Empty space just outside the transformed left edge must still pass through.
                Assert.False(Takes(toWindow.Transform(new Point(-12, body.Bounds.Height / 2))));
                Assert.False(Takes(new Point(2, 2)));
            }
            squash.ScaleX = 1.25; squash.ScaleY = .8; Check();
            rotate.Angle = 18; Check();
            land.Y = 30; Check();
            crt.ScaleX = .85; crt.ScaleY = .9; Check();
            land.Y = 0; crt.ScaleX = crt.ScaleY = 1;
            // WPF Presentation.cs:52 and :68: frame transforms, followed by real reset path.
            desk.BeginPresentation(() => { });
            desk.PresentAt(new Point(600, 500), -45, -15, "idle", "^_^"); Check();
            desk.EndPresentation(); AssertBody(desk);
            // WPF React.cs:398-403 cancels reactions and restores both transform slots.
            squash.ScaleX = 1.25; rotate.Angle = 18; Check();
            typeof(EmiDeskWindow).GetMethod("TearDownReactions", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(desk, null);
            AssertBody(desk);
            // Bubble mode must remain whole-window even while the body moves.
            desk.BubbleHost.IsHitTestVisible = true; shift.Y = -30;
            Assert.Null(desk.InputRects); Assert.Null(requests.Last());
            desk.BubbleHost.IsHitTestVisible = false; Check();
            shift.Y = 0; AssertBody(desk);
            requests.Clear(); desk.Close(); squash.ScaleX = 1.1;
            Assert.Empty(requests);
        }
        finally { desk.Close(); }
        return Task.CompletedTask;
    });

    private static T Slot<T>(EmiDeskWindow desk, string field) =>
        (T)typeof(EmiDeskWindow).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(desk)!;

    private static void AssertBody(EmiDeskWindow desk)
    {
        var body = (Control)typeof(EmiDeskWindow).GetField("_bodyRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(desk)!;
        var s = desk.RenderScaling;
        var at = body.TranslatePoint(default, desk)!.Value;
        var want = new PixelRect((int)Math.Round(at.X * s), (int)Math.Round(at.Y * s),
                                 (int)Math.Round(body.Bounds.Width * s), (int)Math.Round(body.Bounds.Height * s));
        var got = Assert.Single(desk.InputRects!);
        Assert.InRange(Math.Abs(got.X - want.X), 0, 1);
        Assert.InRange(Math.Abs(got.Y - want.Y), 0, 1);
        Assert.InRange(Math.Abs(got.Width - want.Width), 0, 1);
        Assert.InRange(Math.Abs(got.Height - want.Height), 0, 1);
        Assert.False(got.Contains(new PixelPoint(2, 2)));   // the pad's corner falls through
    }
}
