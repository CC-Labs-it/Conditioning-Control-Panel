using System;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Haptics;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        private bool _dtrhFrozen;
        private MandatoryVideoScheduler? _dtrhVideo;

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            if (!IsDtrh) return;
            _dtrhVideo = CoreEngine.Video;
            if (_dtrhVideo == null) return;
            _dtrhVideo.VideoStarted += DtrhVideoStarted;
            _dtrhVideo.VideoEnded += DtrhVideoEnded;
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            if (_dtrhVideo != null)
            {
                _dtrhVideo.VideoStarted -= DtrhVideoStarted;
                _dtrhVideo.VideoEnded -= DtrhVideoEnded;
                _dtrhVideo = null;
            }
            base.OnUnloaded(e);
        }

        private void DtrhVideoStarted() => DtrhVideoState(true);
        private void DtrhVideoEnded() => DtrhVideoState(false);
        private void DtrhVideoState(bool on)
        {
            DtrhHapticDirector.OnVideoCovering(on);
            Dispatcher.UIThread.Post(() =>
            {
                if (_closing) return;
                Post(new { type = "payload-state", kind = "video", on });
                if (!on) Web.FocusPage();
            });
        }

        private static void FireDtrhPayload(JObject o)
        {
            if (!DtrhNativePayloadRule.Accepts((string?)o["kind"])) return;
            var payload = EffectPayloadFactory.Build(EffectBubblePayloadKind.Audio);
            payload.Strength = Math.Clamp((int?)o["strength"] ?? 60, 0, 100);
            payload.DurationMult = Math.Clamp((double?)o["durationMult"] ?? 1.0, 0.1, 10.0);
            payload.Fire();
        }

        private void ApplyDtrhFreeze(bool on)
        {
            if (_dtrhFrozen == on) return;
            _dtrhFrozen = on;
            DtrhHapticDirector.OnWorldFreeze(on);
            if (on) MandatoryVideoOverlay.Instance.PausePrimary();
            else MandatoryVideoOverlay.Instance.PlayPrimary();
        }

        private void ReleaseDtrhVideo()
        {
            ApplyDtrhFreeze(false);
            MandatoryVideoOverlay.Instance.SetExternalMute(false);
        }
    }
}
