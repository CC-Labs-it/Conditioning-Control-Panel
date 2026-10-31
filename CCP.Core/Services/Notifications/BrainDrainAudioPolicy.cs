using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services;

// WPF BrainDrainService: primary wins by filename, legacy is never modified.
public static class BrainDrainAudioPolicy
{
    public static string AudioFolder => Path.Combine(CorePaths.EffectiveAssets, "braindrain");
    public static string LegacyFolder => Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "braindrain");
    public static string[] Enumerate(string folder)
    {
        try { return Directory.GetFiles(folder).Where(f => new[] { ".mp3", ".wav", ".ogg" }
            .Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToArray(); }
        catch { return Array.Empty<string>(); }
    }
    public static string[] Discover(string primary, string legacy)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Enumerate(primary)) files[Path.GetFileName(file)] = file;
        foreach (var file in Enumerate(legacy)) files.TryAdd(Path.GetFileName(file), file);
        return files.Values.ToArray();
    }
    public static TimeSpan Interval(bool highRefresh) => TimeSpan.FromMilliseconds(highRefresh ? 500 : 5000);
    public static double Probability(int intensity, TimeSpan interval) => Math.Clamp(intensity, 1, 100) / 100.0 * interval.TotalSeconds / 60;
    public static double Volume(int master, int own) => Math.Clamp(master, 0, 100) / 100.0 * Math.Clamp(own, 0, 100) / 100;
}
