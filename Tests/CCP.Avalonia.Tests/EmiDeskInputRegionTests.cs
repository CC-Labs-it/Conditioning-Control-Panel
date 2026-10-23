using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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
            desk.Show();
            Dispatcher.UIThread.RunJobs();
            AssertBody(desk);

            desk.ApplyBodyWidth(300);
            Dispatcher.UIThread.RunJobs();
            AssertBody(desk);   // the region follows a resize

            desk.BubbleHost.IsHitTestVisible = true;
            Assert.Null(desk.InputRect);   // ask chips live in the pad: whole window
            desk.BubbleHost.IsHitTestVisible = false;
            AssertBody(desk);
        }
        finally
        {
            desk?.Close();
            st.WinLeftPx = left; st.WinTopPx = top; st.Monitor = monitor;
        }
        return Task.CompletedTask;
    });

    private static void AssertBody(EmiDeskWindow desk)
    {
        var body = (Control)typeof(EmiDeskWindow).GetField("_bodyRoot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(desk)!;
        var s = desk.RenderScaling;
        var at = body.TranslatePoint(default, desk)!.Value;
        var want = new PixelRect((int)Math.Round(at.X * s), (int)Math.Round(at.Y * s),
                                 (int)Math.Round(body.Bounds.Width * s), (int)Math.Round(body.Bounds.Height * s));
        var got = desk.InputRect;
        Assert.NotNull(got);
        Assert.InRange(Math.Abs(got!.Value.X - want.X), 0, 1);
        Assert.InRange(Math.Abs(got.Value.Y - want.Y), 0, 1);
        Assert.InRange(Math.Abs(got.Value.Width - want.Width), 0, 1);
        Assert.InRange(Math.Abs(got.Value.Height - want.Height), 0, 1);
        Assert.False(got.Value.Contains(new PixelPoint(2, 2)));   // the pad's corner falls through
    }
}
