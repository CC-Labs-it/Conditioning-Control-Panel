using System.IO;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>WPF CompanionPhraseService's event audio lookup over existing Core mod/content seams.
/// A mod without its own matching MP3 stays text-only.</summary>
// ponytail: only this lookup is ported; the frozen WPF CompanionPhraseService stays in its head.
public static class CompanionEventAudio
{
    public static string? Folder
    {
        get
        {
            if (CoreMods.ActiveModPackage?.InstalledPath is { } root)
            {
                var packaged = Path.Combine(root, "resources", "sounds", "event_audio");
                if (Directory.Exists(packaged)) return packaged;
            }
            if (CoreMods.ActiveModId is not { Length: > 0 } id) return null;
            var relative = Path.Combine(CompanionContentResolver.PerModRelativeDir(id), "event_audio");
            return ContentLocator.DirectoryExists(relative) ? ContentLocator.ResolveDirectory(relative) : null;
        }
    }

    public static string? Resolve(string? text)
    {
        var folder = Folder;
        if (folder == null || string.IsNullOrWhiteSpace(text)) return null;
        var slug = CompanionPhraseIds.Slugify(text);
        if (slug.Length == 0) return null;
        var path = Path.Combine(folder, slug + ".mp3");
        return File.Exists(path) ? path : null;
    }
}
