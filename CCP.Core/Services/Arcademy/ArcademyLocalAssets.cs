using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Quiz;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Arcademy;

/// <summary>Local image manifest for the Arcademy page (WPF ArcademyHostService.cs:1604).</summary>
public static class ArcademyLocalAssets
{
    public static JObject Build(string assetsRoot, IEnumerable<string>? disabledPaths)
    {
        var (gifs, stills) = IntakeRun.SampleMedia(assetsRoot, disabledPaths, 60);
        var loops = gifs.Select(p => "https://ccp.assets/" + p).ToList();
        var images = new List<string>();
        foreach (var path in stills)
        {
            var url = "https://ccp.assets/" + path;
            var file = Path.Combine(assetsRoot, Uri.UnescapeDataString(path).Replace('/', Path.DirectorySeparatorChar));
            if (AnimatedWebp(file)) loops.Add(url + "#.gif");
            else images.Add(url);
        }
        return new JObject { ["gifs"] = new JArray(loops), ["stills"] = new JArray(images) };
    }

    private static bool AnimatedWebp(string file)
    {
        if (!Path.GetExtension(file).Equals(".webp", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var stream = File.OpenRead(file);
            var header = new byte[21];
            return stream.Read(header, 0, header.Length) == header.Length
                && header[0] == 'R' && header[1] == 'I' && header[2] == 'F' && header[3] == 'F'
                && header[8] == 'W' && header[9] == 'E' && header[10] == 'B' && header[11] == 'P'
                && header[12] == 'V' && header[13] == 'P' && header[14] == '8' && header[15] == 'X'
                && (header[20] & 2) != 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
