using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Serilog;
using Serilog.Events;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>A typed card that never became active (ccp-bugs #1713). On X11, Activate() is a
/// _NET_ACTIVE_WINDOW request KWin's focus-stealing prevention refuses while the user types in
/// another app; the card still draws on top and Deactivated never fires, so keys go elsewhere.
/// WPF forces the foreground (Windows/LockCardWindow.xaml.cs:190-200, :474, :632). Here: re-check
/// for about 2 s and ask the X server directly (XSetInputFocus). Stops once the card has the
/// keyboard, or is completed, closed or panic-closed.</summary>
internal static class KeyboardReclaim
{
    // shortcut: an X11 client under KWin Wayland cannot take focus from a native Wayland app; upgrade
    // to xdg-activation if the card ever goes native Wayland.
    internal static Func<Window, bool> NativeFocus = w => !OperatingSystem.IsWindows() && X11Overlay.ActivateClickThrough(w);
    internal static Func<Window, bool> HasKeyboard = w => w.IsActive;
    internal static TimeSpan Interval = TimeSpan.FromMilliseconds(150);
    internal const int Attempts = 13;

    internal static async void Start(Window window, Func<bool> wanted, Action refocus, string who)
    {
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                await Task.Delay(Interval).ConfigureAwait(false);
                int n = attempt;
                if (await Dispatcher.UIThread.InvokeAsync(() => Step(window, wanted, refocus, who, n))) return;
            }
        }
        catch (Exception ex) { Log.Debug(ex, "{Who}: keyboard reclaim failed", who); }
    }

    /// <summary>One check on the UI thread; true ends the retry.</summary>
    private static bool Step(Window window, Func<bool> wanted, Action refocus, string who, int attempt)
    {
        if (!wanted() || !window.IsVisible) return true;
        bool active = HasKeyboard(window);
        if (active || attempt >= Attempts)
        {
            Log.Write(active ? LogEventLevel.Information : LogEventLevel.Warning,
                "{Who} focus: active={Active} after={Retries} retries", who, active, attempt);
            return true;
        }
        if (!NativeFocus(window)) window.Activate();
        refocus();
        return false;
    }
}
