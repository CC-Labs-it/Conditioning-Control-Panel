using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services.Descent;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private BeatLoop? _candleLoop;
        private bool _candleClosed;
        private bool _candleVisible;
        private readonly ScaleTransform _candleScale = new();

        // WPF AvatarTubeWindow.DescentFuse.cs:38-85: catch up, then follow phases until close.
        private void InitializeDescentFuseCandle()
        {
            var flame = this.FindControl<Path>("FuseCandleFlame")!;
            flame.RenderTransform = _candleScale;
            var fuse = App.DescentCountdown;
            if (fuse == null) return;
            void PhaseChanged(object? sender, DescentFusePhaseChangedEventArgs e) =>
                RunOnAvatar(() => { if (!_candleClosed) ApplyFuseCandlePhase(e.Current); });
            void GateChanged() { if (!_candleClosed && _candleVisible) StartFuseCandleFlicker(); }
            fuse.PhaseChanged += PhaseChanged;
            Env.MotionGateChanged += GateChanged;
            Closed += (_, _) =>
            {
                _candleClosed = true;
                fuse.PhaseChanged -= PhaseChanged;
                Env.MotionGateChanged -= GateChanged;
                StopFuseCandleFlicker();
            };
            ApplyFuseCandlePhase(fuse.LastAnnouncedPhase);
        }

        private void ApplyFuseCandlePhase(DescentFusePhase phase)
        {
            _candleVisible = phase >= DescentFusePhase.Candle && phase < DescentFusePhase.Zero;
            this.FindControl<Grid>("FuseCandleHost")!.IsVisible = _candleVisible;
            if (_candleVisible) StartFuseCandleFlicker();
            else StopFuseCandleFlicker();
        }

        // WPF :93-163: independent autoreversing sine breaths, or a still lit flame.
        private void StartFuseCandleFlicker()
        {
            if (!Env.AllowAmbientLoops) { StopFuseCandleFlicker(); return; }
            if (_candleLoop != null) return;
            var halo = this.FindControl<Ellipse>("FuseCandleHalo")!;
            _candleLoop = new BeatLoop(halo, t =>
            {
                halo.Opacity = 0.34 + 0.28 * BeatLoop.Breath(t, 1.7);
                _candleScale.ScaleY = 0.92 + 0.16 * BeatLoop.Breath(t, 2.3);
            }, TimeSpan.FromSeconds(1.0 / 24));
            _candleLoop.Start();
        }

        private void StopFuseCandleFlicker()
        {
            _candleLoop?.Stop();
            _candleLoop = null;
            this.FindControl<Ellipse>("FuseCandleHalo")!.Opacity = 0.5;
            _candleScale.ScaleX = _candleScale.ScaleY = 1;
        }
    }
}
