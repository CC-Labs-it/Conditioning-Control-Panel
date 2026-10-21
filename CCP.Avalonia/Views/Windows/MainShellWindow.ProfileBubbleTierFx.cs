// Adapted from WPF MainWindow.ProfileBubbleTierFx.cs and parity/trunk 56ab3f850.
using System;
using System.Threading;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private BeatLoop? _profileTierBeat;
        private CancellationTokenSource? _profileTierGrow;
        private Image? ProfileTierBadgeSmall => Named<Image>("ProfileBubbleTierBadge");
        private Popup? ProfileTierBadgePopupHost => Named<Popup>("ProfileTierBadgePopup");
        private Image? ProfileTierBadgeBigImage => Named<Image>("ProfileTierBadgeBig");
        private static readonly (double At, double V)[] ProfileTierWobbleKeys =
        {
            (0, -12), (0.105, -3), (0.245, -18.75), (0.385, -7.95), (0.525, -13.8), (0.7, -12),
        };

        private void EnsureProfileTierFx()
        {
            if (_profileTierBeat != null || ProfileTierBadgeSmall is not { } small || ProfileTierBadgeBigImage is not { } big) return;
            small.RenderTransform = new RotateTransform(-12);
            big.RenderTransform = new ScaleTransform(1, 1);
            // Same eight-second / 700ms WPF wobble, on the existing shared frame clock.
            _profileTierBeat = new BeatLoop(small, seconds =>
            {
                if (small.RenderTransform is RotateTransform tilt)
                    tilt.Angle = seconds < 8 || ProfileTierBadgePopupHost?.IsOpen == true
                        ? -12 : BeatLoop.Keys((seconds - 8) % 8, ProfileTierWobbleKeys);
            });
            AmbientFxCanvas.Env.MotionGateChanged += SyncProfileTierMotion;
            if (ProfileBubblePopupHost is { } menu) menu.Opened += (_, _) =>
            {
                // ponytail: portable native/overlay restack in place of WPF SetWindowPos.
                // A later-opened menu must not cover the badge's enlarged copy.
                Dispatcher.UIThread.Post(() =>
                {
                    if (ProfileTierBadgePopupHost is not { IsOpen: true } popup) return;
                    popup.IsOpen = false;
                    popup.IsOpen = true;
                });
            };
            Closed += (_, _) =>
            {
                AmbientFxCanvas.Env.MotionGateChanged -= SyncProfileTierMotion;
                _profileTierBeat.Stop();
                _profileTierGrow?.Cancel();
                _profileTierGrow?.Dispose();
                _profileTierGrow = null;
                HideProfileTierBig();
            };
        }

        private void SyncProfileTierMotion()
        {
            if (_profileTierBeat == null) return;
            if (AmbientFxCanvas.Env.AllowAmbientLoops && ProfileTierBadgeSmall?.IsVisible == true)
                _profileTierBeat.Start();
            else
            {
                _profileTierBeat.Stop();
                if (ProfileTierBadgeSmall?.RenderTransform is RotateTransform tilt) tilt.Angle = -12;
            }
        }

        private void ProfileTierBadge_MouseEnter(object? sender, PointerEventArgs e)
        {
            if (ProfileTierBadgeSmall is not { IsVisible: true, Source: { } art }) return;
            EnsureProfileTierFx();
            if (ProfileTierBadgePopupHost is not { } popup || ProfileTierBadgeBigImage is not { } big) return;
            big.Source = art;
            popup.HorizontalOffset = -4;
            popup.VerticalOffset = -46;
            popup.IsOpen = true;
            _profileTierGrow?.Cancel();
            _profileTierGrow?.Dispose();
            _profileTierGrow = null;
            if (!AmbientFxCanvas.Env.AllowTransitions)
            {
                big.RenderTransform = new ScaleTransform(1, 1);
                return;
            }
            _profileTierGrow = new CancellationTokenSource();
            // Run on the image: Avalonia's transform animator requires a Visual.
            // ponytail: BackEaseOut has no WPF Amplitude=0.45 knob; exact curve is in lane-b.
            _ = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(260), Easing = new BackEaseOut(),
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(ScaleTransform.ScaleXProperty, 0.35), new Setter(ScaleTransform.ScaleYProperty, 0.35) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(ScaleTransform.ScaleXProperty, 1.0), new Setter(ScaleTransform.ScaleYProperty, 1.0) } },
                },
            }.RunAsync(big, _profileTierGrow.Token);
        }

        private void ProfileTierBadge_MouseLeave(object? sender, PointerEventArgs e) => HideProfileTierBig();

        private void HideProfileTierBig()
        {
            if (ProfileTierBadgePopupHost is { } popup) popup.IsOpen = false;
            _profileTierGrow?.Cancel();
        }
    }
}
