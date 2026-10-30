using System;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class OnlineMeterRingTests
{
    [Fact]
    public void Ring_draws_progress_and_a_plain_grey_track_when_spent() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var ring = new OnlineMeterRing();
        Assert.Equal(18, ring.Width);
        Assert.False(ring.IsHitTestVisible);
        var window = new Window { Width = 32, Height = 32, Background = Brushes.Black, Content = ring };
        try
        {
            window.Show();
            byte[] Frame(int percent, OnlineMeterTone tone)
            {
                ring.Percent = percent;
                ring.Tone = tone;
                Dispatcher.UIThread.RunJobs();
                using var bitmap = window.CaptureRenderedFrame()!;
                var pixels = new byte[32 * 32 * 4];
                var memory = Marshal.AllocHGlobal(pixels.Length);
                try
                {
                    bitmap.CopyPixels(new PixelRect(0, 0, 32, 32), memory, pixels.Length, 32 * 4);
                    Marshal.Copy(memory, pixels, 0, pixels.Length);
                    return pixels;
                }
                finally { Marshal.FreeHGlobal(memory); }
            }
            var empty = Frame(0, OnlineMeterTone.Pink);
            var half = Frame(50, OnlineMeterTone.Pink);
            var amber = Frame(80, OnlineMeterTone.Amber);
            var spent = Frame(100, OnlineMeterTone.Spent);
            Assert.False(empty.SequenceEqual(half));
            Assert.False(half.SequenceEqual(amber));
            Assert.False(amber.SequenceEqual(spent));
            Assert.Equal(spent, Frame(0, OnlineMeterTone.Spent));
            Assert.True(spent.Any(b => b > 100));
        }
        finally { window.Close(); }
    });
}
