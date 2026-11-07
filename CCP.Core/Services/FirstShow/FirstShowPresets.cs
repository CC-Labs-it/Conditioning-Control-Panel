using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Services.FirstShow;

internal static class FirstShowPresets
{
    internal static readonly string[] All = { "EroticHypnosis", "HypnoGoneWild", "sissyhypno", "bimbofication", "cosplay", "censored" };
    internal static string[] Sources(string preset) => preset == "censored"
        ? FypOnlineCoordinator.Catalog.First(n => n.Id == "censored").Subs
        : new[] { preset };

    /// <summary>The source a yes turns on when it is still "local" (WPF MainWindow.Assets.cs MediaSrcMixed). Owner
    /// decision 2026-11-06: mixed, not online, so a library the user already has keeps playing beside the feed.</summary>
    internal const string MixedSource = "mixed";

    /// <summary>
    /// Owner decision 2026-11-06 (option B): every preset's pictures come from Scrolller, and the device cache refuses
    /// all of them until the user has said yes to remote media AND left the "local" source
    /// (<see cref="ScrolllerDiskCache.RemoteConsent"/>). True when that is the only thing in the way, so picking a
    /// preset asks first. The licence switch, a spent share and Offline mode are not consent: they fall back as before.
    /// </summary>
    internal static bool AsksForRemoteMedia(AppSettings? s, bool licenceOn, bool shareSpent) =>
        s != null && !(s.MediaSource != "local" && s.HasRemoteMediaConsent) && licenceOn && !shareSpent && !s.OfflineMode;

    internal static bool AsksForRemoteMedia(AppSettings? s) =>
        s != null && AsksForRemoteMedia(s, LicenceOn(), ShareSpent());

    /// <summary>Test seams: the licence switch, and today's share (read from the device's allowance book).</summary>
    internal static Func<bool> LicenceOn = () => ScrolllerPolicy.Enabled;
    internal static Func<bool> ShareSpent = () => ScrolllerAllowance.Spent;

    /// <summary>A yes to the consent card: what the Assets page's source chip records after its ask (WPF
    /// MainWindow.Assets.cs RemoteSourceChip_Changed: RemoteMediaConsented, then the source). A source that already
    /// fetches (online / mixed) stays as it is; "local" becomes <see cref="MixedSource"/>. The caller saves and resets
    /// the channels.</summary>
    internal static void AcceptRemoteMedia(AppSettings s)
    {
        s.RemoteMediaConsented = true;
        if (string.Equals(s.MediaSource, "local", StringComparison.OrdinalIgnoreCase)) s.MediaSource = MixedSource;
    }
}
