// Arcademy host: registration, launch gate, init and meta. WPF ArcademyHostService.cs:157-260,
// 510-535,575-700,708-805; heartbeat WPF ArcademyHostService.cs:575-585,619-634,5935-5980.
using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Arcademy;
using ConditioningControlPanel.Services.Fyp;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        internal static Func<DateTime> ArcademyUtcNow = () => DateTime.UtcNow;
        internal static bool ArcademyRelaunchedOnce;
        private DispatcherTimer? _arcademyWatch;
        private DateTime _arcademyLastBeat;
        private bool _arcademyClassActive, _arcademyRecovering;
        internal bool ArcademyWatchRunning => _arcademyWatch?.IsEnabled == true;
        private ArcademyMetaStore? _arcademyMeta;
        private bool _arcademyInitPosted;

        /// <summary>The page's meta blob (WPF ArcademyHostService.cs:226 mints it per launch); tests point it at a temp file.</summary>
        internal static Func<Action<object>, ArcademyMetaStore> NewArcademyMeta = post => new ArcademyMetaStore(post);

        /// <summary>The audio-only refusal toast (WPF ArcademyHostService.cs:497-508); tests capture it.</summary>
        internal static Action<string> ArcademyNotice = text =>
            App.Notifications.Show(text, Helpers.NotificationType.Info, TimeSpan.FromSeconds(7));

        // WPF LauncherCatalogue.cs:270-273 -> ArcademyHostService.Launch (:157): DoorAvailable is true (:153),
        // so the door exists; the window opens on the shipped page at Resources/web/arcademy.
        static partial void RegisterArcademy(Dictionary<string, Game> games) =>
            games["arcademy"] = new("arcademy", "launcher_game_arcademy_title", "arcademy/index.html", ArcademyGate);

        /// <summary>WPF ArcademyHostService.cs:186-201: the Lab bar, then the audio-only refusal. LaunchWindow
        /// refocuses a live window before it runs, as WPF's idempotent step 1 does.</summary>
        internal static bool ArcademyGate()
        {
            if (!TierGate.DemandLab(Loc.Get("launcher_game_arcademy_title"))) return false;
            if (!CoreSettings.Current.AudioOnlySession) return true;
            Log.Information("Arcademy launch refused: AudioOnlySession is active");
            ArcademyNotice(Loc.Get("arcademy_audio_only_refused"));
            return false;
        }

        private void StartArcademyWatch()
        {
            _arcademyLastBeat = ArcademyUtcNow();
            _arcademyWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _arcademyWatch.Tick += (_, _) => CheckArcademyHeartbeat(ArcademyUtcNow());
            _arcademyWatch.Start();
        }

        private void StopArcademyWatch()
        {
            _arcademyWatch?.Stop();
            _arcademyWatch = null;
            // WPF DisposeAll flushes the debounced meta write on every close path, panic included.
            try { _arcademyMeta?.FlushSave(); }
            catch (Exception ex) { Log.Warning(ex, "Arcademy meta flush failed"); }
            _arcademyMeta = null;
        }

        partial void HandleArcademyMessage(JObject message, ref bool handled)
        {
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
                    // WPF OnPageReady (:510-535): exactly one Arcademy init per boot, then the window mode.
                    _arcademyLastBeat = ArcademyUtcNow();
                    IsReady = true;
                    handled = true;
                    if (_arcademyInitPosted) break;
                    _arcademyInitPosted = true;
                    Post(ArcademyInit());
                    Post(new { type = "fullscreen", on = WindowState == WindowState.FullScreen });
                    // WPF SeedNativeState (:545-560): a flip during boot is seeded as a suspend.
                    if (CoreSettings.Current.AudioOnlySession) Post(new { type = "suspend", on = true, reason = "audio-only" });
                    break;
                case "heartbeat":
                case "pong":
                    _arcademyLastBeat = ArcademyUtcNow();
                    break;
                case "meta-command":
                    // WPF OnPageMessage (:594): the store answers every get/set/merge with the stored value.
                    ArcademyMeta().Handle(message);
                    handled = true;
                    break;
                case "fullscreen-request":
                    // WPF ApplyHostFullscreen (:684-697): the host owns the mode and echoes the result.
                    SetPageFullscreen((bool?)message["on"] ?? false);
                    Post(new { type = "fullscreen", on = WindowState == WindowState.FullScreen });
                    handled = true;
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

        private ArcademyMetaStore ArcademyMeta() => _arcademyMeta ??= NewArcademyMeta(msg => Dispatcher.UIThread.Post(() => { if (!_closing) Post(msg); }));

        /// <summary>WPF BuildInit (:708-805), the fields this head can back. Media, sub audio, mods, presence,
        /// the wallet and the Discord photo are not hosted here, so they go out resolved to off or empty.</summary>
        internal object ArcademyInit()
        {
            var s = CoreSettings.Current;
            var levels = s.ArcademyAudioLevels ?? ConditioningControlPanel.Models.AppSettings.DefaultArcademyAudioLevels();
            double Level(string group, double fallback) => levels.TryGetValue(group, out var v) && double.IsFinite(v)
                ? Math.Clamp(v, 0.0, ConditioningControlPanel.Models.AppSettings.ArcademyAudioCeiling(group)) : fallback;
            JObject? Parse(string? json) { try { return string.IsNullOrWhiteSpace(json) ? null : JObject.Parse(json); } catch { return null; } }
            var settings = Parse(s.ArcademySettingsJson) ?? new JObject();
            settings["remoteMediaEnabled"] = false;
            var motion = s.MotionLevel;
            var now = DateTime.Now;
            return new
            {
                type = "init",
                protocol = 1,
                platform = new { isTouch = false, hasHaptics = false, host = "desktop" },
                modId = (string?)null,
                lexicon = new JObject(),
                palette = ArcademyPalette.Resolve(null),
                masterIntensity = s.ArcademyMasterIntensity,
                caps = new
                {
                    flashRate = s.ArcademyCapFlashRate, flashOpacity = s.ArcademyCapFlashOpacity,
                    subDensity = s.ArcademyCapSubDensity, duckDepth = s.ArcademyCapDuckDepth,
                    bubbleRate = s.ArcademyCapBubbleRate, binauralDepth = s.ArcademyCapBinauralDepth,
                    bgIntensity = s.ArcademyCapBgIntensity,
                },
                effectIntensity = s.ChaosEffectIntensity,
                audioLevels = new { fx = Level("fx", 0.48), voice = Level("voice", 0.85), tutorial = Level("tutorial", 0.85), drops = Level("drops", 0.4), music = Level("music", 1.0) },
                audioMute = s.ArcademyAudioMute,
                autoplayOk = false,
                sfxSamples = Array.Empty<string>(),
                masterVolume = Math.Clamp(s.MasterVolume / 100.0, 0.0, 1.0),
                remoteMediaEnabled = false,
                remoteMediaRatio = Math.Clamp(s.RemoteMediaRatio / 100.0, 0.0, 1.0),
                offlineMode = s.OfflineMode,
                audioAudible = false,
                audioOnlySession = false,
                protectBrowserVideo = s.ProtectBrowserVideoPlayback,
                motionLevel = motion switch { ConditioningControlPanel.Models.MotionLevel.Off => 0, ConditioningControlPanel.Models.MotionLevel.Reduced => 1, _ => 2 },
                performanceMode = false,
                reducedMotion = motion != ConditioningControlPanel.Models.MotionLevel.Full,
                words = Array.Empty<string>(),
                triggers = Array.Empty<object>(),
                houseTriggers = Array.Empty<object>(),
                utcDateSeed = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                localDate = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                overrideCalendar = (object?)null,
                meta = ArcademyMeta().Snapshot(),
                settings,
                keybinds = Parse(s.ArcademyKeybindsJson),
                hideTutorial = s.ArcademyHideTutorial,
                presenceShare = "off",
                panicKeyEnabled = s.PanicKeyEnabled,
                panicKey = s.PanicKey ?? "Escape",
                devDoor = false,
                profile = new { name = string.IsNullOrWhiteSpace(CoreAccount.DisplayName) ? null : CoreAccount.DisplayName!.Trim(), avatarUrl = (string?)null, discordLinked = false, presenceShare = "off" },
                economy = new { catalog = new JArray(), payday = new { gameKey = (string?)null, mult = 1 }, leverUnlocks = new { extra = false, honors = false }, settingUnlocks = new JArray() },
            };
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
