// WPF MainWindow.Possession.cs + PossessionWpfHead: the live shell uses the shared director.
// Trunk a4ee52d40cd347d288dcf7fdaa8445b79f8e5038: install/usability/panic path adapted to our Core API.
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    internal static PossessionDirector? PossessionDirector { get; private set; }
    private PossessionHost? _possessionHost;
    internal static void InstallPossession(LockdownService lockdown)
    {
        PossessionDirector?.Dispose();
        PossessionDirector = new PossessionDirector(lockdown, PossessionRuntime.CreateAll().Select(e => new PossessionDirectorEffect(e)), new PossessionDirectorHead
        {
            OnUi = a => { if (Dispatcher.UIThread.CheckAccess()) a(); else Dispatcher.UIThread.Post(a); },
            UiAlive = () => PossessionDirector != null,
            MainHost = () => Current?._possessionHost,
            Attribution = h => ((PossessionHost)h).Attribution,
            Pointer = () => Current?._possessionHost is { } h ? (h.Pointer.X, h.Pointer.Y) : (0, 0),
            BoundsOf = (h, el) => ((PossessionHost)h).BoundsOf((Control)el) is { } r ? (r.X, r.Y, r.Width, r.Height) : null,
            IsVisible = el => ((Control)el).IsEffectivelyVisible,
            ParentOf = el => (el as global::Avalonia.Visual)?.GetVisualParent(),
            BarkRung = r => Platform.BarkHead.Engine?.Raise(PossessionBarkTriggers.RungChanged, c => c.Set("rung", r)),
            BarkTimerRestarted = (r, n) => Platform.BarkHead.Engine?.Raise(PossessionBarkTriggers.TimerRestarted, c => c.Set("reason", r).Set("restart", n)),
            BarkTripwire = (k, r, t) => Platform.BarkHead.Engine?.Raise(PossessionBarkTriggers.Tripwire, c => c.Set("kind", k).Set("repeat", r).Set("total", t)),
        });
        PossessionAudio.Install(lockdown, PossessionDirector);
    }
    private void InitializePossessionHost()
    {
        Opened += (_, _) =>
        {
            if (_possessionHost != null || Named<Grid>("RootGrid") is not { } root) return;
            _possessionHost = new PossessionHost(this, root);
            PossessionDirector?.AttachHost(_possessionHost);
        };
        Closed += (_, _) =>
        {
            if (_possessionHost == null) return;
            PossessionDirector?.DetachHost(_possessionHost); _possessionHost.Dispose(); _possessionHost = null;
        };
    }
    internal static void StopPossessionForPanic()
    {
        PossessionDirector?.UndoAll();
        Current?._possessionHost?.Attribution.ReleaseAll();
        PossessionRuntime.UndoAllLive();
    }
    internal static void DisposePossession()
    {
        PossessionAudio.Uninstall(); PossessionDirector?.Dispose(); PossessionDirector = null;
    }
}
