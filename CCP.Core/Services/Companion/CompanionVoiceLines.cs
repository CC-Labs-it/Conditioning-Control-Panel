using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>Shared WPF/Avalonia voice-line catalogue. Files are re-enumerated after content arrives.</summary>
public static class CompanionVoiceLines
{
    public static (string Absolute, string? Relative) ResolveFolder() =>
        ResolveFolder(CoreMods.ActiveModId, CoreMods.ActiveModPackage?.InstalledPath);

    internal static (string Absolute, string? Relative) ResolveFolder(string? modId, string? installedPath)
    {
        string? packaged = installedPath == null ? null :
            Path.Combine(installedPath, "resources", "sounds", "flashes_audio");
        if (packaged != null && Directory.Exists(packaged)) return (packaged, null);
        string? perMod = string.IsNullOrEmpty(modId) ? null :
            Path.Combine(CompanionContentResolver.PerModRelativeDir(modId), "flashes_audio");
        if (perMod != null && ContentLocator.DirectoryExists(perMod))
            return (ContentLocator.ResolveDirectory(perMod), perMod);
        // The shared folder is not a neutral fallback - it is BambiSleep's own recorded VO, and
        // FlashService prints each clip's file name in the companion's speech bubble as it plays.
        // Only the mods it belongs to may speak with it (see
        // CompanionContentResolver.OwnsBaselineVoiceLines); anyone else points at their OWN
        // (possibly empty) folder and simply has no voice lines, instead of borrowing Bambi's.
        if (CompanionContentResolver.OwnsBaselineVoiceLines(modId))
        {
            var relative = CompanionContentResolver.VoiceLinesRelativeDir;
            return (ContentLocator.ResolveDirectory(relative), relative);
        }
        if (packaged != null) return (packaged, null);
        return (ContentLocator.ResolveDirectory(perMod!), perMod);
    }

    public static List<string> GetFiles() => GetFiles(ResolveFolder());

    internal static List<string> GetFiles((string Absolute, string? Relative) folder)
    {
        try
        {
            var files = new List<string>();
            foreach (var pattern in new[] { "*.mp3", "*.wav", "*.ogg", "*.flac" })
            {
                if (folder.Relative != null) files.AddRange(ContentLocator.EnumerateFiles(folder.Relative, pattern));
                else if (Directory.Exists(folder.Absolute)) files.AddRange(Directory.GetFiles(folder.Absolute, pattern));
            }
            files.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
            return files;
        }
        catch { return new List<string>(); }
    }

    // Leave positional IDs untouched while content is absent, so a later download can map them.
    // Numeric filenames are already stable IDs and must not be mistaken for positional IDs.
    public static bool MigrateLegacyIds(AppSettings settings, IReadOnlyList<string> sortedFiles)
    {
        if (sortedFiles.Count == 0) return false;
        // Built only once a positional id is seen: GetRows runs on every dialog open.
        HashSet<string>? currentIds = null;
        var changed = false;
        foreach (var set in new[] { settings.DisabledPhraseIds, settings.RemovedPhraseIds })
        {
            foreach (var old in set.ToArray())
            {
                const string prefix = "VoiceLine:";
                if (!old.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var tail = old[prefix.Length..];
                if (tail.Length == 0 || !tail.All(char.IsDigit)) continue;
                currentIds ??= new HashSet<string>(sortedFiles.Select(CompanionPhraseIds.VoiceLineId), StringComparer.OrdinalIgnoreCase);
                if (currentIds.Contains(old)) continue;
                set.Remove(old);
                changed = true;
                if (int.TryParse(tail, out var index) && index < sortedFiles.Count)
                    set.Add(CompanionPhraseIds.VoiceLineId(sortedFiles[index]));
            }
        }
        return changed;
    }

    public static List<CompanionPhrase> GetRows(AppSettings settings)
    {
        var files = GetFiles();
        try
        {
            if (MigrateLegacyIds(settings, files))
            {
                CoreSettings.Save();
                Serilog.Log.Information(
                    "CompanionPhraseService: migrated positional VoiceLine phrase ids to filename-based ids");
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "CompanionPhraseService: failed to migrate legacy VoiceLine phrase ids");
        }
        return files.Where(file => !settings.RemovedPhraseIds.Contains(CompanionPhraseIds.VoiceLineId(file)))
            .Select(file => new CompanionPhrase
            {
                Id = CompanionPhraseIds.VoiceLineId(file), Text = Path.GetFileNameWithoutExtension(file),
                Category = "VoiceLine", IsBuiltIn = true,
                IsEnabled = !settings.DisabledPhraseIds.Contains(CompanionPhraseIds.VoiceLineId(file)),
                AudioFileName = Path.GetFileName(file), AudioFolder = Path.GetDirectoryName(file),
            }).ToList();
    }
}
