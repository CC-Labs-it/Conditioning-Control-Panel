using System;
using ConditioningControlPanel.Avalonia.Views.Windows.EmiDesk;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows.WelcomeShow;

/// <summary>PORTED from ConditioningControlPanel/Services/FirstShow/FirstShowService.cs: one disposable show using the
/// player's real desk EMI. Door: the first-run wizard's far side (<see cref="MainShellWindow.OfferWelcomeShowAfterFirstRun"/>,
/// WPF MainWindow.xaml.cs:546-553); panic ("first-show") and the shell's close stop it. New rule: a stage that cannot be
/// click-through would eat every desktop click (native Wayland), so the show refuses there before EMI is borrowed.
/// Help and EMI book replay use this same service. ponytail: the WPF DEBUG preview launch is not ported.</summary>
internal static class FirstShowService
{
    private static FirstShowStageWindow? _window;
    public static bool IsActive => _window != null;
    internal static FirstShowStageWindow? Window => _window;
    /// <summary>Test seam: headless has no native window to make click-through.</summary>
    internal static bool RequireClickThrough = true;
    /// <summary>Open the show. False when it is refused or could not start.</summary>
    public static bool Open(MainShellWindow? owner = null)
    {
        if (_window != null) { _window.Activate(); return true; }
        FirstShowStageWindow? window = null;
        EmiDeskWindow? desk = null;
        try
        {
            FirstShowStageWindow.SweepTemp();
            window = new FirstShowStageWindow(owner);
            if (!window.Launch() && RequireClickThrough)
            {
                Log.Warning("[FirstShow] not opened: this desktop cannot make the stage click-through");
                window.Close();
                return false;
            }
            desk = EmiDeskService.Instance.BeginPresentation();
            if (desk == null) { window.Close(); return false; }
            _window = window;
            var shown = window;
            window.Closing += (_, _) => desk.EndPresentation();
            window.Closed += (_, _) => { if (ReferenceEquals(_window, shown)) _window = null; };
            desk.BeginPresentation(() => Stop());
            window.BeginEntrance(desk);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[FirstShow] could not open");
            _window = null;
            try { window?.Close(); } catch { /* already down */ }
            desk?.EndPresentation();
            return false;
        }
    }
    /// <summary>Stop the show at once. Idempotent; true when one was running.</summary>
    public static bool Stop()
    {
        if (_window == null) return false;
        var window = _window; _window = null;
        try { window.Close(); } catch (Exception ex) { Log.Debug(ex, "[FirstShow] close failed"); }
        return true;
    }
}
