using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Services;

/// <summary>WPF CompanionPhraseService voice-line listing and positional-id migration.</summary>
public static class CompanionVoiceLines
{
    public static List<CompanionPhrase> Load()
    {
        var files = FindFiles(CoreMods.ActiveModId, CoreMods.ActiveModPackage?.InstalledPath);
        var s = CoreSettings.Current;
        if (MigrateIds(files, s.DisabledPhraseIds, s.RemovedPhraseIds)) CoreSettings.Save();
        return files.Where(f => !s.RemovedPhraseIds.Contains(CompanionPhraseIds.VoiceLineId(f)))
            .Select(f => new CompanionPhrase
            {
                Id = CompanionPhraseIds.VoiceLineId(f), Text = Path.GetFileNameWithoutExtension(f),
                Category = "VoiceLine", IsBuiltIn = true,
                IsEnabled = !s.DisabledPhraseIds.Contains(CompanionPhraseIds.VoiceLineId(f)),
                AudioFileName = Path.GetFileName(f), AudioFolder = Path.GetDirectoryName(f)
            }).ToList();
    }

    internal static List<string> FindFiles(string? modId, string? installedPath)
    {
        try
        {
            string? absolute = installedPath == null ? null : Path.Combine(installedPath, "resources", "sounds", "flashes_audio");
            string? relative = null;
            if (absolute == null || !Directory.Exists(absolute))
            {
                relative = Path.Combine(CompanionContentResolver.CompanionAudioRelativeDir, "mods", modId ?? "", "flashes_audio");
                if (!ContentLocator.DirectoryExists(relative) && CompanionContentResolver.OwnsBaselineVoiceLines(modId))
                    relative = CompanionContentResolver.VoiceLinesRelativeDir;
            }
            var files = new List<string>();
            foreach (var ext in new[] { "*.mp3", "*.wav", "*.ogg", "*.flac" })
                if (relative != null) files.AddRange(ContentLocator.EnumerateFiles(relative, ext));
                else if (Directory.Exists(absolute)) files.AddRange(Directory.GetFiles(absolute!, ext));
            files.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
            return files;
        }
        catch { return new(); }
    }

    internal static bool MigrateIds(List<string> files, params HashSet<string>[] sets)
    {
        if (files.Count == 0) return false; // Audio can arrive later; preserve positional ids until then.
        var current = new HashSet<string>(files.Select(CompanionPhraseIds.VoiceLineId), StringComparer.OrdinalIgnoreCase);
        bool changed = false;
        foreach (var set in sets)
            foreach (var id in set.Where(id => id.StartsWith("VoiceLine:", StringComparison.Ordinal)
                         && id.Length > 10 && id.Substring(10).All(char.IsDigit)).ToList())
            {
                if (current.Contains(id)) continue; // A genuinely numeric filename wins.
                set.Remove(id);
                changed = true;
                if (int.TryParse(id.Substring(10), out var index) && index < files.Count)
                    set.Add(CompanionPhraseIds.VoiceLineId(files[index]));
            }
        return changed;
    }
}
