using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;

namespace ConditioningControlPanel.Services
{
    /// <summary>The Mod Creator form as last saved (ccp-bugs #1667). Images and Audio map slot key to file path;
    /// SideDir names a folder under the store root with a resources tree for the side panels, written on close.</summary>
    internal sealed class ModCreatorDraft
    {
        public DateTime SavedAtUtc { get; set; }
        public ModManifest? Manifest { get; set; }
        public Dictionary<string, string> Images { get; set; } = new();
        public Dictionary<string, string> Audio { get; set; } = new();
        public List<string> VoiceLines { get; set; } = new();
        public string? SideDir { get; set; }
    }

    /// <summary>The Mod Creator's machine-local draft; the root is a seam (app: UserDataPath/mod-creator, tests: a temp
    /// folder). Picked files stay referenced by path; only a file under the temp folder (an extracted .ccpmod, a pack
    /// decrypt) is copied next to the draft, because those vanish when the window closes or the app exits.</summary>
    internal sealed class ModCreatorDraftStore
    {
        private readonly string _volatileRoot;

        public ModCreatorDraftStore(string root, string? volatileRoot = null)
        {
            Root = Path.GetFullPath(root);
            _volatileRoot = Path.GetFullPath(volatileRoot ?? Path.GetTempPath());
        }

        public static ModCreatorDraftStore ForApp() => new(Path.Combine(App.UserDataPath, "mod-creator"));

        public string Root { get; }
        public string DraftPath => Path.Combine(Root, "draft.json");
        private string ExportStampPath => Path.Combine(Root, "last-export.json");
        private string FilesDir => Path.Combine(Root, "files");

        /// <summary>A path that can disappear under the draft: inside the temp folder and not already ours.</summary>
        public bool IsVolatile(string path) =>
            !string.IsNullOrEmpty(path) && IsUnder(Path.GetFullPath(path), _volatileRoot) && !IsUnder(Path.GetFullPath(path), Root);

        public void Save(ModCreatorDraft draft)
        {
            Directory.CreateDirectory(Root);
            draft.Images = draft.Images.ToDictionary(kv => kv.Key, kv => Keep("img", kv.Key, kv.Value));
            draft.Audio = draft.Audio.ToDictionary(kv => kv.Key, kv => Keep("audio", kv.Key, kv.Value));
            draft.VoiceLines = draft.VoiceLines.Select(p => Keep("voice", "", p)).ToList();

            var tmp = DraftPath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(draft, Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
            File.Move(tmp, DraftPath, overwrite: true);
        }

        public ModCreatorDraft? Load()
        {
            try
            {
                var draft = File.Exists(DraftPath) ? JsonConvert.DeserializeObject<ModCreatorDraft>(File.ReadAllText(DraftPath)) : null;
                return draft?.Manifest == null ? null : draft;
            }
            catch (Exception ex) { App.Logger?.Warning(ex, "Mod Creator: draft unreadable"); return null; }
        }

        public DateTime? LastExportUtc() =>
            File.Exists(ExportStampPath) && DateTime.TryParse(File.ReadAllText(ExportStampPath).Trim(),
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToUniversalTime() : null;

        /// <summary>The draft worth offering on open: one exists and it was saved after the last successful export.</summary>
        public ModCreatorDraft? Offer() =>
            Load() is { } draft && (LastExportUtc() is not { } exported || draft.SavedAtUtc > exported) ? draft : null;

        /// <summary>After a successful export: stamp the time and drop the draft. Copied files stay until the next
        /// open finds no draft (<see cref="PruneIfNoDraft"/>), because the open form may still show them.</summary>
        public void MarkExported(DateTime utc)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(ExportStampPath, utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
            TryDelete(DraftPath);
        }

        public void Discard() { TryDelete(DraftPath); PruneIfNoDraft(); }

        /// <summary>On open with no draft on disk nothing can reference the copied files: clear them.</summary>
        public void PruneIfNoDraft()
        {
            if (File.Exists(DraftPath)) return;
            TryDeleteDir(FilesDir);
            DropSideDirsExcept(null);
        }

        public string NewSideDirName() => "side-" + Guid.NewGuid().ToString("N");

        public void DropSideDirsExcept(string? keep)
        {
            if (!Directory.Exists(Root)) return;
            foreach (var dir in Directory.EnumerateDirectories(Root, "side-*"))
                if (!string.Equals(Path.GetFileName(dir), keep, StringComparison.OrdinalIgnoreCase))
                    TryDeleteDir(dir);
        }

        private string Keep(string bucket, string key, string path)
        {
            if (!IsVolatile(path) || !File.Exists(path)) return path;
            var safeKey = string.Concat(key.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == '/' ? '_' : c));
            var dir = Path.Combine(FilesDir, bucket, safeKey);
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, Path.GetFileName(path));
            if (!File.Exists(dest) || new FileInfo(dest).Length != new FileInfo(path).Length)
                File.Copy(path, dest, overwrite: true);
            return dest;
        }

        private static bool IsUnder(string full, string root) =>
            full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        private static void TryDeleteDir(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
    }
}
