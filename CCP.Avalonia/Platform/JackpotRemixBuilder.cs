using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ConditioningControlPanel.Services.Remix;

namespace ConditioningControlPanel.Avalonia.Platform;

// Disk boundaries for the future browser host. WPF: Services/Remix/JackpotRemixBuilder.cs:142,216.
internal sealed partial class JackpotRemixBuilder
{
    internal const int MaxOutputBytes = 32 * 1024 * 1024;

    // Filter before cycling so a path outside the library cannot leave fewer than eight tiles.
    internal static IReadOnlyList<string> Sources(string root, IEnumerable<string> paths, long cap = JackpotRemixPlan.MaxSourceBytes)
    {
        var usable = JackpotRemixPlan.FilterBySize(paths, p =>
        {
            var relative = Path.GetRelativePath(root, p);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return -1;
            return new FileInfo(p).Length;
        }, cap);
        return JackpotRemixPlan.Cycle(usable);
    }

    // Reject oversized replies before decoding and require a GIF signature before writing.
    internal static string? WriteOutput(string folder, string code, string base64, CancellationToken cancellationToken, int cap = MaxOutputBytes)
    {
        if (cancellationToken.IsCancellationRequested || cap < 6 || string.IsNullOrEmpty(base64) ||
            base64.Length > ((long)cap + 2) / 3 * 4) return null;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { return null; }
        if (bytes.Length > cap || bytes.Length < 6 ||
            !(bytes.Take(6).SequenceEqual("GIF87a"u8.ToArray()) || bytes.Take(6).SequenceEqual("GIF89a"u8.ToArray()))) return null;
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, JackpotRemixPlan.OutputName(code, DateTime.UtcNow));
        var temp = Path.Combine(folder, ".remix-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temp, path, true);
            JackpotRemixPlan.Bound(folder);
            return path;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
