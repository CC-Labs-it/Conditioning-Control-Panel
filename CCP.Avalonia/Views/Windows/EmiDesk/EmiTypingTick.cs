using System;
using System.IO;
using ConditioningControlPanel.Services.EmiDesk;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;

// WPF EmiVox.cs:398-453, 500-513, 551-572. A single 30 ms triangle tick.
internal sealed class EmiTypingTick : IDisposable
{
    private Action? _stop;
    private bool _disposed;
    private static readonly object Gate = new();

    internal static byte[] Render()
    {
        const int rate = 44100;
        const double duration = .03, attack = duration * .35;
        int count = (int)((30.0 + 20.0) / 1000 * rate) + 8;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8); writer.Write(36 + count * 2); writer.Write("WAVE"u8);
        writer.Write("fmt "u8); writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(count * 2);
        double phase = 0, peak = Math.Sqrt(.22) * .16 * .85;
        for (int i = 0; i < count; i++)
        {
            double sample = 0;
            if (i < (int)(duration * rate))
            {
                double t = i / (double)rate;
                phase += 336 * Math.Pow(322.0 / 336, t / duration) / rate;
                double envelope = t < attack ? peak * t / attack
                    : peak * Math.Pow(.0001 / peak, (t - attack) / (duration - attack));
                sample = (4 * Math.Abs(phase - Math.Floor(phase) - .5) - 1) * envelope;
            }
            writer.Write((short)Math.Round(Math.Clamp(sample, -1, 1) * short.MaxValue));
        }
        return stream.ToArray();
    }

    internal bool Tick()
    {
        if (_disposed) return false;
        try
        {
            int master = CoreSettings.Current?.MasterVolume ?? 0;
            if (master <= 0 || CoreAudio.IsOutputSuppressed || EmiLineEngine.Instance.HoldActive
                || PanicAftermath.TriggersSilenced()) return false;
            string path = Path.Combine(CorePaths.UserData, "emi", "vox", "tick-v1.wav");
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (!File.Exists(path)) File.WriteAllBytes(path, Render());
            }
            Stop();
            _stop = CoreAudio.PlayStoppable(path, Math.Clamp(master / 100f, 0, 1), "emi-vox-tick");
            return true;
        }
        catch (Exception ex) { Serilog.Log.Debug(ex, "[EmiDesk] typing tick failed"); return false; }
    }

    internal void Stop() { var stop = _stop; _stop = null; try { stop?.Invoke(); } catch { } }
    public void Dispose() { _disposed = true; Stop(); }
}
