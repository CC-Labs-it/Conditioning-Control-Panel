using System;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;

namespace ConditioningControlPanel.Avalonia.Views.AvatarTube
{
    public partial class AvatarTubeWindow
    {
        private BeatLoop? _floatLoop;
        private readonly TranslateTransform _avatarFloat = new();
        internal Action<double>? FloatingFrame { get; private set; }

        // WPF Windowing.cs:733-755,804-808: legacy avatar bob. Keep this on the image,
        // separate from AvatarBounceHost so a reply's double bounce cannot replace it.
        // Portrait breathing, wobble and mist need the portrait-mode renderer (part B).
        private void StartFloatingAnimation()
        {
            StopFloatingAnimation();
            BeatLoop? loop = null;
            FloatingFrame = seconds =>
            {
                if (_floatLoop != loop) return;
                // WPF Windowing.cs:740-751: +0.05 radians per 16 ms. Sample the same
                // nominal speed on the existing shared 30 Hz beat, without a private timer.
                _avatarFloat.Y = Math.Sin(seconds * (0.05 / 0.016)) * 4;
            };
            _floatLoop = loop = new BeatLoop(this, FloatingFrame);
            loop.Start();
        }

        private void StopFloatingAnimation()
        {
            _floatLoop?.Stop();
            _floatLoop = null;
            FloatingFrame = null;
            _avatarFloat.Y = 0;
        }
    }
}
