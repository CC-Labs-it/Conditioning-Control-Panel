using System;
using System.IO;

namespace ConditioningControlPanel.Services.Deeper;

/// <summary>WPF App.ParseFileOpenArgs / ValidateMediaArgPath.</summary>
public static class DeeperFileOpen
{
    public static string? Encode(string[]? args)
    {
        var request = Parse(args);
        return request.Action == null ? null : "deeper:" + request.Action + "\n" + request.Path;
    }

    public static (string? Action, string? Path) Decode(string? payload)
    {
        if (payload == null || !payload.StartsWith("deeper:", StringComparison.Ordinal)) return (null, null);
        var split = payload.IndexOf('\n');
        return split < 0 ? (null, null) : Parse(new[] { "--" + payload[7..split], payload[(split + 1)..] });
    }

    public static (string? Action, string? Path) Parse(string[]? args)
    {
        if (args == null) return (null, null);
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] != "--play" && args[i] != "--edit") continue;
            var raw = args[i + 1];
            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith(@"\\", StringComparison.Ordinal)) return (null, null);
            try
            {
                var path = System.IO.Path.GetFullPath(raw);
                if (File.Exists(path) && DropRules.IsDeeperPlayableMedia(path))
                    return (args[i] == "--play" ? "play" : "edit", path);
            }
            catch (Exception) { /* malformed path: WPF silently declines it */ }
            return (null, null);
        }
        return (null, null);
    }
}
