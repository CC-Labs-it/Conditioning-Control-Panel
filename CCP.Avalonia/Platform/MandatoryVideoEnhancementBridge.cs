using System;
using System.IO;
using ConditioningControlPanel.Services.Deeper;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Owns the enhancement host for one mandatory video, independent of the Deeper player.
/// The playback integration must call Bind at clip start and Unbind before every teardown.
/// WPF VideoEnhancementBridge.cs:41-146.</summary>
internal sealed class MandatoryVideoEnhancementBridge : IDisposable
{
    private readonly EnhancementHostService _host = new();
    internal bool IsBound => _host.IsRunning;

    internal bool Bind(string? path, bool enabled, IPlaybackTimeSource source)
    {
        Unbind();
        if (!enabled || string.IsNullOrEmpty(path)) return false;
        try
        {
            var resolved = EnhancementResolver.ResolveForLocalMedia(path);
            if (!resolved.Found) return false;
            var loaded = resolved.FilePath != null
                ? _host.LoadFromFile(resolved.FilePath)
                : _host.LoadFromMemory(resolved.Enhancement!, "video:" + Path.GetFileName(path));
            if (loaded && _host.Bind(source)) return true;
        }
        catch (Exception ex) { Log.Warning(ex, "Mandatory video enhancement bind failed"); }
        Unbind();
        return false;
    }

    internal void Unbind() => _host.Unload();
    public void Dispose() => _host.Dispose();
}
