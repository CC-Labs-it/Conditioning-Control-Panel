using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Avalonia;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class EmiOptionsX11Tests
{
    private static byte[] Wire(byte type, byte detail, PixelPoint at = default, bool swapped = false)
    {
        var bytes = new byte[32];
        bytes[0] = type; bytes[1] = detail;
        if (BitConverter.IsLittleEndian != swapped)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), checked((short)at.X));
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), checked((short)at.Y));
        }
        else
        {
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(20), checked((short)at.X));
            BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(22), checked((short)at.Y));
        }
        return bytes;
    }

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    // WPF EmiOptionsWindow.xaml.cs:666-692: inside clicks stay open, outside/Escape fold.
    [Fact]
    public Task RecordedEventsReachTheOpenPanelAndStopAfterFold() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        X11OptionsDismiss? listener = null;
        var window = new EmiOptionsWindow
        {
            StartDismissListener = callback => listener = new X11OptionsDismiss(callback, 9),
            ReadPointer = () => throw new InvalidOperationException("Event backend must not poll"),
        };
        try
        {
            window.OpenPanel();
            listener!.Route(Wire(4, 1, window.Position + new PixelPoint(5, 5)));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsOpen);
            listener.Route(Wire(4, 3, new PixelPoint(-5000, -5000)));
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsOpen);
            window.OpenPanel();
            listener!.Route(Wire(2, 9));
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsOpen);
        }
        finally { window.Kill(); }
        return Task.CompletedTask;
    });

    // WPF EmiOptionsWindow.xaml.cs:673-680 uses the hook event's screen point, not the cursor now.
    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 3)]
    [InlineData(true, true, 3)]
    [InlineData(false, true, 1)]
    public Task DelayedDispatchUsesClickCoordinatesAfterPointerCrossesPanel(bool clickedInside, bool swapped, byte button)
        => AvaloniaTestDispatcher.RunAsync(() =>
    {
        Setup();
        X11OptionsDismiss? listener = null;
        PixelPoint current = default;
        PixelPoint? observed = null;
        var window = new EmiOptionsWindow
        {
            StartDismissListener = callback => listener = new X11OptionsDismiss((at, escape) =>
            {
                observed = at;
                callback(at, escape);
            }, 9),
            ReadPointer = () => (current, false, false),
        };
        try
        {
            window.OpenPanel();
            var inside = window.Position + new PixelPoint(5, 5);
            var outside = new PixelPoint(-5000, -5000);
            current = clickedInside ? inside : outside;
            var atClick = current;
            var queuedEvent = Wire(4, button, atClick, swapped);
            // The native event has queued, but its callback has not run. Move across the boundary.
            current = clickedInside ? outside : inside;
            listener!.Route(queuedEvent, swapped);
            Assert.Equal(atClick, observed);
            Assert.NotEqual(current, observed);
            Assert.True(window.IsOpen); // closing is posted to the UI dispatcher
            // Reusing the native event memory after the callback must not change the posted point.
            Wire(4, button, current, swapped).CopyTo(queuedEvent, 0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(clickedInside, window.IsOpen);
        }
        finally { window.Kill(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void WindowsPanelNeverActivatesButStillReceivesClicks()
    {
        const uint existing = 0x100;
        uint style = Win32OptionsDismiss.PassiveStyle(existing);
        Assert.Equal(existing | Win32Overlay.WsExToolWindow | Win32Overlay.WsExNoActivate, style);
        Assert.Equal(0u, style & Win32Overlay.WsExTransparent);
        Assert.Equal(style, Win32OptionsDismiss.PassiveStyle(style));
    }

    [Fact]
    public void BriefLeftAndRightPressesSurviveWithoutPollingHeldState()
    {
        var clicks = new List<PixelPoint?>();
        using var listener = new X11OptionsDismiss((point, escape) =>
        {
            Assert.False(escape);
            clicks.Add(point);
        }, 9);
        // RECORD retains the press even when release happened before callback dispatch.
        listener.Route(Wire(4, 1, new PixelPoint(-42, 72)));
        listener.Route(Wire(5, 1));
        listener.Route(Wire(4, 3, new PixelPoint(15, 20)));
        Assert.Equal(new PixelPoint?[] { new(-42, 72), new(15, 20) }, clicks);
    }

    [Fact]
    public void OnlyEscapeDownOrLeftAndRightDownReachThePanel()
    {
        int calls = 0;
        using var listener = new X11OptionsDismiss((point, escape) =>
        {
            Assert.True(escape);
            Assert.Null(point);
            calls++;
        }, 9);
        listener.Route(Wire(2, 9));
        listener.Route(Wire(3, 9));
        listener.Route(Wire(2, 38));
        listener.Route(Wire(2, 0));
        listener.Route(Wire(4, 2));
        listener.Route(Wire(4, 4));
        listener.Route(Wire(4, 1).AsSpan(0, 20)); // incomplete event cannot supply coordinates
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FoldUnsubscribesIdempotentlyWithoutRemovingAnotherPanel()
    {
        int first = 0, second = 0;
        var a = new X11OptionsDismiss((_, _) => first++, 9);
        using var b = new X11OptionsDismiss((_, _) => second++, 9);
        try
        {
            a.Route(Wire(2, 9)); b.Route(Wire(2, 9));
            a.Dispose();
            a.Dispose();
            a.Route(Wire(2, 9)); b.Route(Wire(2, 9));
            Assert.Equal(1, first);
            Assert.Equal(2, second);
        }
        finally { a.Dispose(); }
    }
}
