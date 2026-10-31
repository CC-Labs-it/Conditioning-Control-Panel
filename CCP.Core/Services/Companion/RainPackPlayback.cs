using System;
using System.IO;

namespace ConditioningControlPanel.Services.Companion.Rain;

/// <summary>Chooses the tube's baked Rain folder, with the embedded V2 folder as fallback.</summary>
internal static class RainPackPlayback
{
    public static string? ResolveTubeFolder(string? generation, string? v2Folder, string? packHash,
        Func<string, string?> tryGetPackFolder, Func<string?> v1Folder)
    {
        if (AvatarGenerationRule.IsV2(generation))
            v2Folder = PackOrEmbedded(v2Folder, packHash, tryGetPackFolder);
        return AvatarGenerationRule.ResolveFolder(generation, v2Folder, v1Folder);
    }

    public static string? PackOrEmbedded(string? v2Folder, string? packHash, Func<string, string?> tryGetPackFolder)
    {
        if (string.IsNullOrEmpty(v2Folder) || string.IsNullOrWhiteSpace(packHash)) return v2Folder;
        try
        {
            var pack = tryGetPackFolder(packHash.Trim());
            return string.IsNullOrEmpty(pack) || !Path.IsPathRooted(pack) ? v2Folder : pack;
        }
        catch (Exception) { return v2Folder; }
    }
}
