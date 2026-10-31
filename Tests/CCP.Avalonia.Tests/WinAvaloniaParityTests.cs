using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF SpeechService.cs:36-102, VideoService.cs:8709, WindowChromeHelper.cs:53-73.
// Hardware capture and HWND effects require Windows; these tests also run the capture worker with fake WinMM.
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

    private sealed class Capture : WinMmMicSource.CaptureApi
    {
        internal string? Fail;
        internal bool FailRequeue, UnpreparedBeforeReset;
        internal int Adds, Prepared, Unprepared, Resets, Closes;
        internal readonly ManualResetEventSlim Closed = new();
        private AutoResetEvent _ready = null!;
        private IntPtr _first;
        public override int Open(out IntPtr h, uint id, ref WinMmMicSource.WaveFormatEx fmt, AutoResetEvent ready)
        { _ready = ready; h = Fail == "open" ? IntPtr.Zero : new IntPtr(1); return Fail == "open" ? 1 : 0; }
        public override int Prepare(IntPtr h, IntPtr p)
        { if (Fail == "prepare" && Prepared == 1) return 1; if (Prepared++ == 0) _first = p; return 0; }
        public override int Add(IntPtr h, IntPtr p)
        {
            if (++Adds == 2 && Fail == "add" || Adds > 3 && FailRequeue) return 1;
            var hdr = Marshal.PtrToStructure<WinMmMicSource.WaveHdr>(p);
            hdr.dwFlags = 0; hdr.dwBytesRecorded = 0;
            Marshal.StructureToPtr(hdr, p, false);
            return 0;
        }
        public override int Start(IntPtr h) => Fail == "start" ? 1 : 0;
        public override int Reset(IntPtr h) { Resets++; return 0; }
        public override int Unprepare(IntPtr h, IntPtr p) { UnpreparedBeforeReset |= Resets == 0; Unprepared++; return 0; }
        public override int Close(IntPtr h) { Closes++; Closed.Set(); return 0; }
        internal void Complete()
        {
            var hdr = Marshal.PtrToStructure<WinMmMicSource.WaveHdr>(_first);
            Marshal.Copy(new byte[] { 1, 2, 3, 4 }, 0, hdr.lpData, 4);
            hdr.dwFlags = 1; hdr.dwBytesRecorded = 4;
            Marshal.StructureToPtr(hdr, _first, false);
            _ready.Set();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureDeliversPcmAndARequeueFailureClosesInsteadOfReplaying(bool fail)
    {
        var api = new Capture { FailRequeue = fail };
        using var delivered = new ManualResetEventSlim();
        byte[]? received = null;
        var calls = 0;
        using var session = new WinMmMicSource.Session(0, (pcm, n) =>
        { received = pcm[..n]; Interlocked.Increment(ref calls); delivered.Set(); }, api);
        api.Complete();
        Assert.True(delivered.Wait(TimeSpan.FromSeconds(5)));
        if (fail) Assert.True(api.Closed.Wait(TimeSpan.FromSeconds(5)));
        else Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref api.Adds) == 4, 5000));
        session.Dispose();
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, received);
        Assert.Equal(1, calls);
        Assert.Equal(4, api.Adds);
        Assert.Equal(3, api.Unprepared);
        Assert.Equal(1, api.Closes);
        Assert.False(api.UnpreparedBeforeReset);
    }

    [Theory]
    [InlineData("open", 0)]
    [InlineData("prepare", 1)]
    [InlineData("add", 2)]
    [InlineData("start", 3)]
    public void PartialInitializationReturnsEveryPreparedBufferAndCloses(string failure, int prepared)
    {
        var api = new Capture { Fail = failure };
        Assert.Throws<InvalidOperationException>(() => new WinMmMicSource.Session(0, (_, _) => { }, api));
        Assert.Equal(prepared, api.Unprepared);
        Assert.Equal(prepared == 0 ? 0 : 1, api.Resets);
        Assert.Equal(prepared == 0 ? 0 : 1, api.Closes);
        Assert.False(api.UnpreparedBeforeReset);
    }

    [Fact]
    public async Task DisposeWaitsForTheActiveCallbackBeforeReturningBuffers()
    {
        var api = new Capture();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var session = new WinMmMicSource.Session(0, (_, _) => { entered.Set(); release.Wait(); }, api);
        Task? stop = null;
        try
        {
            api.Complete();
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            stop = Task.Run(session.Dispose);
            Assert.True(SpinWait.SpinUntil(() => session.IsStopping, 5000));
            Assert.False(stop.Wait(TimeSpan.FromMilliseconds(200)));
            Assert.Equal(0, api.Closes);
            Assert.Equal(0, api.Unprepared);
        }
        finally { release.Set(); if (stop != null) await stop.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.Equal(3, api.Adds);
        Assert.Equal(3, api.Unprepared);
        Assert.Equal(1, api.Closes);
        session.Dispose();
        Assert.Equal(1, api.Closes);
    }


    [Fact]
    public void ACallbackMayDisposeItsOwnSessionWithoutReturningItsBuffersEarly()
    {
        var api = new Capture();
        var closedDuringCallback = -1;
        WinMmMicSource.Session? session = null;
        using (session = new WinMmMicSource.Session(0, (_, _) =>
        { session!.Dispose(); closedDuringCallback = api.Closes; }, api))
        {
            api.Complete();
            Assert.True(api.Closed.Wait(TimeSpan.FromSeconds(5)));
        }
        Assert.Equal(0, closedDuringCallback);
        Assert.Equal(3, api.Unprepared);
        Assert.Equal(1, api.Closes);
    }

}
