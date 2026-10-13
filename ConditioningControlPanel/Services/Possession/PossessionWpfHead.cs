using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Possession.Scenes;

namespace ConditioningControlPanel.Services.Possession;

/// <summary>
/// The WPF head's half of the Possession engine. PossessionDirector and PossessionRemember moved to
/// CCP.Core; everything they used to read from App.*, the dispatcher and the visual tree is handed to
/// them here, unchanged, so the WPF haunt behaves exactly as before.
/// </summary>
public static class PossessionWpfHead
{
    public static PossessionDirectorHead CreateDirectorHead()
    {
        var events = new PossessionEvents();
        return new PossessionDirectorHead
        {
            OnUi = a => DispatcherHelper.RunOnUI(a),
            UiAlive = () => Application.Current?.Dispatcher is { HasShutdownStarted: false },
            Settings = () => App.Settings?.Current,
            MainHost = () => App.MainWindowRef as IPossessionHost,
            Attribution = host => new EmberAttribution(host, () => App.Settings?.Current?.LockdownPhotosafe == true),
            Scenes = PossessionSceneCatalog.CreateAll,
            AttachReactive = events.Attach,
            DetachReactive = events.Detach,
            DetachAllReactive = events.DetachAll,
            Pointer = () => (PossessionPointer.Position.X, PossessionPointer.Position.Y),
            BoundsOf = (host, el) =>
            {
                var r = Effects.PossessionVisual.BoundsOf(host, (FrameworkElement)el);
                return r.IsEmpty ? null : (r.X, r.Y, r.Width, r.Height);
            },
            IsVisible = el => ((FrameworkElement)el).IsVisible,
            ParentOf = node => node is not DependencyObject d ? null
                : d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d),
            BarkRung = rung => App.Bark?.NotifyPossessionRung(rung),
            BarkTimerRestarted = (reason, restart) => App.Bark?.NotifyPossessionTimerRestarted(reason, restart),
            BarkEffect = (id, target) => App.Bark?.NotifyPossessionEffect(id, target),
            BarkTripwire = (kind, repeat, total) => App.Bark?.NotifyPossessionTripwire(kind, repeat, total),
            Shake = (intensity, ms) => App.ScreenShake?.Shake(intensity, ms),
        };
    }

    /// <summary>Seed PossessionRemember's head hooks, then install it on the app's lockdown.</summary>
    public static void InstallRemember()
    {
        PossessionRemember.Settings = () => App.Settings?.Current;
        PossessionRemember.StartTimer = (interval, tick) =>
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background, Application.Current.Dispatcher) { Interval = interval };
            timer.Tick += (_, __) => tick();
            timer.Start();
            return new Stopper(timer);
        };
        PossessionRemember.WindowReady = () => App.MainWindowRef is { IsLoaded: true, IsVisible: true };
        PossessionRemember.HasCompanion = () => App.AvatarWindow != null;
        PossessionRemember.ChargeDoor = ChargeDoor;
        PossessionRemember.SayRemembered = SayRemembered;
        PossessionRemember.Install(App.Lockdown);
    }

    private sealed class Stopper(DispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }

    private static bool ChargeDoor()
    {
        var window = App.MainWindowRef;
        if (window == null) return false;
        var door = ResolveDoor(window);
        if (door == null) return false;

        App.Logger?.Information("Possession: spending the remembered charge on {Door}", door.Name);

        // The attribution layer, borrowed for one ripple. The director owns an EmberAttribution per
        // attached host, but it only builds them while it is haunting and it never exposes them, so
        // this makes its own over the same host and drops it. ChargeAsync tears its own overlay down
        // (see EmberAttribution.StartCharge / Finish), so there is nothing to release afterwards.
        if (window is IPossessionHost host)
        {
            var attribution = new EmberAttribution(host, () => App.Settings?.Current?.LockdownPhotosafe == true);
            _ = attribution.ChargeAsync(door, CancellationToken.None, 600);
        }
        return true;
    }

    /// <summary>
    /// The most Lockdown-ish thing currently on screen. The nav rail's Lockdown entry when its door is
    /// open, otherwise the Play door that contains it - charging a control inside a collapsed accordion
    /// (Height 0, IsHitTestVisible false) would produce an ember ripple nobody can see.
    /// </summary>
    private static FrameworkElement? ResolveDoor(MainWindow window)
    {
        foreach (var name in new[] { "BtnNavLockdown", "DoorPlay" })
        {
            try
            {
                if (window.FindName(name) is FrameworkElement fe
                    && fe.IsVisible && fe.ActualWidth > 1 && fe.ActualHeight > 1)
                {
                    return fe;
                }
            }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// One line, in the mod's own voice. The packs own the words (trigger
    /// <see cref="PossessionRemember.RememberTrigger"/>, text-only variants); the hardcoded line below exists only for the
    /// case where there is no bark service at all, exactly like Warden.Say's fallback.
    ///
    /// <para>Goes through BarkService.NotifyPossessionRemember (rule poss_remember in every pack);
    /// the bubble fallback only covers a mod pack without that rule.</para>
    /// </summary>
    private static void SayRemembered()
    {
        try
        {
            if (App.Bark != null && App.Bark.NotifyPossessionRemember()) return;

            App.AvatarWindow?.GigglePriority("i remember.", playSound: false,
                aiGenerated: false, mood: "possessive");
        }
        catch (Exception ex) { App.Logger?.Debug("PossessionRemember line failed: {Error}", ex.Message); }
    }
}
