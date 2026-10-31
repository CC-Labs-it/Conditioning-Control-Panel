using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public partial class WebHost
{
    // UI-thread only, like the hosts and PanicSurfaces. Do not keep closed previews alive.
    private static readonly List<WeakReference<WebHost>> ScrolllerHosts = new();

    private void TrackScrolllerHost()
    {
        ScrolllerHosts.RemoveAll(w => !w.TryGetTarget(out _));
        ScrolllerHosts.Add(new(this));
    }

    private Uri? _scrolllerPanicTarget;

    private static bool IsScrolllerMedia(Uri? uri) => uri is { IsAbsoluteUri: true }
        && (ScrolllerPolicy.IsScrolllerUrl(uri.AbsoluteUri) && !ScrolllerWebRules.IsApiHost(uri.Host)
                && ScrolllerWebRules.KindOf(uri.AbsoluteUri) != ScrolllerMediaKind.Other
            || uri.IsLoopback && uri.AbsolutePath.StartsWith(ScrolllerRelay.PathPrefix, StringComparison.Ordinal));

    // Re-cut from 21096f217 WebHost.Scrolller.cs. WPF MainWindow.xaml.cs:2042-2055
    // stops web/video surfaces; DeeperEditorWindow.xaml.cs:5302 disposes its preview on close.
    // This also clears a retained preview, stricter than WPF, without changing the licence switch
    // or the base's native request gate, relay, navigation hold or in-app browser exemption.
    internal static void StopScrolllerForPanic()
    {
        ScrolllerHosts.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var weak in ScrolllerHosts)
            if (weak.TryGetTarget(out var host) && host.GateScrolller
                && (IsScrolllerMedia(host.Source) || IsScrolllerMedia(host.CurrentUrl) || IsScrolllerMedia(host._scrolllerPanicTarget)))
            {
                try { host._web?.Stop(); }
                catch (Exception ex) { Serilog.Log.Debug(ex, "WebHost: panic stop failed"); }
                finally
                {
                    // Clear even while the native gate is pending: its late report must not reload it.
                    host.Source = null;
                    host.CurrentUrl = null;
                    host._scrolllerPanicTarget = null;
                    try { if (host._web != null) host._web.Source = null!; }
                    catch (Exception ex) { Serilog.Log.Debug(ex, "WebHost: panic clear failed"); }
                }
            }
    }
}
