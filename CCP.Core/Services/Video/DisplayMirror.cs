using System;
using Serilog;
namespace ConditioningControlPanel.Services;
// WPF Services/Video/ScreenMirrorService.cs:37-137. The head supplies OS topology calls.
public interface IDisplayTopology
{
    int ActiveScreenCount { get; }
    int Apply(bool clone);
}
public sealed class DisplayMirror(IDisplayTopology topology) : IDisposable
{
    private bool _disposed;
    public bool IsMirroring { get; private set; }
    public bool EnableMirror()
    {
        if (IsMirroring) return true;
        try
        {
            if (topology.ActiveScreenCount < 2 || topology.Apply(true) != 0) return false;
            return IsMirroring = true;
        }
        catch (Exception ex) { Log.Warning(ex, "Display clone failed"); return false; }
    }
    public bool DisableMirror()
    {
        if (!IsMirroring) return true;
        try
        {
            if (topology.Apply(false) != 0) return false;
            IsMirroring = false;
            return true;
        }
        catch (Exception ex) { Log.Warning(ex, "Display restore failed"); return false; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisableMirror();
    }
}
