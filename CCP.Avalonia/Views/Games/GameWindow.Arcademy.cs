// Arcademy heartbeat foundation. WPF ArcademyHostService.cs:575-585,619-634,5935-5980.
// WPF ArcademyHostService.cs:176-209, 510-532, 5914-5928.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Arcademy;
using ConditioningControlPanel.Services.Video;
using Avalonia.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Services.Fyp;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        static partial void RegisterArcademy(Dictionary<string, Game> games)
            => games["arcademy"] = new("arcademy", "launcher_game_arcademy_title", "arcademy/index.html", ArcademyGate);

        internal static bool ArcademyGate() => TierGate.DemandLab(Loc.Get("launcher_game_arcademy_title"))
            && !CoreSettings.Current.AudioOnlySession;

        private ArcademyMetaStore? _arcademyMeta;
        private AppSettings? _arcademySettings;
        private MandatoryVideoScheduler? _arcademyVideo;
        private DateTime _arcademyProgress;
        private bool _arcademyInit;

        private void WireArcademy()
        {
            // The page announces ready during module evaluation. The Linux carrier must exist first.
            Web.InjectCarrier = true;
            _arcademyMeta = new ArcademyMetaStore(Post);
            _arcademySettings = CoreSettings.Current;
            _arcademySettings.PropertyChanged += ArcademySettingChanged;
            _arcademyVideo = CoreEngine.Video;
            if (_arcademyVideo != null)
            {
                _arcademyVideo.VideoStarted += ArcademyVideoChanged;
                _arcademyVideo.VideoEnded += ArcademyVideoChanged;
            }
        }

        private void ArcademyVideoChanged() => Dispatcher.UIThread.Post(ArcademyPushSuspension);
        private void ArcademyPushSuspension()
        {
            if (_closing || !IsReady) return;
            bool video = CoreEngine.Video?.IsPlaying == true;
            bool audio = CoreSettings.Current.AudioOnlySession;
            Post(new { type = "suspend", on = video || audio, reason = video ? "video" : "audio-only" });
        }

        private void ArcademySettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AppSettings.AudioOnlySession))
                Dispatcher.UIThread.Post(ArcademyPushSuspension);
            if (e.PropertyName is nameof(AppSettings.MotionLevel) or nameof(AppSettings.GentleVisuals))
                Dispatcher.UIThread.Post(() => { if (!_closing && IsReady) Post(new { type = "setting", key = "motionLevel", value = ArcademyMotion() }); });
        }

        private static int ArcademyMotion() => ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level switch
        { MotionLevel.Off => 0, MotionLevel.Reduced => 1, _ => 2 };

        private object ArcademyInit()
        {
            var s = CoreSettings.Current;
            JObject Parse(string? json) { try { return JObject.Parse(json ?? "{}"); } catch { return new JObject(); } }
            return new {
                type = "init", protocol = 1, platform = new { isTouch = false, hasHaptics = false, host = "desktop" },
                masterIntensity = s.ArcademyMasterIntensity, effectIntensity = s.ChaosEffectIntensity,
                caps = new { flashRate = s.ArcademyCapFlashRate, flashOpacity = s.ArcademyCapFlashOpacity,
                    subDensity = s.ArcademyCapSubDensity, duckDepth = s.ArcademyCapDuckDepth,
                    bubbleRate = s.ArcademyCapBubbleRate, binauralDepth = s.ArcademyCapBinauralDepth, bgIntensity = s.ArcademyCapBgIntensity },
                audioLevels = s.ArcademyAudioLevels, audioMute = s.ArcademyAudioMute,
                masterVolume = Math.Clamp(s.MasterVolume / 100.0, 0, 1), audioOnlySession = s.AudioOnlySession,
                motionLevel = ArcademyMotion(), reducedMotion = ArcademyMotion() != 2,
                utcDateSeed = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                localDate = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                settings = Parse(s.ArcademySettingsJson), keybinds = Parse(s.ArcademyKeybindsJson),
                hideTutorial = s.ArcademyHideTutorial, meta = _arcademyMeta?.Snapshot() ?? new JObject(),
                panicKey = s.PanicKey, panicKeyEnabled = s.PanicKeyEnabled,
            };
        }

        internal static Func<DateTime> ArcademyUtcNow = () => DateTime.UtcNow;
        internal static bool ArcademyRelaunchedOnce;
        private DispatcherTimer? _arcademyWatch;
        private DateTime _arcademyLastBeat;
        private bool _arcademyClassActive, _arcademyRecovering;
        internal bool ArcademyWatchRunning => _arcademyWatch?.IsEnabled == true;

        private void StartArcademyWatch()
        {
            _arcademyLastBeat = _arcademyProgress = ArcademyUtcNow();
            _arcademyWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _arcademyWatch.Tick += (_, _) => {
                var now = ArcademyUtcNow();
                if (CheckArcademyBoot(now)) return;
                CheckArcademyHeartbeat(now);
            };
            _arcademyWatch.Start();
        }

        internal bool CheckArcademyBoot(DateTime now)
        {
            if (_closing || _shellExiting || IsReady || now - _arcademyProgress < TimeSpan.FromSeconds(45)) return false;
            Close();
            return true;
        }

        private void StopArcademyWatch()
        {
            _arcademyWatch?.Stop();
            _arcademyWatch = null;
            if (_arcademySettings != null) _arcademySettings.PropertyChanged -= ArcademySettingChanged;
            if (_arcademyVideo != null) { _arcademyVideo.VideoStarted -= ArcademyVideoChanged; _arcademyVideo.VideoEnded -= ArcademyVideoChanged; }
            _arcademyMeta?.FlushSave();
        }

        partial void HandleArcademyMessage(JObject message, ref bool handled)
        {
            _arcademyProgress = ArcademyUtcNow();
            switch ((string?)message["type"])
            {
                case "set-setting":
                    handled = true;
                    var key = ((string?)message["key"] ?? "").Trim();
                    var value = message["value"];
                    var settings = CoreSettings.Current;
                    object? echo;
                    switch (key)
                    {
                        case "keybinds":
                            if (value is JObject binds && binds.ToString(Formatting.None).Length <= 7000)
                            {
                                settings.ArcademyKeybindsJson = binds.ToString(Formatting.None);
                                echo = binds;
                            }
                            else
                            {
                                // WPF ArcademyHostService.cs:3492-3509 echoes the stored object on refusal.
                                try { echo = JObject.Parse(settings.ArcademyKeybindsJson); }
                                catch { echo = null; }
                            }
                            break;
                        case "audioMute" when value?.Type == JTokenType.Boolean:
                            settings.ArcademyAudioMute = (bool)value;
                            echo = settings.ArcademyAudioMute;
                            break;
                        default:
                            return;
                    }
                    CoreSettings.Save();
                    Post(new { type = "setting", key, value = echo });
                    break;
                case "ready":
                    handled = true;
                    _arcademyLastBeat = ArcademyUtcNow();
                    if (_arcademyInit || _closing) break;
                    _arcademyInit = true;
                    IsReady = true;
                    Post(ArcademyInit());
                    Post(new { type = "fullscreen", on = WindowState == WindowState.FullScreen });
                    ArcademyPushSuspension();
                    Web.FocusPage();
                    break;
                case "meta-command":
                    _arcademyMeta?.Handle(message);
                    handled = true;
                    break;
                case "fullscreen-request":
                    SetPageFullscreen((bool?)message["on"] ?? false);
                    Post(new { type = "fullscreen", on = WindowState == WindowState.FullScreen });
                    handled = true;
                    break;
                case "heartbeat":
                case "pong":
                    _arcademyLastBeat = ArcademyUtcNow();
                    break;
                case "assets-request":
                    PostArcademyAssetsUnavailable(message);
                    handled = true;
                    break;
                case "library-remove":
                    FypRules.RemoveLibrarySub(CoreSettings.Current, (string?)message["name"] ?? (string?)message["sub"]);
                    Post(new { type = "library", subLibrary = FypRules.BuildLibraryPayload(CoreSettings.Current) });
                    handled = true;
                    break;
                case "class-started":
                    _arcademyClassActive = true;
                    handled = true;
                    break;
                case "class-left":
                case "class-ended":
                    _arcademyClassActive = false;
                    handled = true;
                    break;
            }
        }

        // WPF polls only after ready, strictly after 12 seconds in class or 20 on campus.
        internal string CheckArcademyHeartbeat(DateTime now)
        {
            if (_closing || _shellExiting || !IsReady) return "idle";
            if (_arcademyRecovering) return "recovering";
            if ((now - _arcademyLastBeat).TotalSeconds <= (_arcademyClassActive ? 12 : 20)) return "ok";
            _arcademyRecovering = true;
            Dispatcher.UIThread.Post(() =>
            {
                if (_closing || _shellExiting) return;
                bool retry = !ArcademyRelaunchedOnce;
                if (retry) ArcademyRelaunchedOnce = true;
                Close();
                if (retry)
                {
                    try { LaunchWindow(Spec); }
                    catch (Exception ex) { Log.Warning(ex, "Arcademy recovery launch failed"); }
                }
            });
            return "recovering";
        }
    }
}
