using System;
using System.Diagnostics;
using System.Text.Json;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF AwarenessProbes.cs:328. Unknown capture state is treated as busy.
internal sealed class AwarenessMicrophoneProbe : IMicrophoneProbe
{
    private readonly Func<string?> _read;
    private readonly object _gate = new();
    private DateTime _checkedAt = DateTime.MinValue;
    private bool _inUse = true;
    private bool _hasSample;

    internal AwarenessMicrophoneProbe() : this(ReadPulse) { }
    internal AwarenessMicrophoneProbe(Func<string?> read) => _read = read;

    public bool IsInUse(DateTime at)
    {
        lock (_gate)
        {
            if (_hasSample && at >= _checkedAt && (at - _checkedAt).TotalSeconds < 5) return _inUse;
            _checkedAt = at;
            _hasSample = true;
            try
            {
                var json = _read();
                if (json == null) return _inUse = true;
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Array) return _inUse = true;
                return _inUse = document.RootElement.GetArrayLength() != 0;
            }
            catch { return _inUse = true; }
        }
    }

    private static string? ReadPulse()
    {
        if (!OperatingSystem.IsLinux()) return null;
        using var process = Process.Start(new ProcessStartInfo("pactl")
        {
            ArgumentList = { "-f", "json", "list", "source-outputs" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (process == null) return null;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(2000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return null;
        }
        return process.ExitCode == 0 ? output.GetAwaiter().GetResult() : null;
    }
}
