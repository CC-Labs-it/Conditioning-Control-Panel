using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using Newtonsoft.Json;
using Serilog;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// WPF BtnPickAssetsFolder_Click's pack migration (MainWindow/MainWindow.UiUpdates.cs ~2164-2330), portable:
    /// find the installed packs under the OLD assets folder and the default one (packs stranded there
    /// by an earlier move), then move each <c>.packs/&lt;guid&gt;</c> into the new folder and register it
    /// in settings (InstalledPackIds, PackGuidMap overwritten, auto-activated). The head asks first.
    ///
    /// <para>USER DATA: a move is copy -> verify -> remove. The copy lands in a staging folder outside
    /// <c>.packs</c> (so a crash mid-copy never leaves a half pack that discovery would register), is
    /// verified file by file (same relative paths, same sizes), and only then renamed into place. The
    /// source is deleted last, and only after that. Any failure before the rename deletes the staging copy
    /// and leaves the source untouched. WPF copied then deleted with no verify, and a failed copy left
    /// a partial destination that the next move skipped and registered.</para>
    /// </summary>
    public static class PackFolderMover
    {
        public sealed record Candidate(string SourceFolder, string PackName, long Bytes);

        /// <summary>Staging for in-flight copies, a sibling of <c>.packs</c> so discovery never sees it.</summary>
        internal const string StagingFolderName = ".packs-moving";

        /// <summary>WPF: the packs found in <paramref name="oldAssets"/>/.packs and
        /// <paramref name="defaultAssets"/>/.packs, never the destination's own.</summary>
        public static List<Candidate> Find(string oldAssets, string defaultAssets, string newAssets,
            Func<string, string>? loadManifest = null)
        {
            loadManifest ??= PackEncryptionService.LoadEncryptedManifest;
            var result = new List<Candidate>();
            var dest = Full(Path.Combine(newAssets, ".packs"));
            var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Full(Path.Combine(oldAssets, ".packs")),
                Full(Path.Combine(defaultAssets, ".packs")),
            };
            locations.Remove(dest);
            foreach (var packs in locations)
            {
                if (!Directory.Exists(packs)) continue;
                foreach (var dir in Directory.GetDirectories(packs))
                {
                    var manifestPath = Path.Combine(dir, ContentPackStore.ManifestFileName);
                    if (!File.Exists(manifestPath)) continue;
                    var name = Path.GetFileName(dir);
                    try
                    {
                        var m = JsonConvert.DeserializeObject<InstalledPackManifest>(loadManifest(manifestPath));
                        if (!string.IsNullOrEmpty(m?.PackName)) name = m!.PackName;
                    }
                    catch { }
                    long bytes = 0;
                    try { foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) bytes += new FileInfo(f).Length; }
                    catch { }
                    result.Add(new Candidate(dir, name, bytes));
                }
            }
            return result;
        }

        /// <summary>WPF: move each candidate into <paramref name="newAssets"/>/.packs and register it.
        /// A destination that already holds the guid is skipped (still registered), and a candidate that
        /// already IS the destination is a no-op. Throws on an I/O failure mid-move (the head shows
        /// msg_could_not_move_packs_0); the failing pack's source is then intact and nothing of it is
        /// left in the destination. Packs moved before the failure stay moved and registered.</summary>
        public static (int Moved, int Registered) Move(IReadOnlyList<Candidate> packs, string newAssets, AppSettings settings,
            Func<string, string>? loadManifest = null)
        {
            loadManifest ??= PackEncryptionService.LoadEncryptedManifest;
            var target = Path.Combine(newAssets, ".packs");
            if (!Directory.Exists(target))
            {
                var di = Directory.CreateDirectory(target);
                try { di.Attributes |= FileAttributes.Hidden; } catch { }
            }
            int moved = 0, registered = 0;
            foreach (var c in packs)
            {
                var guid = Path.GetFileName(c.SourceFolder);
                var dest = Path.Combine(target, guid);
                if (string.Equals(Full(c.SourceFolder), Full(dest), StringComparison.OrdinalIgnoreCase))
                    Log.Information("Pack '{Name}' is already in {Dest}", c.PackName, dest);
                else if (!Directory.Exists(dest))
                {
                    CopyVerified(c.SourceFolder, dest, Path.Combine(newAssets, StagingFolderName, guid));
                    moved++;
                    Log.Information("Moved pack '{Name}' from {Source} to {Dest}", c.PackName, c.SourceFolder, dest);
                    // The verified copy is in place; only now may the source go. A failed delete leaves a
                    // harmless duplicate, never a loss.
                    try { Directory.Delete(c.SourceFolder, recursive: true); }
                    catch (Exception ex) { Log.Warning(ex, "Pack copied but the old folder could not be removed: {Source}", c.SourceFolder); }
                }
                else Log.Warning("Pack folder already exists at destination, skipping: {Dest}", dest);

                var manifestPath = Path.Combine(dest, ContentPackStore.ManifestFileName);
                if (!File.Exists(manifestPath)) continue;
                try
                {
                    var m = JsonConvert.DeserializeObject<InstalledPackManifest>(loadManifest(manifestPath));
                    if (string.IsNullOrEmpty(m?.PackId)) continue;
                    settings.InstalledPackIds ??= new List<string>();
                    settings.PackGuidMap ??= new Dictionary<string, string>();
                    settings.ActivePackIds ??= new List<string>();
                    if (!settings.InstalledPackIds.Contains(m!.PackId)) settings.InstalledPackIds.Add(m.PackId);
                    settings.PackGuidMap[m.PackId] = guid;
                    if (!settings.ActivePackIds.Contains(m.PackId)) settings.ActivePackIds.Add(m.PackId);
                    registered++;
                }
                catch (Exception ex) { Log.Warning(ex, "Failed to register pack from manifest: {Path}", manifestPath); }
            }
            Log.Information("Moved {Moved}/{Total} packs, registered {Registered} in settings", moved, packs.Count, registered);
            return (moved, registered);
        }

        /// <summary>WPF FormatFileSize (MainWindow/MainWindow.UiUpdates.cs:2372), invariant digits.</summary>
        public static string FormatSize(long bytes)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (bytes >= 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024.0 * 1024.0)).ToString("F1", ci) + " GB";
            if (bytes >= 1024 * 1024) return (bytes / (1024.0 * 1024.0)).ToString("F1", ci) + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("F1", ci) + " KB";
            return $"{bytes} bytes";
        }

        /// <summary>Copy <paramref name="source"/> to <paramref name="staging"/>, verify, rename to
        /// <paramref name="dest"/>. On any failure the staging copy is deleted and the error rethrown;
        /// <paramref name="source"/> is only ever read.</summary>
        private static void CopyVerified(string source, string dest, string staging)
        {
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);   // our own leftover
                CopyRecursive(source, staging);
                Verify(source, staging);
                Directory.Move(staging, dest);   // same volume: a rename, so dest is all or nothing
            }
            catch
            {
                try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
                catch (Exception ex) { Log.Warning(ex, "Could not remove the partial pack copy {Staging}", staging); }
                throw;
            }
        }

        /// <summary>Every source file exists in the copy with the same size, and the copy holds nothing else.</summary>
        internal static void Verify(string source, string copy)
        {
            var want = Files(source);
            var got = Files(copy);
            if (want.Count != got.Count || want.Any(kv => !got.TryGetValue(kv.Key, out var len) || len != kv.Value))
                throw new IOException($"Pack copy of {source} did not verify ({got.Count}/{want.Count} files match).");
        }

        private static Dictionary<string, long> Files(string root) =>
            Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(root, f), f => new FileInfo(f).Length, StringComparer.Ordinal);

        private static void CopyRecursive(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var f in Directory.GetFiles(source)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)), overwrite: false);
            foreach (var d in Directory.GetDirectories(source)) CopyRecursive(d, Path.Combine(dest, Path.GetFileName(d)));
        }

        private static string Full(string p)
        {
            try { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch { return p; }
        }
    }
}
