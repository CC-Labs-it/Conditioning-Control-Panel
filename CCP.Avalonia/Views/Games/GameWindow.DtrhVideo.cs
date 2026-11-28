using System;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Haptics;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        private void DtrhVideoState(bool on)
        {
            DtrhVideoCovering(on);
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

        private void ReleaseDtrhVideo()
        {
            ApplyDtrhWorldFreeze(false);
            MandatoryVideoOverlay.Instance.SetExternalMute(false);
        }
    }
}
