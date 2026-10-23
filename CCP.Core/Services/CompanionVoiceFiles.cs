using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;

namespace ConditioningControlPanel.Services;

/// <summary>WPF CompanionPhraseService's recorded voice pool, shared by both heads.</summary>
public static class CompanionVoiceFiles
{
    private static readonly string AudioRelative = Path.Combine("Resources", "sounds", "companion_audio");
    private static readonly string VoiceRelative = Path.Combine("Resources", "sounds", "flashes_audio");

    public static (string Absolute, string? Relative) ResolveFolder(string? modId, string? modPath)
    {
        var packaged = modPath == null ? null : Path.Combine(modPath, "resources", "sounds", "flashes_audio");
        if (packaged != null && Directory.Exists(packaged)) return (packaged, null);
        var perMod = string.IsNullOrEmpty(modId) ? null : Path.Combine(AudioRelative, "mods", modId, "flashes_audio");
        if (perMod != null && ContentLocator.DirectoryExists(perMod))
            return (ContentLocator.ResolveDirectory(perMod), perMod);
        if (CompanionContentResolver.OwnsBaselineVoiceLines(modId))
            return (ContentLocator.ResolveDirectory(VoiceRelative), VoiceRelative);
        if (packaged != null) return (packaged, null);
        return (ContentLocator.ResolveDirectory(perMod!), perMod);
    }

    public static List<string> GetFiles(string? modId, string? modPath)
    {
        try
        {
            var (absolute, relative) = ResolveFolder(modId, modPath);
            var files = new List<string>();
            foreach (var extension in new[] { "*.mp3", "*.wav", "*.ogg", "*.flac" })
                if (relative != null) files.AddRange(ContentLocator.EnumerateFiles(relative, extension));
                else if (Directory.Exists(absolute)) files.AddRange(Directory.GetFiles(absolute, extension));
            files.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
            return files;
        }
        catch { return new List<string>(); }
    }

    /// <summary>Leave legacy IDs intact while a content pack is absent. Return whether settings changed.</summary>
    public static bool MigrateLegacyIds(AppSettings? settings, IReadOnlyList<string> sortedFiles)
    {
        if (settings == null || sortedFiles.Count == 0) return false;
        HashSet<string>? currentIds = null;   // built only when a legacy id exists: the common call allocates nothing
        var changed = false;
        foreach (var set in new[] { settings.DisabledPhraseIds, settings.RemovedPhraseIds })
        {
            if (set == null || !set.Any(IsLegacyId)) continue;
            currentIds ??= sortedFiles.Select(CompanionPhraseIds.VoiceLineId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var old in set.Where(IsLegacyId).ToArray())
            {
                if (currentIds.Contains(old)) continue;
                set.Remove(old);
                changed = true;
                if (int.TryParse(old["VoiceLine:".Length..], out var index) && index >= 0 && index < sortedFiles.Count)
                    set.Add(CompanionPhraseIds.VoiceLineId(sortedFiles[index]));
            }
        }
        return changed;
    }

    private static bool IsLegacyId(string id) =>
        !string.IsNullOrEmpty(id) && id.StartsWith("VoiceLine:", StringComparison.Ordinal)
        && id.Length > "VoiceLine:".Length && id["VoiceLine:".Length..].All(char.IsDigit);

    public static List<string> Enabled(IReadOnlyList<string> files, AppSettings? settings, string customAudioFolder)
    {
        var result = files.Where(file =>
            settings?.RemovedPhraseIds?.Contains(CompanionPhraseIds.VoiceLineId(file)) != true &&
            settings?.DisabledPhraseIds?.Contains(CompanionPhraseIds.VoiceLineId(file)) != true).ToList();
        if (settings?.CustomCompanionPhrases != null)
            foreach (var custom in settings.CustomCompanionPhrases)
                if (custom.Category == "VoiceLine" && custom.Enabled && !string.IsNullOrEmpty(custom.AudioFileName))
                {
                    var path = Path.Combine(customAudioFolder, custom.AudioFileName);
                    if (File.Exists(path)) result.Add(path);
                }
        return result;
    }
}
