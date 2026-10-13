using System;
using System.Threading;
using System.Windows;

namespace ConditioningControlPanel.Services.Possession;

/// <summary>
/// The WPF side of "it remembers" (rules, flag and clock: CCP.Core PossessionRemember): the main
/// window's readiness and the charge itself (ember ripple on the Lockdown door, then one line).
/// </summary>
public static class PossessionRememberCharge
{
    /// <summary>The main window is loaded and visible.</summary>
    public static bool IsWindowReady() => App.MainWindowRef is { IsLoaded: true, IsVisible: true };

    /// <summary>The charge, on the UI thread. Core has already ruled out a running lockdown.</summary>
    public static void Fire()
    {
        try
        {
            var window = App.MainWindowRef;
            if (window == null) return;

            // No tube, no line, no point: the charge alone reads as a glitch rather than a message.
            if (App.AvatarWindow == null) return;

            var door = ResolveDoor(window);
            if (door == null) return;

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

            SayRemembered();
        }
        catch (Exception ex) { App.Logger?.Warning("PossessionRemember fire failed: {Error}", ex.Message); }
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
    /// <see cref="PossessionBarkTriggers.Remember"/>, text-only variants); the hardcoded line below exists
    /// only for the case where there is no bark service at all, exactly like Warden.Say's fallback.
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
