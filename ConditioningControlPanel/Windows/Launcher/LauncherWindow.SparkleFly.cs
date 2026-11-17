using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Launcher;

/// <summary>The launcher's side of the Sparkles flight: stars land on the wallet chip's star, and
/// the chip's number is held while they do (<see cref="RefreshSpReadout"/> stands aside).</summary>
public partial class LauncherWindow
{
    private bool _spFlightHold;
    private ScaleTransform? _chipStarKick;

    private void InitializeSparkleFly()
    {
        var host = new LauncherSparkleFlightHost(this);
        SparkleFlightDirector.Register(host);
        Closed += (_, _) => SparkleFlightDirector.Unregister(host);
    }

    private sealed class LauncherSparkleFlightHost : ISparkleFlightHost
    {
        private readonly LauncherWindow _w;
        public LauncherSparkleFlightHost(LauncherWindow w) => _w = w;
        public Window Window => _w;
        public Panel? FlightPanel => _w.RootGrid;
        // The launcher waits during Lockdown: nothing flies over the veil.
        public FrameworkElement? WalletStar => _w.WalletChip.IsVisible && !_w.LockdownVeil.IsVisible
            ? SparkleWallet.FindStar(_w.WalletChip) : null;
        public FrameworkElement? OriginFor(SparkleSource source) => null;
        public void HoldCount(int value) => _w.HoldChipCount(value);
        public void ReleaseCount() { _w._spFlightHold = false; _w.RefreshSpReadout(countUp: false); }
        public void Kick(bool settle) => _w.KickChipStar(settle);
    }

    private void HoldChipCount(int value)
    {
        _spFlightHold = true;
        var from = double.IsNaN(_spShown) ? value : _spShown;
        MotionFx.Odometer(SpReadout, from, value, "{0:N0}", 0.12);
        _spShown = value;
    }

    private void KickChipStar(bool settle)
    {
        if (!MotionFx.AllowTransitions || SparkleWallet.FindStar(WalletChip) is not { } star) return;
        if (_chipStarKick == null || !ReferenceEquals(star.RenderTransform, _chipStarKick))
        {
            if (star.RenderTransform is { } t && !t.Value.IsIdentity) return;
            _chipStarKick = new ScaleTransform(1, 1);
            star.RenderTransformOrigin = new Point(0.5, 0.5);
            star.RenderTransform = _chipStarKick;
        }
        var peak = (settle ? 0.45 : 0.22) * (MotionFx.Level == MotionLevel.Reduced ? 0.5 : 1);
        var ms = settle ? 340 : 160;
        var kick = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(ms), FillBehavior = FillBehavior.Stop };
        kick.KeyFrames.Add(new EasingDoubleKeyFrame(1 + peak, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(settle ? 90 : 50)),
            new QuadraticEase { EasingMode = EasingMode.EaseOut }));
        kick.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)),
            new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = settle ? 0.8 : 0.3 }));
        _chipStarKick.BeginAnimation(ScaleTransform.ScaleXProperty, kick);
        _chipStarKick.BeginAnimation(ScaleTransform.ScaleYProperty, kick);
    }
}
