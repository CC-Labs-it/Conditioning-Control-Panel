using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Services.Speech;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Windows microphone for the Core <see cref="SpeechEngine"/>, the twin of WPF SpeechService's NAudio
    /// <c>WaveInEvent</c> (ConditioningControlPanel/Services/Speech/SpeechService.cs:36-102: 16 kHz, 16-bit mono,
    /// 50 ms buffers, NAudio's 3 of them) over the same winmm waveIn API, with no package. Device list =
    /// "System default" then every waveIn device by its product name; the saved NAME wins over the saved
    /// index, else device 0 (WPF ResolveDeviceNumber, #441b, through Core's ResolveInputDevice). Audio only
    /// lives in memory. Taken from parity/trunk 1fe230a1d, resolve rule put back to WPF's.
    /// The shared SpeechEngine retains its high-pass/noise gate. The separate WPF sherpa wake
    /// spotter is not ported; Vosk's closed grammar carries the wake word on this head.
    /// </summary>
    internal sealed class WinMmMicSource : IMicSource
    {
        private const int SampleRate = 16000;
        internal const int BufferBytes = SampleRate * 2 / 20;   // 50 ms of s16 mono = 1600 bytes
        internal const int BufferCount = 3;
        private const uint CallbackEvent = 0x00050000;
        private const uint WhdrDone = 0x00000001;

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        internal struct WaveFormatEx
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WaveHdr
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WaveInCaps
        {
            public ushort wMid, wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels, wReserved1;
        }

        [DllImport("winmm.dll")] private static extern uint waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetDevCapsW")]
        private static extern int waveInGetDevCaps(UIntPtr id, ref WaveInCaps caps, uint size);
        [DllImport("winmm.dll")] private static extern int waveInOpen(out IntPtr hwi, uint id, ref WaveFormatEx fmt, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern int waveInPrepareHeader(IntPtr hwi, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern int waveInUnprepareHeader(IntPtr hwi, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern int waveInAddBuffer(IntPtr hwi, IntPtr hdr, uint size);
        [DllImport("winmm.dll")] private static extern int waveInStart(IntPtr hwi);
        [DllImport("winmm.dll")] private static extern int waveInReset(IntPtr hwi);
        [DllImport("winmm.dll")] private static extern int waveInClose(IntPtr hwi);

        private static readonly uint HdrSize = (uint)Marshal.SizeOf<WaveHdr>();

        /// <summary>waveIn devices the OS reports (0 = no microphone, or not Windows).</summary>
        internal static int DeviceCount
        {
            get
            {
                if (!OperatingSystem.IsWindows()) return 0;
                try { return (int)waveInGetNumDevs(); } catch { return 0; }
            }
        }

        private static string NameOf(int i)
        {
            try
            {
                var caps = new WaveInCaps();
                return waveInGetDevCaps((UIntPtr)(uint)i, ref caps, (uint)Marshal.SizeOf<WaveInCaps>()) == 0 ? caps.szPname ?? "" : "";
            }
            catch { return ""; }
        }

        public bool HasDevice => DeviceCount > 0;

        /// <summary>WPF SpeechService.EnumerateInputDevices: "System default" (-1), then each device.</summary>
        public IReadOnlyList<SpeechInputDevice> ListDevices()
        {
            var list = new List<SpeechInputDevice> { new(-1, "System default") };
            var n = DeviceCount;
            for (var i = 0; i < n; i++)
            {
                var name = NameOf(i);
                list.Add(new SpeechInputDevice(i, string.IsNullOrWhiteSpace(name) ? $"Device {i}" : name));
            }
            return list;
        }

        /// <summary>WPF ResolveDeviceNumber: saved name, then saved index, else device 0 ("WaveIn 0 == Windows default").</summary>
        internal static uint ResolveDevice(IReadOnlyList<SpeechInputDevice> devices, int savedIndex, string? savedName)
            => (uint)(SpeechEngine.ResolveInputDevice(devices, savedIndex, savedName)?.Index ?? 0);

        public IDisposable Start(Action<byte[], int> onPcm)
        {
            var s = CoreSettings.Current;
            return new Session(ResolveDevice(ListDevices(), s.SpeechInputDeviceIndex, s.SpeechInputDeviceName), onPcm);
        }

        internal static WaveFormatEx CaptureFormat => new()
        {
            wFormatTag = 1, nChannels = 1, nSamplesPerSec = SampleRate, nAvgBytesPerSec = SampleRate * 2,
            nBlockAlign = 2, wBitsPerSample = 16, cbSize = 0,
        };

        // Same checked WinMM calls used by WPF's NAudio WaveInEvent (SpeechService.cs:85-106).
        internal class CaptureApi
        {
            public virtual int Open(out IntPtr h, uint id, ref WaveFormatEx fmt, AutoResetEvent ready) => waveInOpen(out h, id, ref fmt, ready.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent);
            public virtual int Prepare(IntPtr h, IntPtr p) => waveInPrepareHeader(h, p, HdrSize);
            public virtual int Add(IntPtr h, IntPtr p) => waveInAddBuffer(h, p, HdrSize);
            public virtual int Unprepare(IntPtr h, IntPtr p) => waveInUnprepareHeader(h, p, HdrSize);
            public virtual int Start(IntPtr h) => waveInStart(h);
            public virtual int Reset(IntPtr h) => waveInReset(h);
            public virtual int Close(IntPtr h) => waveInClose(h);
        }

        internal sealed class Session : IDisposable
        {
            private readonly CaptureApi _api;
            private readonly IntPtr _hwi;
            private readonly IntPtr[] _hdrs = new IntPtr[BufferCount];
            private readonly bool[] _prepared = new bool[BufferCount];
            private readonly AutoResetEvent _ready = new(false);
            private readonly Thread? _reader;
            private volatile bool _stopping;
            private int _released;
            internal bool IsStopping => _stopping;

            internal Session(uint device, Action<byte[], int> onPcm, CaptureApi? api = null)
            {
                _api = api ?? new CaptureApi();
                var fmt = CaptureFormat;
                try
                {
                    Check(_api.Open(out _hwi, device, ref fmt, _ready), "open");
                    for (var i = 0; i < BufferCount; i++)
                    {
                        _hdrs[i] = Marshal.AllocHGlobal((int)HdrSize);
                        Marshal.StructureToPtr(default(WaveHdr), _hdrs[i], false);
                        var hdr = new WaveHdr { lpData = Marshal.AllocHGlobal(BufferBytes), dwBufferLength = BufferBytes };
                        Marshal.StructureToPtr(hdr, _hdrs[i], false);
                        Check(_api.Prepare(_hwi, _hdrs[i]), "prepare");
                        _prepared[i] = true;
                        Check(_api.Add(_hwi, _hdrs[i]), "add");
                    }
                    Check(_api.Start(_hwi), "start");
                    _reader = new Thread(() => Pump(onPcm)) { IsBackground = true, Name = "WinMmMic" };
                    _reader.Start();
                }
                catch { Release(); throw; }
            }

            private static void Check(int rc, string what)
            {
                if (rc != 0) throw new InvalidOperationException($"waveIn {what} failed ({rc})");
            }

            private void Pump(Action<byte[], int> onPcm)
            {
                try
                {
                    while (!_stopping)
                    {
                        _ready.WaitOne(200);
                        foreach (var p in _hdrs)
                        {
                            if (_stopping) return;
                            var hdr = Marshal.PtrToStructure<WaveHdr>(p);
                            if ((hdr.dwFlags & WhdrDone) == 0) continue;
                            var n = (int)hdr.dwBytesRecorded;
                            if (n > 0)
                            {
                                var buf = new byte[n];
                                Marshal.Copy(hdr.lpData, buf, 0, n);
                                try { onPcm(buf, n); }
                                catch (Exception ex) { Log.Debug(ex, "WinMmMicSource: consumer threw"); }
                            }
                            if (!_stopping) Check(_api.Add(_hwi, p), "requeue");
                        }
                    }
                }
                catch (Exception ex) { Log.Warning(ex, "WinMmMicSource: capture stopped"); }
                finally { _stopping = true; Release(); }
            }

            public void Dispose()
            {
                _stopping = true;
                try { _ready.Set(); } catch (ObjectDisposedException) { }
                // The worker returns native buffers only after the active callback has finished.
                // A callback may itself dispose: its worker's finally then performs the cleanup.
                if (_reader != null && Thread.CurrentThread != _reader) _reader.Join();
            }

            private void Release()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0) return;
                try { if (_hwi != IntPtr.Zero) _api.Reset(_hwi); } catch { }
                for (var i = 0; i < BufferCount; i++)
                {
                    var p = _hdrs[i];
                    if (p == IntPtr.Zero) continue;
                    var hdr = Marshal.PtrToStructure<WaveHdr>(p);
                    try { if (_prepared[i]) _api.Unprepare(_hwi, p); } catch { }
                    if (hdr.lpData != IntPtr.Zero) Marshal.FreeHGlobal(hdr.lpData);
                    Marshal.FreeHGlobal(p);
                }
                try { if (_hwi != IntPtr.Zero) _api.Close(_hwi); } catch { }
                _ready.Dispose();
            }
        }
    }
}
