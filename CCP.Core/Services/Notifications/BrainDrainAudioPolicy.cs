using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services;

// WPF Services/LockCard/BrainDrainService.cs:59-114,162-181.
// Primary wins by filename; legacy is never modified.
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
}
