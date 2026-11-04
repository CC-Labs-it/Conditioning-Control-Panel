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

        public static (string[] Clips, int LegacyCount) Scan(string primary, string legacy)
        {
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Enumerate(EnsureFolder(primary))) byName[Path.GetFileName(file)] = file;
            var old = Enumerate(legacy);
            foreach (var file in old) byName.TryAdd(Path.GetFileName(file), file);
            return (byName.Values.ToArray(), old.Length);
        }
    }
}
