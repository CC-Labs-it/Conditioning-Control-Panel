using System.IO;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>WPF CompanionPhraseService's event audio lookup over existing Core mod/content seams.
/// A mod without its own matching MP3 stays text-only.</summary>
// ponytail: only this lookup is ported; the frozen WPF CompanionPhraseService stays in its head.
public static class CompanionEventAudio
{
    public static string? Folder => ModCompanionContent.ResolveActive(CompanionChannel.EventAudio).Path;

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
