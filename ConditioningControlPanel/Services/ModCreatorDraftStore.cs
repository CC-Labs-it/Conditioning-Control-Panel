using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        private readonly object _gate = new();
        private DateTime _lastWrittenUtc = DateTime.MinValue;

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

        /// <summary>Thread safe (autosave runs off the UI thread). A draft older than the last one written, or than an
        /// export, is dropped, so an autosave that lands late never writes over a newer save.</summary>
        public void Save(ModCreatorDraft draft)
        {
            lock (_gate) SaveLocked(draft);
        }

        private void SaveLocked(ModCreatorDraft draft)
        {
            if (draft.SavedAtUtc < _lastWrittenUtc) return;
            Directory.CreateDirectory(Root);
            draft.Images = draft.Images.ToDictionary(kv => kv.Key, kv => Keep("img", kv.Key, kv.Value));
            draft.Audio = draft.Audio.ToDictionary(kv => kv.Key, kv => Keep("audio", kv.Key, kv.Value));
            draft.VoiceLines = draft.VoiceLines.Select(p => Keep("voice", "", p)).ToList();

            var tmp = DraftPath + ".tmp";
            File.WriteAllText(tmp, JsonConvert.SerializeObject(draft, Formatting.Indented,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
            File.Move(tmp, DraftPath, overwrite: true);
            _lastWrittenUtc = draft.SavedAtUtc;
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
            lock (_gate)
            {
                Directory.CreateDirectory(Root);
                File.WriteAllText(ExportStampPath, utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                TryDelete(DraftPath);
                if (utc.ToUniversalTime() > _lastWrittenUtc) _lastWrittenUtc = utc.ToUniversalTime();
            }
        }

        /// <summary>Drops the draft and its copies; <paramref name="keepSideDir"/> is a side folder the open form still reads.</summary>
        public void Discard(string? keepSideDir = null) { lock (_gate) { TryDelete(DraftPath); PruneIfNoDraft(keepSideDir); } }

        /// <summary>On open with no draft on disk nothing can reference the copied files: clear them.</summary>
        public void PruneIfNoDraft(string? keepSideDir = null)
        {
            if (File.Exists(DraftPath)) return;
            TryDeleteDir(FilesDir);
            DropSideDirsExcept(keepSideDir);
        }

        /// <summary>Which side folder the draft keeps after a close wrote <paramref name="written"/>: that one when every
        /// copy landed; otherwise the previous complete one (if it is still there), and the partial one goes.</summary>
        public string SettleSideDir(string written, bool complete, string? previous)
        {
            if (complete || string.IsNullOrEmpty(previous) || !Directory.Exists(Path.Combine(Root, previous))) return written;
            TryDeleteDir(Path.Combine(Root, written));
            return previous;
        }

        public void DropSideDir(string name)
        {
            if (name.StartsWith("side-", StringComparison.OrdinalIgnoreCase) && name.IndexOfAny(new[] { '/', '\\' }) < 0)
                TryDeleteDir(Path.Combine(Root, name));
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
            // Media identity is the whole path: two mods' same-named files never share a copy.
            var full = Path.GetFullPath(path);
            var id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant())))[..16];
            var dir = Path.Combine(FilesDir, bucket, safeKey, id);
            Directory.CreateDirectory(dir);
            var dest = Path.Combine(dir, Path.GetFileName(path));
            var src = new FileInfo(full);
            var copy = new FileInfo(dest);
            if (!copy.Exists || copy.Length != src.Length || copy.LastWriteTimeUtc != src.LastWriteTimeUtc)
            {
                File.Copy(full, dest, overwrite: true);
                File.SetLastWriteTimeUtc(dest, src.LastWriteTimeUtc);
            }
            return dest;
        }

        private static bool IsUnder(string full, string root) =>
            full.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        private static void TryDeleteDir(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
    }
}
