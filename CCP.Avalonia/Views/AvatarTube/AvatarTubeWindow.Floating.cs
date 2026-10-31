using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private DispatcherTimer? _floatTimer;
        private readonly TranslateTransform _avatarFloat = new();
        private double _floatPhase;
        internal Action? FloatingFrame { get; private set; }

        // WPF Windowing.cs:733-755,804-808: legacy avatar bob. Keep this on the image,
        // separate from AvatarBounceHost so a reply's double bounce cannot replace it.
        // Portrait breathing, wobble and mist need the portrait-mode renderer (part B).
        private void StartFloatingAnimation()
        {
            StopFloatingAnimation();
            _floatPhase = 0;
            this.FindControl<Image>("ImgAvatar")!.RenderTransform = _avatarFloat;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _floatTimer = timer;
            FloatingFrame = () =>
            {
                if (_floatTimer != timer) return;
                _floatPhase += 0.05;
                _avatarFloat.Y = Math.Sin(_floatPhase) * 4;
            };
            var frame = FloatingFrame;
            timer.Tick += (_, _) => frame();
            timer.Start();
        }

        private void StopFloatingAnimation()
        {
            _floatTimer?.Stop();
            _floatTimer = null;
            FloatingFrame = null;
            _avatarFloat.Y = 0;
        }
    }
}
