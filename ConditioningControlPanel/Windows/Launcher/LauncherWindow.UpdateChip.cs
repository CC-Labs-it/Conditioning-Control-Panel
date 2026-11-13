using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// The "Update ready" chip (ccp-bugs #1580). The update dialog waits on the startup ladder until the
/// panel opens, so a player who goes straight from the launcher into a game never saw it. The chip
/// lights when <see cref="App.PendingUpdate"/> is set and opens the SAME in-app dialog and updater
/// (<see cref="App.OfferPendingUpdate"/>), never a browser URL.
///
/// <para>Juice: IN = fade plus a small pop, OUT = fade plus a small shrink, then collapse. Gentle
/// visuals keeps the fades (300 ms rise floor) and drops the scale; Motion Off snaps. Hidden under
/// Lockdown: an install exits the app, and Lockdown promises there is no exit.</para>
/// </summary>
public partial class LauncherWindow
{
    private const int UpdateChipInMs = 240;
    private const int UpdateChipOutMs = 160;
    private bool _updateChipShown;

    private void OnPendingUpdateChanged()
    {
        if (Dispatcher.CheckAccess()) RefreshUpdateChip();
        else Dispatcher.BeginInvoke(new Action(RefreshUpdateChip));
    }

    /// <summary>Show or hide the chip to match the pending update and Lockdown. Idempotent.</summary>
    private void RefreshUpdateChip()
    {
        try
        {
            var info = App.PendingUpdate;
            bool show = info != null && App.Lockdown?.IsActive != true;
            if (info != null)
                UpdateChip.ToolTip = Loc.GetF("launcher_update_tooltip", info.Version);

            if (show == _updateChipShown) return;
            _updateChipShown = show;
            if (show) UpdateChipIn();
            else UpdateChipOut();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] update chip refresh failed"); }
    }

    private void UpdateChipIn()
    {
        UpdateChip.Visibility = Visibility.Visible;
        if (!MotionFx.AllowTransitions)
        {
            UpdateChip.BeginAnimation(OpacityProperty, null);
            UpdateChip.Opacity = 1;
            SetChipScale(1);
            return;
        }

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(GentleVisuals.RiseMs(UpdateChipInMs)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        };
        UpdateChip.BeginAnimation(OpacityProperty, fade);

        if (GentleVisuals.On) { SetChipScale(1); return; }
        var pop = new DoubleAnimation(0.8, 1, TimeSpan.FromMilliseconds(UpdateChipInMs))
        {
            EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut },
        };
        UpdateChipScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        UpdateChipScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    private void UpdateChipOut()
    {
        if (!MotionFx.AllowTransitions || !UpdateChip.IsVisible)
        {
            UpdateChip.BeginAnimation(OpacityProperty, null);
            UpdateChip.Visibility = Visibility.Collapsed;
            UpdateChip.Opacity = 1;
            SetChipScale(1);
            return;
        }

        var fade = new DoubleAnimation(UpdateChip.Opacity, 0, TimeSpan.FromMilliseconds(GentleVisuals.FadeMs(UpdateChipOutMs)))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        fade.Completed += (_, _) =>
        {
            if (_updateChipShown) return;   // lit again mid-fade: the IN owns it now
            UpdateChip.BeginAnimation(OpacityProperty, null);
            UpdateChip.Visibility = Visibility.Collapsed;
            UpdateChip.Opacity = 1;
            SetChipScale(1);
        };
        UpdateChip.BeginAnimation(OpacityProperty, fade);

        if (GentleVisuals.On) return;
        var shrink = new DoubleAnimation(1, 0.9, TimeSpan.FromMilliseconds(UpdateChipOutMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
        };
        UpdateChipScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        UpdateChipScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    private void SetChipScale(double value)
    {
        UpdateChipScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        UpdateChipScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        UpdateChipScale.ScaleX = UpdateChipScale.ScaleY = value;
    }

    /// <summary>The same dialog as the ladder: Update now runs the in-app installer, Later skips this
    /// version for 24 h. The launcher owns the dialog so it opens where the player is.</summary>
    private void UpdateChip_Click(object sender, RoutedEventArgs e)
    {
        LauncherSfx.Click();
        if (App.Lockdown?.IsActive == true) { RefreshUpdateChip(); return; }
        try { App.OfferPendingUpdate(this); }
        catch (Exception ex) { Log.Warning(ex, "[Launcher] update dialog failed to open"); }
    }
}
