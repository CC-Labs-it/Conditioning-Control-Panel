using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Quiz;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>The page's local image manifest, sent as <c>init.settings.localAssets</c> (WPF ArcademyHostService.cs:1476,1617;
/// page provider/index.js:229-232). Without it every local draw falls back to a bundled tile.</summary>
public static class ArcademyLocalAssets
{
    /// <summary>Up to 60 active gifs and 60 active stills under <c>images/</c>; <paramref name="assetUrl"/> turns an
    /// unescaped relative path into the head's URL. An animated WebP moves to gifs with WPF's "#.gif" hint
    /// (ArcademyHostService.cs:1658,1688), because the page re-derives each entry's kind from its URL.</summary>
    public static JObject Build(string assetsRoot, IEnumerable<string>? disabledPaths, Func<string, string> assetUrl)
    {
        var (gifs, stills) = IntakeRun.SampleMedia(assetsRoot, disabledPaths, 60);
        var loops = gifs.Select(p => assetUrl(Uri.UnescapeDataString(p))).ToList();
        var images = new List<string>();
        foreach (var escaped in stills)
        {
            var rel = Uri.UnescapeDataString(escaped);
            var url = assetUrl(rel);
            if (Path.GetExtension(rel).Equals(".webp", StringComparison.OrdinalIgnoreCase)
                && IsAnimatedWebpFile(Path.Combine(assetsRoot, rel.Replace('/', Path.DirectorySeparatorChar))))
                loops.Add(url + "#.gif");
            else images.Add(url);
        }
        return new JObject { ["gifs"] = new JArray(loops), ["stills"] = new JArray(images) };
    }

    internal static bool IsAnimatedWebpFile(string file)
    {
        try
        {
            using var fs = File.OpenRead(file);
            var head = new byte[21];
            return ScrolllerDiskCache.IsAnimatedWebp(head, fs.Read(head, 0, head.Length));
        }
        catch { return false; }
    }
}
