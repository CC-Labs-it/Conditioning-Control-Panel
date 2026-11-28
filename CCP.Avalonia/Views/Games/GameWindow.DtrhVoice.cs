using System;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        private bool _dtrhVnSpeaking;
        internal Action<string, float> DtrhPlaySfx = PlayDtrhSfx;

        private static void PlayDtrhSfx(string name, float scale)
        {
            if (name == "wave_clear") ChaosOverlayWindow.ChaosSfx.PlayWaveClear();
            else if (name == "ripple_cast") ChaosOverlayWindow.ChaosSfx.PlayRippleCast();
            else ChaosOverlayWindow.ChaosSfx.Play(name, scale);
        }

        // WPF DtrhHostService.cs:329-342,360-371,386-390.
        private bool ObserveDtrhVoice(JObject o)
        {
            if (_closing) return false; // HandleDtrh retains its closed-window guard.
            try
            {
                switch ((string?)o["type"])
                {
                    case "run-started": _dtrhVnSpeaking = false; return false;
                    case "vn-speaking": _dtrhVnSpeaking = (bool?)o["on"] ?? false; return true;
                    case "bark":
                        // The host taps haptics before the VN gate, even for haptics-only events.
                        if (HandleDtrh(o)) return true;
                        if (!_dtrhVnSpeaking && DtrhBarkRoute.Route(o) is { } context)
                            BarkHead.Engine?.Raise(context.Trigger, c => { foreach (var pair in context.Values) c.Set(pair.Key, pair.Value); });
                        return true;
                    case "sfx":
                        if (_dtrhVnSpeaking) return true;
                        var name = (string?)o["name"];
                        if (!string.IsNullOrEmpty(name)) DtrhPlaySfx(name, (float?)o["scale"] ?? 0.6f);
                        return true;
                    default: return false;
                }
            }
            catch (Exception ex) { Log.Debug("DtrhHost voice feed failed: {E}", ex.Message); return true; }
        }
    }
}
