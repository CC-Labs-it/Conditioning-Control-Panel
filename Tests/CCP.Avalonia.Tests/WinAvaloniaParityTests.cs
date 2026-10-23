using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>win-avalonia-parity: the Windows halves of ducking (WPF AudioService WASAPI sweep), the mic
/// (WPF ResolveDeviceNumber), PassiveToastWindow / MA_NOACTIVATE and WindowChromeHelper, through their
/// pure seams. The P/Invoke/COM ends only run on Windows and are compiled, not run, here.</summary>
public sealed class WinAvaloniaParityTests
{
    private const int OwnPid = 42;

    private sealed class FakeSessions
    {
        public readonly Dictionary<int, float> Volume = new() { [OwnPid] = 1f, [0] = 1f, [7] = 0.5f, [9] = 1f };
        public bool RestoreThrows;

        public void ForEach(WasapiDuck.SessionVisitor visit)
        {
            foreach (var pid in new List<int>(Volume.Keys))
                visit(pid, () => Volume[pid], v =>
                {
                    if (RestoreThrows && v > Volume[pid]) throw new InvalidOperationException("session busy");
                    Volume[pid] = v;
                });
        }
    }

    [Fact]
    public void DuckScalesOtherAppsSessionsAndExitRestoresThem()
    {
        var fake = new FakeSessions();
        var audio = new LibVlcAudio(new WasapiDuck(fake.ForEach, OwnPid));
        audio.Duck(80);
        audio.Duck(80);
        audio.Drain();
        Assert.Equal(0.1f, fake.Volume[7], 3);
        Assert.Equal(0.2f, fake.Volume[9], 3);
        Assert.Equal(1f, fake.Volume[OwnPid]);   // our own process is never ducked
        Assert.Equal(1f, fake.Volume[0]);        // nor the system-sounds session

        audio.Unduck(-1);
        Assert.True(audio.IsDucked);             // one ref still held
        audio.Shutdown();                         // force-unduck + drain
        Assert.False(audio.IsDucked);
        Assert.Equal(0.5f, fake.Volume[7], 3);
        Assert.Equal(1f, fake.Volume[9], 3);
    }

    [Fact]
    public void AFailedRestoreKeepsTheTrueOriginalSoTheNextDuckCannotRatchet()
    {
        var fake = new FakeSessions();
        var duck = new WasapiDuck(fake.ForEach, OwnPid);
        duck.Sweep(0.2, () => true);
        fake.RestoreThrows = true;
        duck.Restore();                           // every restore throws: originals survive
        Assert.Equal(0.1f, fake.Volume[7], 3);

        fake.RestoreThrows = false;
        duck.Sweep(0.2, () => true);              // must not recapture 0.1 as the "original"
        Assert.Equal(0.1f, fake.Volume[7], 3);
        duck.Restore();
        Assert.Equal(0.5f, fake.Volume[7], 3);
        Assert.Equal(1f, fake.Volume[9], 3);
    }

    [Fact]
    public void MicResolvesSavedNameThenIndexElseDeviceZeroLikeWpf()
    {
        var devices = new List<SpeechInputDevice> { new(-1, "System default"), new(0, "Headset"), new(1, "Webcam mic") };
        Assert.Equal(1u, WinMmMicSource.ResolveDevice(devices, 0, "webcam MIC")); // the name wins over the index
        Assert.Equal(1u, WinMmMicSource.ResolveDevice(devices, 1, null));
        Assert.Equal(0u, WinMmMicSource.ResolveDevice(devices, 5, "unplugged"));  // gone: WaveIn 0, as WPF
        Assert.Equal(0u, WinMmMicSource.ResolveDevice(devices, -1, null));        // "System default"
    }

    [Fact]
    public void PassiveToastAndNoClickActivateAnswerLikeWpf()
    {
        Assert.Equal(0x08000080u | 0x100u, Win32Window.PassiveStyle(0x100));     // + NOACTIVATE | TOOLWINDOW

        var handled = false;
        Assert.Equal(new IntPtr(3), Win32Window.MouseActivate(0x21, ref handled)); // WM_MOUSEACTIVATE -> MA_NOACTIVATE
        Assert.True(handled);
        handled = false;
        Assert.Equal(IntPtr.Zero, Win32Window.MouseActivate(0x201, ref handled));  // anything else passes through
        Assert.False(handled);
    }

    [Fact]
    public void TitleBarTakesTheAccentWithAutoContrastText()
    {
        // #FF69B4 (WPF's fallback accent) is bright: dark ink. COLORREF is 0x00BBGGRR.
        Assert.Equal((0xB469FF, 0x260816), Win32Window.TitleBarColors((0xFF, 0x69, 0xB4)));
        Assert.Equal((0x402020, 0xFFFFFF), Win32Window.TitleBarColors((0x20, 0x20, 0x40)));
    }
}
