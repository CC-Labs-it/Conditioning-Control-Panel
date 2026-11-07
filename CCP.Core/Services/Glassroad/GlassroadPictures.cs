using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Glassroad;

// WPF GlassroadRules.PicturesOn / PicturesFeed: the race's consent gate and stills wire.
internal static class GlassroadPictures
{
    public const string SettingKey = "media.remoteConsent";
    public const int Rows = 12;
    public const int Step = 6;

    /// <summary>B2 (owner 2026-10-29, handoff-1029/B2-RT-LINUX-DECISIONS.md): the Linux race host offers no Scrolller
    /// pictures until the shell's request gate lands (a 7.2.x follow-up); the player keeps My files and Off. Every
    /// other host follows the WPF rule. The head reads the OS once (GlassroadHostRules.LinuxHost) and passes it here.</summary>
    public static bool HostOffersRemote(bool linuxHost) => !linuxHost;

    /// <summary>WPF GlassroadRules.PicturesOn behind <see cref="HostOffersRemote"/>: a host that offers no remote
    /// pictures is off whatever the settings say, and no caller can leave the rule out.</summary>
    public static bool On(bool? own, string? source, bool consent, bool offline, bool hostOffersRemote)
        => hostOffersRemote && consent && !offline && (own == true || (own == null
            && !string.Equals(source ?? "local", "local", StringComparison.OrdinalIgnoreCase)));

    public static JObject Feed(IReadOnlyList<string> urls, int offset, IEnumerable<(string Id, string Label)> catalog)
    {
        var rows = new JArray();
        int n = Math.Min(urls.Count, Rows), start = urls.Count == 0 ? 0 : ((offset % urls.Count) + urls.Count) % urls.Count;
        for (int i = 0; i < n; i++)
            rows.Add(new JObject { ["url"] = urls[(start + i) % urls.Count], ["kind"] = "image" });
        var list = new JArray();
        foreach (var (id, label) in catalog) list.Add(new JObject { ["id"] = id, ["label"] = label });
        return new JObject { ["type"] = "scrolller-feed", ["v"] = 1, ["rows"] = rows, ["catalog"] = list };
    }
}
