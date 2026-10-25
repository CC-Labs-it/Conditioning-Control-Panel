using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Flash;

/// <summary>
/// The online flash clips on disk: clip URL -> (materialized file, poster URL). This set OWNS the
/// files (owned copies), so the shared device-cache sweep can no
/// longer delete a clip between the draw and LibVLC opening it. That sweep is what turned every
/// flash into a still a few rounds in: each round warms ~30 clips, so the files the draw had just
/// picked were the oldest on the sweep's books.
///
/// Trimming is oldest-USED first and never touches a clip used inside <see cref="RecentMs"/>
/// unless the hard ceiling is hit. Thread-safe. Returns paths for the caller to release.
/// </summary>
internal sealed class RemoteClipFiles
{
    internal const int SoftCap = 64;
    internal const int HardCap = 128;
    internal const long RecentMs = 120_000;

    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _byUrl = new(StringComparer.Ordinal);

    private sealed class Entry
    {
        public required string Path;
        public required string Poster;
        public long UsedMs;
        public long Order;
    }

    private long _order;

    public int Count { get { lock (_gate) return _byUrl.Count; } }

    /// <summary>Look up a clip and mark it used now, so a trim leaves it alone for a while.</summary>
    public bool TryUse(string url, long nowMs, out string path, out string poster)
    {
        lock (_gate)
        {
            if (_byUrl.TryGetValue(url, out var e))
            {
                e.UsedMs = nowMs;
                path = e.Path; poster = e.Poster;
                return true;
            }
        }
        path = poster = "";
        return false;
    }

    /// <summary>Add or replace a clip. Returns the files that are no longer referenced.</summary>
    public List<string> Put(string url, string path, string poster, long nowMs)
    {
        var drop = new List<string>();
        lock (_gate)
        {
            if (_byUrl.TryGetValue(url, out var old) && !string.Equals(old.Path, path, StringComparison.OrdinalIgnoreCase))
                drop.Add(old.Path);
            _byUrl[url] = new Entry { Path = path, Poster = poster, UsedMs = nowMs, Order = ++_order };
            Trim(nowMs, url, drop);
        }
        return drop;
    }

    /// <summary>Everything, for teardown.</summary>
    public List<string> TakeAll()
    {
        lock (_gate)
        {
            var all = new List<string>(_byUrl.Count);
            foreach (var e in _byUrl.Values) all.Add(e.Path);
            _byUrl.Clear();
            return all;
        }
    }

    private void Trim(long nowMs, string keep, List<string> drop)
    {
        if (_byUrl.Count <= SoftCap) return;
        var oldest = new List<KeyValuePair<string, Entry>>(_byUrl);
        oldest.Sort((a, b) => a.Value.UsedMs != b.Value.UsedMs
            ? a.Value.UsedMs.CompareTo(b.Value.UsedMs) : a.Value.Order.CompareTo(b.Value.Order));
        foreach (var (url, e) in oldest)
        {
            if (_byUrl.Count <= SoftCap) break;
            if (url == keep) continue;
            bool recent = nowMs - e.UsedMs < RecentMs;
            if (recent && _byUrl.Count <= HardCap) continue;
            _byUrl.Remove(url);
            drop.Add(e.Path);
        }
    }
}
