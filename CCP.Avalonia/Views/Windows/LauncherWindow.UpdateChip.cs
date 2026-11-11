// WPF Windows/Launcher/LauncherWindow.UpdateChip.cs:25-129.
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Theme;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class LauncherWindow
    {
        private readonly ScaleTransform UpdateChipScale = new(1, 1);
        private bool _updateChipShown;
        private int _updateChipGeneration;

        private void HookUpdateChip()
        {
            UpdateChip.RenderTransform = UpdateChipScale;
            AppUpdater.LatestChanged += OnLatestChanged;
            Closed += (_, _) => { AppUpdater.LatestChanged -= OnLatestChanged; _updateChipGeneration++; };
            RefreshUpdateChip();
        }

        private void OnLatestChanged()
        {
            if (Dispatcher.UIThread.CheckAccess()) RefreshUpdateChip();
            else Dispatcher.UIThread.Post(RefreshUpdateChip);
        }

        internal async void RefreshUpdateChip()
        {
            var info = AppUpdater.Latest;
            var show = info?.IsNewer == true && !MainShellWindow.LockdownActive;
            if (info != null) ToolTip.SetTip(UpdateChip, Loc.GetF("launcher_update_tooltip", info.Version));
            if (show == _updateChipShown) return;
            _updateChipShown = show;
            var generation = ++_updateChipGeneration;
            var motion = AmbientFxCanvas.Env.AllowTransitions;
            var gentle = GentleVisuals.On;
            UpdateChip.Transitions = null;
            UpdateChipScale.Transitions = null;
            if (!motion)
            {
                UpdateChip.IsVisible = show;
                UpdateChip.Opacity = 1;
                UpdateChipScale.ScaleX = UpdateChipScale.ScaleY = 1;
                return;
            }
            if (show)
            {
                UpdateChip.IsVisible = true;
                UpdateChip.Opacity = 0;
                UpdateChipScale.ScaleX = UpdateChipScale.ScaleY = gentle ? 1 : 0.8;
            }
            var ms = show ? GentleVisuals.RiseMs(240) : GentleVisuals.FadeMs(160);
            UpdateChip.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(ms), Easing = show ? new QuadraticEaseOut() : new QuadraticEaseIn() } };
            if (!gentle)
            {
                Easing ease = show ? new BackEaseOutAmplitude { Amplitude = 0.4 } : new QuadraticEaseIn();
                var duration = TimeSpan.FromMilliseconds(show ? 240 : 160);
                UpdateChipScale.Transitions = new Transitions {
                    new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = duration, Easing = ease },
                    new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = duration, Easing = ease } };
            }
            UpdateChip.Opacity = show ? 1 : 0;
            UpdateChipScale.ScaleX = UpdateChipScale.ScaleY = show || gentle ? 1 : 0.9;
            if (!show)
            {
                await Task.Delay(ms);
                if (generation == _updateChipGeneration) { UpdateChip.IsVisible = false; UpdateChip.Opacity = 1; }
            }
        }

        private async void UpdateChip_Click(object? sender, RoutedEventArgs e)
        {
            if (MainShellWindow.LockdownActive) { RefreshUpdateChip(); return; }
            if (AppUpdater.Latest is { IsNewer: true } info) await AppUpdater.OfferAsync(this, info);
        }
    }
}
