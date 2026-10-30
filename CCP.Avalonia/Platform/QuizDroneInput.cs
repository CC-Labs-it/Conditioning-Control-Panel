using System;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF Windows/QuizWindow.xaml.cs:41-54: rewind the decoded source at EOF,
// filling the same output buffer. No overlapping copies or output restarts.
internal sealed class QuizDroneInput(string path) : MediaInput
{
    private readonly NLayer.MpegFile _source = new(path);
    private float[] _samples = [];
    internal int SampleRate => _source.SampleRate;
    internal int Channels => _source.Channels;
    public override bool Open(out ulong size)
    {
        size = ulong.MaxValue;
        _source.Position = 0;
        return true;
    }
    public override int Read(IntPtr buffer, uint length)
    {
        try
        {
            var count = checked((int)(length / sizeof(float)));
            if (_samples.Length < count) _samples = new float[count];
            int total = 0;
            bool rewound = false;
            while (total < count)
            {
                var read = _source.ReadSamples(_samples, total, count - total);
                if (read == 0)
                {
                    if (rewound) break; // empty source, as WPF
                    _source.Position = 0;
                    rewound = true;
                }
                else { total += read; rewound = false; }
            }
            Marshal.Copy(_samples, 0, buffer, total);
            return total * sizeof(float);
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Quiz drone decode failed"); return -1; }
    }
    public override bool Seek(ulong offset) => false;
    public override void Close() { }
    protected override void Dispose(bool disposing) { if (disposing) _source.Dispose(); base.Dispose(disposing); }
}
