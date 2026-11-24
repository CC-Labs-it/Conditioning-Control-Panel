using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ConditioningControlPanel.Services.JustDrop
{
    // WPF JustDropAssetHost.cs:25-85: one opaque source map per window.
    // Listing, serving and temporary pack files are not yet wired to this map.
    internal sealed class JustDropAssetMap
    {
        public const string Host = "justdrop-assets.ccp";
        public const string Origin = "https://" + Host;

        private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".avif" };
        private static readonly string[] VideoExts = { ".mp4", ".webm", ".m4v" };
        private static readonly Regex IdPath = new("^/([0-9a-f]{32})(\\.[a-z0-9]{2,5})$", RegexOptions.CultureInvariant);

        public sealed class Entry
        {
            public required string Ext { get; init; }
            public required bool Video { get; init; }
            public string? DiskPath { get; init; }
            public string? PackId { get; init; }
            public PackFileEntry? PackFile { get; init; }
        }

        private readonly object _gate = new();
        private readonly Dictionary<string, Entry> _byId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _idByIdentity = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsServable(string ext, bool video)
            => (video ? VideoExts : ImageExts).Contains(ext.ToLowerInvariant());

        public string? Take(string identity, Entry entry)
        {
            if (string.IsNullOrEmpty(identity) || !IsServable(entry.Ext, entry.Video)) return null;
            lock (_gate)
            {
                if (!_idByIdentity.TryGetValue(identity, out var id))
                {
                    id = Guid.NewGuid().ToString("N");
                    _idByIdentity[identity] = id;
                    _byId[id] = entry;
                }
                return $"{Origin}/{id}{_byId[id].Ext.ToLowerInvariant()}";
            }
        }

        public Entry? Resolve(string? requestUri)
        {
            if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var u)) return null;
            if (u.Scheme != Uri.UriSchemeHttps || !string.Equals(u.Host, Host, StringComparison.OrdinalIgnoreCase)
                || !u.IsDefaultPort || u.UserInfo.Length > 0) return null;
            var m = IdPath.Match(u.AbsolutePath);
            if (!m.Success) return null;
            lock (_gate)
            {
                if (!_byId.TryGetValue(m.Groups[1].Value, out var e)) return null;
                return string.Equals(e.Ext, m.Groups[2].Value, StringComparison.OrdinalIgnoreCase) ? e : null;
            }
        }

        // WPF Clear also deletes decrypted files; this slice does not create any.
        public void Clear()
        {
            lock (_gate) { _byId.Clear(); _idByIdentity.Clear(); }
        }
    }
}
