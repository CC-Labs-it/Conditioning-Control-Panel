using System;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using NAudio.Wave;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF Windows/QuizWindow.xaml.cs:41-54: rewind the decoded source at EOF,
// filling the same output buffer. No overlapping copies or output restarts.
internal sealed class QuizDroneInput(string path) : MediaInput
{
    // WPF QuizWindow.xaml.cs:1205: AudioFileReader also accepts WAV mod overrides.
    private readonly NLayer.MpegFile? _source = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? null : new(path);
    private readonly WaveFileReader? _wave = path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? new(path) : null;
    private ISampleProvider? _waveSamples;
    private float[] _samples = [];
    internal int SampleRate => _source?.SampleRate ?? _wave!.WaveFormat.SampleRate;
    internal int Channels => _source?.Channels ?? _wave!.WaveFormat.Channels;
    public override bool Open(out ulong size)
    {
        size = ulong.MaxValue;
        Rewind();
        return true;
    }
    public override int Read(IntPtr buffer, uint length)
    {
        try
        {
            var count = checked((int)(length / sizeof(float)));
            count -= count % Channels; // WAV reads must contain whole interleaved frames.
            if (_samples.Length < count) _samples = new float[count];
            int total = 0;
            bool rewound = false;
            while (total < count)
            {
                var read = ReadSamples(total, count - total);
                if (read == 0)
                {
                    if (rewound) break; // empty source, as WPF
                    Rewind();
                    rewound = true;
                }
                else { total += read; rewound = false; }
            }
            Marshal.Copy(_samples, 0, buffer, total);
            return total * sizeof(float);
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Quiz drone decode failed"); return -1; }
    }
    private int ReadSamples(int offset, int count) => _source?.ReadSamples(_samples, offset, count)
        ?? (_waveSamples ??= _wave!.ToSampleProvider()).Read(_samples, offset, count);
    private void Rewind()
    {
        if (_source != null) _source.Position = 0;
        else { _wave!.Position = 0; _waveSamples = null; }
    }
    public override bool Seek(ulong offset) => false;
    public override void Close() { }
    protected override void Dispose(bool disposing) { if (disposing) { _source?.Dispose(); _wave?.Dispose(); } base.Dispose(disposing); }
}
