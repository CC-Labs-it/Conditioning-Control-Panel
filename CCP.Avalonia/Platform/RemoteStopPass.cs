using System;
using System.Collections.Generic;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF RemoteControlService.cs:964-969 -> App.xaml.cs:1849. Core stops its own effects.
internal static class RemoteStopPass
{
    internal static readonly HashSet<string> RemoteStopIds = new()
        { "quiz", "mantra-chant", "audio-playlist", "companion-audio", "mantra", "tube-speech" };

    internal static void Post() => Dispatcher.UIThread.Post(() =>
    {
        foreach (var surface in PanicSurfaces.All)
        {
            if (!RemoteStopIds.Contains(surface.Id)) continue;
            try { surface.Stop(MainShellWindow.Current); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Remote stop: {Id} failed", surface.Id); }
        }
    });
}
