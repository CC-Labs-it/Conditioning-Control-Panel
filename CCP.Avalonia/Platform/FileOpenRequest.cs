using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace ConditioningControlPanel.Avalonia.Platform
{
    // WPF App.xaml.cs:104-139. Validate again when a request crosses a process boundary.
    internal sealed class FileOpenRequest
    {
        readonly Queue<string?> _pending = new();
        Func<string?, Task>? _deliver;

        // Queue until the shell has opened. Enqueue and draining share the lock so arrival order is kept.
        internal Task Enqueue(string? payload)
        {
            lock (_pending)
            {
                if (_deliver != null) return _deliver(payload);
                _pending.Enqueue(payload);
                return Task.CompletedTask;
            }
        }

        internal void Ready(Func<string?, Task> deliver)
        {
            lock (_pending)
            {
                _deliver = deliver;
                while (_pending.TryDequeue(out var payload)) _ = deliver(payload);
            }
        }

        internal static string? Encode(string[] args)
        {
            var (action, path) = Parse(args);
            return action == null ? null : "file:" + action + ":" + path;
        }

        // A file prefix is reserved even when invalid: never route a malformed file as a launcher request.
        internal static bool Decode(string? payload, out string? action, out string? path)
        {
            action = null;
            path = null;
            if (payload == null || !payload.StartsWith("file:", StringComparison.Ordinal)) return false;
            var sep = payload.IndexOf(':', 5);
            if (sep < 0) return true;
            var candidate = payload[5..sep];
            if (candidate is not ("play" or "edit")) return true;
            path = Validate(payload[(sep + 1)..]);
            if (path != null) action = candidate;
            return true;
        }

        static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v",
            ".mp3", ".wav", ".m4a", ".aac", ".flac", ".ogg"
        };

        internal static (string? action, string? path) Parse(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] is "--play" or "--edit")
                {
                    var path = Validate(args[i + 1]);
                    return path == null ? (null, null) : (args[i][2..], path);
                }
            return (null, null);
        }

        internal static string? Validate(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            try
            {
                var full = Path.GetFullPath(raw);
                return Path.IsPathRooted(full) && File.Exists(full) && Extensions.Contains(Path.GetExtension(full))
                    ? full : null;
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
