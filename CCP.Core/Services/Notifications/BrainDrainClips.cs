using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ConditioningControlPanel.Services
{
    // WPF BrainDrainService.cs:59-110,149-183: primary assets folder plus read-only legacy folder.
    public static class BrainDrainClips
    {
        public static string AudioFolderPath
        {
            get { try { return Path.Combine(CorePaths.EffectiveAssets, "braindrain"); } catch { return LegacyFolderPath; } }
        }
        public static string LegacyFolderPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", "braindrain");

        public static string EnsureFolder(string path)
        {
            try { Directory.CreateDirectory(path); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "BrainDrain: could not create the audio folder"); }
            return path;
        }

        private static readonly string[] Extensions = { ".mp3", ".wav", ".ogg" };

        private static string[] Enumerate(string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return Array.Empty<string>();
                return Directory.GetFiles(folder, "*.*").Where(f =>
                    Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToArray();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "BrainDrain: could not read clip folder {Path}", folder);
                return Array.Empty<string>();
            }
        }

        // WPF BrainDrainService.cs:95-99: counting legacy files never creates the primary folder.
        public static int Count(string folder) => Enumerate(folder).Length;

        public static (string[] Clips, int LegacyCount) Scan(string primary, string legacy) => Scan(primary, legacy, out _);

        // WPF BrainDrainService.cs:168-183: log only legacy files that actually joined the pool.
        public static (string[] Clips, int LegacyCount) Scan(string primary, string legacy, out int legacyAdded, bool ensurePrimary = true)
        {
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Enumerate(ensurePrimary ? EnsureFolder(primary) : primary)) byName[Path.GetFileName(file)] = file;
            var old = Enumerate(legacy);
            legacyAdded = 0;
            foreach (var file in old) if (byName.TryAdd(Path.GetFileName(file), file)) legacyAdded++;
            return (byName.Values.ToArray(), old.Length);
        }
    }
}
