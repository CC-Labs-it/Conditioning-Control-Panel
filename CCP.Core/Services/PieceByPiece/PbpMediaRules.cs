// WPF 7.1.5 media consent and availability rules, unchanged behaviour.
using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.GoonGame.Online;
namespace ConditioningControlPanel.Services.PieceByPiece.Media;
internal static class PbpMediaRules
{
    public static bool HasSavedChoice(bool chosen, bool pbpMediaOnline, string? flavour)
        => chosen || !pbpMediaOnline || GoonOnlineMediaRules.CleanFlavour(flavour) != "";
    public static bool SavedOptIn(bool pbpMediaOnline, string? flavour)
        => GoonOnlineMediaRules.IsSessionOptIn(pbpMediaOnline, GoonOnlineMediaRules.CleanFlavour(flavour));
    public const int PickedSharePct = 70;
    public static bool AppWideOnline(string? mediaSource, bool remoteConsent)
        => remoteConsent && !string.Equals(mediaSource ?? "local", "local", StringComparison.OrdinalIgnoreCase);
    public static bool FetchAllowed(bool pbpMediaOnline, bool sessionOptIn, string? mediaSource, bool remoteConsent)
        => pbpMediaOnline && (sessionOptIn || AppWideOnline(mediaSource, remoteConsent));
    public static List<string> ChannelsFor(bool pbpMediaOnline, bool sessionOptIn, string? mediaSource,
        bool remoteConsent, string? flavour, IEnumerable<string>? flavourSubs, IEnumerable<string>? appChannels)
    {
        if (!FetchAllowed(pbpMediaOnline, sessionOptIn, mediaSource, remoteConsent)) return new List<string>();
        var picked = GoonOnlineMediaRules.CleanFlavour(flavour);
        if (picked != "") return GoonOnlineMediaRules.CleanSubs(flavourSubs);
        return AppWideOnline(mediaSource, remoteConsent)
            ? GoonOnlineMediaRules.CleanSubs(appChannels)
            : new List<string>();
    }
    public static int SharePct(string? mediaSource, int remoteRatio, bool sessionOptIn)
    {
        var src = (mediaSource ?? "local").ToLowerInvariant();
        if (src == "online") return 100;
        if (src == "mixed" && !sessionOptIn) return Math.Clamp(remoteRatio, 5, 95);
        return PickedSharePct;
    }
    public static bool MediaAvailable(bool hasLocalMedia, IReadOnlyCollection<string>? onlineChannels,
        string? onlineState = null)
        => hasLocalMedia || ((onlineChannels?.Count ?? 0) > 0 && !OnlineCameBackDry(onlineState));
    public static bool OnlineCameBackDry(string? onlineState)
        => string.Equals(onlineState, "off", StringComparison.OrdinalIgnoreCase)
           || string.Equals(onlineState, "empty", StringComparison.OrdinalIgnoreCase);
    public static bool FolderHasEntries(string? root)
    {
        try
        {
            return !string.IsNullOrEmpty(root) && System.IO.Directory.Exists(root)
                   && System.IO.Directory.EnumerateFileSystemEntries(root).Any();
        }
        catch (Exception) { return false; }
    }
}
