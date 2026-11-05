using System;
using System.Collections.Generic;
using System.IO;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Bookkeeping for decrypted / downloaded temp files that are swept above a cap.
///
/// The rule this exists for: a delete that is REFUSED (LibVLC and the WPF decoders open a file
/// without FILE_SHARE_DELETE, so a clip still on screen cannot be removed) must stay on the
/// books and be tried again. Every sweep in the app used to drop such a file from its list, so
/// it stayed on disk until the next launch: at 120 flashes a minute that is disk filling for as
/// long as the engine runs.
///
/// Held files are bounded by what is really open right now (a handful of players), so retrying
/// them on each sweep costs a few failed deletes, not a scan.
/// </summary>
internal sealed class MediaTempFileLedger
{
    private readonly object _gate = new();
    private readonly List<string> _live = new();
    private readonly List<string> _held = new();
    private readonly int _cap;
    private readonly Func<string, bool> _delete;

    /// <param name="cap">Tracked files above which the oldest are deleted.</param>
    /// <param name="delete">Returns true when the file is gone. Defaults to <see cref="TryDeleteFile"/>.</param>
    public MediaTempFileLedger(int cap, Func<string, bool>? delete = null)
    {
        _cap = Math.Max(0, cap);
        _delete = delete ?? TryDeleteFile;
    }

    /// <summary>Files whose delete was refused and is still owed.</summary>
    public int HeldCount { get { lock (_gate) return _held.Count; } }

    /// <summary>Files on the books that no sweep has tried to delete yet.</summary>
    public int LiveCount { get { lock (_gate) return _live.Count; } }

    /// <summary>Add a fresh file. Above the cap the oldest go, never the ones just handed out.</summary>
    public void Track(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        List<string> sweep;
        lock (_gate)
        {
            _live.Add(path);
            int over = Math.Max(0, _live.Count - _cap);
            // Held files are retried on every Track, not only above the cap: a consumer that
            // releases its own files (Goon, wallpaper) may never push the live list over it.
            if (over == 0 && _held.Count == 0) return;
            sweep = _live.GetRange(0, over);
            _live.RemoveRange(0, over);
            sweep.AddRange(_held);
            _held.Clear();
        }
        DeleteOrHold(sweep);
    }

    /// <summary>The consumer is done with this file: delete it now, or hold it for a retry.</summary>
    public void Release(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var sweep = new List<string> { path };
        lock (_gate)
        {
            _live.Remove(path);
            _held.Remove(path);
            // Every release is also a retry of what an earlier release could not delete.
            sweep.AddRange(_held);
            _held.Clear();
        }
        DeleteOrHold(sweep);
    }

    /// <summary>Delete everything on the books (stop, dispose). Refused deletes stay held.</summary>
    public void SweepAll()
    {
        List<string> sweep;
        lock (_gate)
        {
            sweep = new List<string>(_live);
            sweep.AddRange(_held);
            _live.Clear();
            _held.Clear();
        }
        DeleteOrHold(sweep);
    }

    private void DeleteOrHold(List<string> paths)
    {
        // File I/O stays outside the gate: a slow disk must not block the next Track.
        List<string>? refused = null;
        foreach (var path in paths)
        {
            bool gone;
            try { gone = _delete(path); }
            catch { gone = false; }
            if (!gone) (refused ??= new List<string>()).Add(path);
        }
        if (refused == null) return;
        lock (_gate)
        {
            foreach (var path in refused)
                if (!_held.Contains(path)) _held.Add(path);
        }
    }

    /// <summary>True when the file no longer exists afterwards. Never throws.</summary>
    public static bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return true;
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug("Temp file delete refused, will retry: {Error}", ex.Message);
            return false;
        }
    }
}
