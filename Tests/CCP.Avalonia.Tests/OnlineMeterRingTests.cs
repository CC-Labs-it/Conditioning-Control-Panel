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
        OnlineMeterTestPlatform.EnsureApplication();
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
                    // Compare colour channels in BGRA order on both rendering backends.
                    if (bitmap.Format == global::Avalonia.Platform.PixelFormats.Rgba8888)
                        for (int i = 0; i < pixels.Length; i += 4)
                            (pixels[i], pixels[i + 2]) = (pixels[i + 2], pixels[i]);
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
            // WPF's spent track is #A7A7C1. Alpha on the black window proves nothing.
            Assert.Contains(Enumerable.Range(0, spent.Length / 4), pixel =>
                Math.Abs(spent[pixel * 4] - 0xC1) <= 3 &&
                Math.Abs(spent[pixel * 4 + 1] - 0xA7) <= 3 &&
                Math.Abs(spent[pixel * 4 + 2] - 0xA7) <= 3);
            Assert.False(spent.SequenceEqual(empty));
        }
        finally { window.Close(); }
    });
}
