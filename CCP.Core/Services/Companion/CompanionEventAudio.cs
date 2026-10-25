using System.IO;

namespace ConditioningControlPanel.Services.Companion;

/// <summary>WPF CompanionPhraseService.ResolveEventAudio: the mod's exact generated clip, else silence.</summary>
public static class CompanionEventAudio
{
    public static string? Resolve(string? text) => ResolveInFolder(text,
        ModCompanionContent.ResolveActive(CompanionChannel.EventAudio).Path);

    internal static string? ResolveInFolder(string? text, string? folder)
    {
        if (folder == null || string.IsNullOrWhiteSpace(text)) return null;
        var slug = CompanionPhraseIds.Slugify(text);
        if (slug.Length == 0) return null;
        var path = Path.Combine(folder, slug + ".mp3");
        return File.Exists(path) ? path : null;
    }
}
