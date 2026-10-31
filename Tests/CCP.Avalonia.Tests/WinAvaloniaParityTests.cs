using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF SpeechService.cs:36-102, VideoService.cs:8709, WindowChromeHelper.cs:53-73.
// Native device capture and HWND effects require Windows; these tests cover the portable seams.
public sealed class WinAvaloniaParityTests
{
    [Fact]
    public void CaptureUsesWpfPcmFormatAndFiftyMillisecondBuffers()
    {
        var fmt = WinMmMicSource.CaptureFormat;
        Assert.Equal(1, fmt.wFormatTag);
        Assert.Equal(1, fmt.nChannels);
        Assert.Equal(16000u, fmt.nSamplesPerSec);
        Assert.Equal(32000u, fmt.nAvgBytesPerSec);
        Assert.Equal(2, fmt.nBlockAlign);
        Assert.Equal(16, fmt.wBitsPerSample);
        Assert.Equal(0, fmt.cbSize);
        Assert.Equal(1600, WinMmMicSource.BufferBytes);
        Assert.Equal(3, WinMmMicSource.BufferCount);
    }

    [Fact]
    public void WindowsGetsAMicrophoneSourceAndUnsupportedPlatformsGetNone()
    {
        Assert.IsType<WinMmMicSource>(PulseMicSource.CreateMic(false, true));
        Assert.IsType<PulseMicSource>(PulseMicSource.CreateMic(true, false));
        Assert.Null(PulseMicSource.CreateMic(false, false));
    }

    [Fact]
    public void MicResolvesSavedNameThenIndexElseDeviceZeroLikeWpf()
    {
        SpeechInputDevice[] devices = [new(-1, "System default"), new(0, "Headset"), new(1, "Webcam mic")];
        Assert.Equal(1u, WinMmMicSource.ResolveDevice(devices, 0, "webcam MIC"));
        Assert.Equal(1u, WinMmMicSource.ResolveDevice(devices, 1, null));
        Assert.Equal(0u, WinMmMicSource.ResolveDevice(devices, 5, "unplugged"));
        Assert.Equal(0u, WinMmMicSource.ResolveDevice(devices, -1, "System default"));
        Assert.Equal(0u, WinMmMicSource.ResolveDevice([], -1, null));
    }

    [Fact]
    public void OnlyMouseActivateIsHandledAndItDoesNotActivate()
    {
        var handled = false;
        Assert.Equal(new IntPtr(3), Win32Window.MouseActivate(0x21, ref handled));
        Assert.True(handled);
        handled = false;
        Assert.Equal(IntPtr.Zero, Win32Window.MouseActivate(0x201, ref handled));
        Assert.False(handled);
    }

    [Fact]
    public void TitleBarPaintsDarkModeCaptionBorderAndText()
    {
        var values = new System.Collections.Generic.List<(int, int)>();
        Win32Window.PaintTitleBar((255, 105, 180), (key, value) => values.Add((key, value)));
        Assert.Equal(new[] { (20, 1), (35, 0xB469FF), (34, 0xB469FF), (36, 0x260816) }, values);
    }

    [Theory]
    [InlineData(255, 105, 180, 0xB469FF, 0x260816)]
    [InlineData(32, 32, 64, 0x402020, 0xFFFFFF)]
    [InlineData(150, 150, 150, 0x969696, 0xFFFFFF)]
    [InlineData(151, 151, 151, 0x979797, 0x260816)]
    public void TitleBarUsesColorrefAndWpfContrastThreshold(byte r, byte g, byte b, int caption, int text)
        => Assert.Equal((caption, text), Win32Window.TitleBarColors((r, g, b)));
}
