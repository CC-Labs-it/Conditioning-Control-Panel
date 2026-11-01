using System;
using System.Collections.Generic;
using System.IO;
namespace ConditioningControlPanel.Services.MediaFiles;
/// <summary>Tracks temp files and retries refused deletes until consumers release them.</summary>
public sealed class MediaTempFileLedger
{
    private readonly object _gate = new();
    private readonly List<string> _live = new();
    private readonly List<string> _held = new();
    private readonly int _cap;
    private readonly Func<string, bool> _delete;
    public MediaTempFileLedger(int cap, Func<string, bool>? delete = null)
    {
        _cap = Math.Max(0, cap);
        _delete = delete ?? TryDeleteFile;
    }
    public int HeldCount { get { lock (_gate) return _held.Count; } }
    public int LiveCount { get { lock (_gate) return _live.Count; } }
    public void Track(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        List<string> sweep;
        lock (_gate)
        {
            _live.Add(path);
            int over = Math.Max(0, _live.Count - _cap);
            if (over == 0 && _held.Count == 0) return;
            sweep = _live.GetRange(0, over);
            _live.RemoveRange(0, over);
            sweep.AddRange(_held);
            _held.Clear();
        }
        DeleteOrHold(sweep);
    }
    public void Release(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var sweep = new List<string> { path };
        lock (_gate)
        {
            _live.Remove(path);
            _held.Remove(path);
            sweep.AddRange(_held);
            _held.Clear();
        }
        DeleteOrHold(sweep);
    }
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
        try { File.Delete(path); return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (Exception ex)
        {
            Serilog.Log.Debug("Temp file delete refused, will retry: {Error}", ex.Message);
            return false;
        }
    }
}
