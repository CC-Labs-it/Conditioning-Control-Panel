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
        // Portrait breathing, wobble and mist ride the same frame (Portrait.cs PortraitMotion).
        private void StartFloatingAnimation()
        {
            StopFloatingAnimation();
            _floatWanted = true;
            HookFloatGate();
            // Owner decision (lane perf, 2026-11-06): on the CPU path (the lighter-effects tier) the idle bob
            // holds still; each bob frame repaints the whole tube window there. Speech, bounces and the other
            // animation states are not this loop and keep playing. With a GPU it is WPF's ungated bob.
            if (global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.SoftwareFloorHoldsIdleLoops) return;
            BeatLoop? loop = null;
            FloatingFrame = seconds =>
            {
                if (_floatLoop != loop) return;
                // WPF Windowing.cs:740-751: +0.05 radians per 16 ms. Sample the same
                // nominal speed on the existing shared 30 Hz beat, without a private timer.
                _avatarFloat.Y = Math.Sin(seconds * (0.05 / 0.016)) * 4;
                if (IsPortraitMode) PortraitMotion(seconds);
            };
            _floatLoop = loop = new BeatLoop(this, FloatingFrame);
            loop.Start();
        }

        private BeatLoop? _speakLoop;

        /// <summary>WPF animates speech on its ungated float timer (Windowing.cs:765-776). While the owner gate
        /// holds the idle loop, a playing portrait clip runs its own loop for just the clip's length.</summary>
        private void StartSpeakingMotion()
        {
            var clip = _clipPlaying;
            if (clip.IsCompleted || _floatLoop != null || !_floatWanted || !IsPortraitMode) return;
            _speakLoop?.Stop();
            BeatLoop? loop = null;
            _speakLoop = loop = new BeatLoop(this, seconds =>
            {
                if (_speakLoop != loop) return;
                if (!IsPortraitMode) { StopSpeakingMotion(); return; }   // left portrait mid-clip: nothing left to move
                PortraitMotion(seconds);
                if (!clip.IsCompleted) return;
                StopSpeakingMotion();   // the clip ended: this frame drew the idle mist, and she settles
                ResetPortraitMotion();
            });
            loop.Start();
        }

        private void StopSpeakingMotion() { _speakLoop?.Stop(); _speakLoop = null; }

        private void StopFloatingAnimation()
        {
            StopSpeakingMotion();
            _floatWanted = false;
            _floatLoop?.Stop();
            _floatLoop = null;
            FloatingFrame = null;
            _avatarFloat.Y = 0;
            ResetPortraitMotion(); // WPF Windowing.cs:809-817
        }

        /// <summary>The bob was asked for (shown and attached), whether or not it is moving right now.</summary>
        private bool _floatWanted;
        private bool _floatGateHooked;

        /// <summary>True while the idle bob is actually moving (tests).</summary>
        internal bool IsFloating => _floatLoop != null;

        private void HookFloatGate()
        {
            if (_floatGateHooked) return;
            _floatGateHooked = true;
            global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.MotionGateChanged += OnFloatGate;
            Closed += (_, _) => global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.MotionGateChanged -= OnFloatGate;
        }

        /// <summary>Settings ▸ Performance ▸ Lighter effects (or the renderer probe) moved: hold or resume the bob.</summary>
        private void OnFloatGate()
        {
            if (!_floatWanted) return;
            bool hold = global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.SoftwareFloorHoldsIdleLoops;
            if (hold && _floatLoop != null)
            {
                _floatLoop.Stop();
                _floatLoop = null;
                FloatingFrame = null;
                _avatarFloat.Y = 0;
                ResetPortraitMotion();
                StartSpeakingMotion();   // WPF Windowing.cs:765-776: a clip already playing keeps her talking
            }
            else if (!hold && _floatLoop == null) StartFloatingAnimation();
        }
    }
}
