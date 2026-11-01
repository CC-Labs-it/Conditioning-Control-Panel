using System;
using System.Collections.Generic;
using System.Text.Json;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF AwarenessProbes.cs:317-395: cache active capture sessions for five seconds.
// Pulse/PipeWire source-output metadata only, never captured audio or client names.
internal sealed class PulseAwarenessMicrophoneProbe : IMicrophoneProbe
{
    private readonly object _gate = new();
    private readonly Func<string, string> _pactl;
    private DateTime _checkedAt = DateTime.MinValue;
    private bool _inUse;

    internal PulseAwarenessMicrophoneProbe(Func<string, string>? pactl = null) => _pactl = pactl ?? ReadPactl;

    private static string ReadPactl(string args)
    {
        if (!OperatingSystem.IsLinux() || SandboxNet.Active) return "[]";
        return LibVlcAudio.Pactl(args);
    }

    public bool IsInUse(DateTime at)
    {
        lock (_gate)
        {
            if ((at - _checkedAt).TotalSeconds < 5) return _inUse;
            _checkedAt = at;
            try { _inUse = HasActiveCapture(_pactl("--format=json list sources"), _pactl("--format=json list source-outputs")); }
            catch { _inUse = false; }
            return _inUse;
        }
    }

    internal static bool HasActiveCapture(string sources, string outputs)
    {
        using var sourceDoc = JsonDocument.Parse(sources);
        using var outputDoc = JsonDocument.Parse(outputs);
        var microphones = new HashSet<uint>();
        foreach (var source in sourceDoc.RootElement.EnumerateArray())
        {
            if (!source.TryGetProperty("index", out var index) || !index.TryGetUInt32(out uint id) ||
                !source.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String) continue;
            if (!(name.GetString() ?? "").EndsWith(".monitor", StringComparison.Ordinal)) microphones.Add(id);
        }
        foreach (var output in outputDoc.RootElement.EnumerateArray())
        {
            if (!output.TryGetProperty("source", out var source) || !source.TryGetUInt32(out uint id) || !microphones.Contains(id)) continue;
            if (output.TryGetProperty("corked", out var corked) &&
                (corked.ValueKind == JsonValueKind.False ||
                 (corked.ValueKind == JsonValueKind.String && string.Equals(corked.GetString(), "no", StringComparison.OrdinalIgnoreCase)))) return true;
        }
        return false;
    }
}
