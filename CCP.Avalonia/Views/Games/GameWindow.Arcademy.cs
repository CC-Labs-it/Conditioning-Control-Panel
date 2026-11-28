// Arcademy heartbeat foundation. WPF ArcademyHostService.cs:575-585,619-634,5935-5980.
// WPF ArcademyHostService.cs:176-209, 510-532, 5914-5928.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
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
        // The door, the launcher card and the EMI ring open only once a class can be banked
        // (WPF OnClassEnded, ArcademyHostService.cs:3735; GameWindow.ArcademyResults.cs).
        internal static bool ArcademyExposed => true;
        internal static Game ArcademySpec => new("arcademy", "launcher_game_arcademy_title", "arcademy/index.html", ArcademyGate);

        static partial void RegisterArcademy(Dictionary<string, Game> games)
        {
            if (ArcademyExposed) games["arcademy"] = ArcademySpec;
        }

        internal static bool ArcademyGate()
        {
            if (!TierGate.DemandLab(Loc.Get("launcher_game_arcademy_title"))) return false;
            if (!CoreSettings.Current.AudioOnlySession) return true;
            // WPF ArcademyHostService.cs:204,494-504: a toast, not a modal, and the streak is safe.
            App.Notifications.Show(Loc.Get("arcademy_audio_only_refused"), Helpers.NotificationType.Info, TimeSpan.FromSeconds(7));
            return false;
        }

        private ArcademyMetaStore? _arcademyMeta;
        private AppSettings? _arcademySettings;
        private SettingsService? _arcademySettingsService;
        private MandatoryVideoScheduler? _arcademyVideo;
        private DateTime _arcademyProgress;
        // Starts false: WPF SeedNativeState (ArcademyHostService.cs:544-568) posts nothing when idle.
        private bool _arcademyInit, _arcademyPanicSuspended, _arcademyLastSuspended, _arcademyWriting;
        private string? _arcademyLastSuspendReason;
        private DateTime _arcademyPanicTime;

        // WPF ArcademyHostService.cs:440-491: one press suspends, a second closes.
        internal static bool HandleArcademyPanic(DateTime now)
        {
            GameWindow? w;
            lock (Open) w = Open.FirstOrDefault(x => x.Spec.Id == "arcademy");
            if (w == null) return false;
            // WPF MainWindow.xaml.cs:1729 closes the counter before the game handoff.
            var counter = OpenCounterWindow();
            if (ConditioningControlPanel.Services.Safety.PanicPolicy.CounterClosesBeforeHandOff(counter != null, true))
                counter!.CloseFor(ConditioningControlPanel.Services.PrizeCounter.PrizeCounterRules.ClosePanic);
            // A booting page cannot receive the suspend. Never let it start after panic.
            if (!w.IsReady) { w.Close(); return true; }
            if (w._arcademyPanicSuspended && now - w._arcademyPanicTime <= TimeSpan.FromSeconds(2))
            {
                w.Close();
                return true;
            }
            w._arcademyPanicSuspended = true;
            w._arcademyPanicTime = now;
            w.Post(new { type = "suspend", on = true, reason = "panic" });
            return true;
        }

        private void ArcademyResume(JObject message)
        {
            if (((string?)message["reason"] ?? "panic").Trim() != "panic" || !_arcademyPanicSuspended
                || CoreSettings.Current.AudioOnlySession || CoreEngine.Video?.IsPlaying == true || ArcademyBrowserBusy()) return;
            _arcademyPanicSuspended = false; _arcademyLastSuspended = false;
            _arcademyPanicTime = DateTime.MinValue;
            Post(new { type = "suspend", on = false, reason = "panic" });
        }

        private void WireArcademy()
        {
            // The page announces ready during module evaluation. The Linux carrier must exist first.
            Web.InjectCarrier = true;
            // WPF ArcademyHostService.cs:308,318: own profile, the shell's audio bed starts without a click.
            // Linux adapters have no autoplay-policy switch, so the grant is projected only on Windows.
            Web.WindowsBrowserArguments = "--autoplay-policy=no-user-gesture-required";
            Web.WindowsUserDataFolder = System.IO.Path.Combine(CorePaths.UserData, "browser_data_avalonia_arcademy");
            _arcademyMeta = new ArcademyMetaStore(Post);
            void Changed() => Dispatcher.UIThread.Post(() => { if (!_closing && _arcademyMeta != null) Post(_arcademyMeta.SnapshotMessage()); });
            ArcademySyncService.Attach(_arcademyMeta, Changed);
            ArcademyWalletSyncService.Attach(_arcademyMeta, Changed);
            // WPF ArcademyHostService.cs:262,4361: emitter gated on rung and sign-in, snapshot on online only.
            ArcademyPresenceService.Attach((self, snapshot) => Dispatcher.UIThread.Post(() =>
            {
                if (!_closing) Post(new { type = "presence", self, snapshot });
            }));
            _arcademySettingsService = CoreSettings.Service;
            if (_arcademySettingsService != null) _arcademySettingsService.CurrentReplaced += ArcademySettingsReplaced;
            BindArcademySettings();
            _arcademyVideo = CoreEngine.Video;
            if (_arcademyVideo != null)
            {
                _arcademyVideo.VideoStarted += ArcademyVideoChanged;
                _arcademyVideo.VideoEnded += ArcademyVideoChanged;
            }
        }

        private void BindArcademySettings()
        {
            if (_arcademySettings != null) _arcademySettings.PropertyChanged -= ArcademySettingChanged;
            _arcademySettings = CoreSettings.Current;
            _arcademySettings.PropertyChanged += ArcademySettingChanged;
        }

        private void ArcademySettingsReplaced() => Dispatcher.UIThread.Post(() =>
        {
            if (_closing) return;
            BindArcademySettings();
            ArcademyPushSuspension();
            foreach (var name in new[] { nameof(AppSettings.ArcademyMasterIntensity), nameof(AppSettings.ArcademyCapFlashRate),
                nameof(AppSettings.ArcademyCapFlashOpacity), nameof(AppSettings.ArcademyCapSubDensity), nameof(AppSettings.ArcademyCapDuckDepth),
                nameof(AppSettings.ArcademyCapBubbleRate), nameof(AppSettings.ArcademyCapBinauralDepth), nameof(AppSettings.ArcademyCapBgIntensity),
                nameof(AppSettings.ArcademyAudioMute), nameof(AppSettings.ArcademyHideTutorial), nameof(AppSettings.ArcademyAudioLevels),
                nameof(AppSettings.ChaosEffectIntensity), nameof(AppSettings.MasterVolume), nameof(AppSettings.RemoteMediaRatio),
                nameof(AppSettings.MediaSource), nameof(AppSettings.OfflineMode), nameof(AppSettings.MotionLevel),
                nameof(AppSettings.ProtectBrowserVideoPlayback), nameof(AppSettings.ArcademyPresenceShare) })
                ArcademySettingChanged(_arcademySettings, new PropertyChangedEventArgs(name));
        });

        private void ArcademyVideoChanged() => Dispatcher.UIThread.Post(ArcademyPushSuspension);
        internal void ArcademyPushSuspension()
        {
            if (_closing || !IsReady) return;
            bool video = CoreEngine.Video?.IsPlaying == true || ArcademyBrowserBusy();
            bool audio = CoreSettings.Current.AudioOnlySession;
            if (!video && !audio && _arcademyPanicSuspended) return;
            bool on = video || audio;
            string reason = video ? "video" : "audio-only";
            if (_arcademyLastSuspended == on && (!on || _arcademyLastSuspendReason == reason)) return;
            _arcademyLastSuspended = on; _arcademyLastSuspendReason = reason;
            Post(new { type = "suspend", on, reason });
        }

        private static bool ArcademyBrowserBusy() => CoreSettings.Current.ProtectBrowserVideoPlayback
            && ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.InterruptingBrowserMediaBusy;

        private void ArcademySettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_arcademyWriting) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (_closing || !IsReady) return;
                if (e.PropertyName is nameof(AppSettings.AudioOnlySession) or nameof(AppSettings.ProtectBrowserVideoPlayback)) ArcademyPushSuspension();
                var projection = ArcademyProjectedSetting(CoreSettings.Current, e.PropertyName);
                if (projection.key != null) Post(new { type = "setting", projection.key, projection.value });
            });
        }

        // WPF ArcademyHostService.cs:5873-5895.
        private static (string? key, object? value) ArcademyProjectedSetting(AppSettings s, string? prop) => prop switch
        {
            nameof(AppSettings.ArcademyMasterIntensity) => ("masterIntensity", s.ArcademyMasterIntensity),
            nameof(AppSettings.ArcademyCapFlashRate) => ("caps.flashRate", s.ArcademyCapFlashRate),
            nameof(AppSettings.ArcademyCapFlashOpacity) => ("caps.flashOpacity", s.ArcademyCapFlashOpacity),
            nameof(AppSettings.ArcademyCapSubDensity) => ("caps.subDensity", s.ArcademyCapSubDensity),
            nameof(AppSettings.ArcademyCapDuckDepth) => ("caps.duckDepth", s.ArcademyCapDuckDepth),
            nameof(AppSettings.ArcademyCapBubbleRate) => ("caps.bubbleRate", s.ArcademyCapBubbleRate),
            nameof(AppSettings.ArcademyCapBinauralDepth) => ("caps.binauralDepth", s.ArcademyCapBinauralDepth),
            nameof(AppSettings.ArcademyCapBgIntensity) => ("caps.bgIntensity", s.ArcademyCapBgIntensity),
            nameof(AppSettings.ArcademyAudioMute) => ("audioMute", s.ArcademyAudioMute),
            nameof(AppSettings.ArcademyHideTutorial) => ("hideTutorial", s.ArcademyHideTutorial),
            nameof(AppSettings.ArcademyAudioLevels) => ("audioLevels", s.ArcademyAudioLevels),
            nameof(AppSettings.ChaosEffectIntensity) => ("effectIntensity", s.ChaosEffectIntensity),
            nameof(AppSettings.MasterVolume) => ("masterVolume", Math.Clamp(s.MasterVolume / 100.0, 0, 1)),
            nameof(AppSettings.RemoteMediaRatio) => ("remoteMediaRatio", Math.Clamp(s.RemoteMediaRatio / 100.0, 0, 1)),
            nameof(AppSettings.MediaSource) or nameof(AppSettings.RemoteMediaConsented) or nameof(AppSettings.FypOnlineConsented) =>
                ("remoteMediaEnabled", s.MediaSource != "local" && s.HasRemoteMediaConsent),
            nameof(AppSettings.OfflineMode) => ("offlineMode", s.OfflineMode),
            nameof(AppSettings.MotionLevel) or nameof(AppSettings.GentleVisuals) => ("motionLevel", ArcademyMotion()),
            nameof(AppSettings.ProtectBrowserVideoPlayback) => ("protectBrowserVideo", s.ProtectBrowserVideoPlayback),
            nameof(AppSettings.ArcademyPresenceShare) => ("presenceShare", s.ArcademyPresenceShare),
            _ => (null, null),
        };

        private static int ArcademyMotion() => ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level switch
        { MotionLevel.Off => 0, MotionLevel.Reduced => 1, _ => 2 };

        private object ArcademyInit()
        {
            var s = CoreSettings.Current;
            JObject Parse(string? json) { try { return JObject.Parse(json ?? "{}"); } catch { return new JObject(); } }
            var payday = ArcademyWalletSyncService.ServerPayday ?? ArcademyEconomy.PickPayday(
                DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), _arcademyMeta?.EnrolledGameKeys());
            var (extra, honors) = _arcademyMeta?.LeverUnlocks() ?? (false, false);
            return new {
                type = "init", protocol = 1,
                economy = new { catalog = ArcademyEconomy.CatalogJson(), payday = new { gameKey = payday.GameKey, mult = payday.Mult },
                    leverUnlocks = new { extra, honors }, settingUnlocks = new[] { new { key = "de_board_size", value = "5x5",
                        sku = ArcademyEconomy.SkuDeepEndWideBoard, owned = _arcademyMeta?.WalletOwns(ArcademyEconomy.SkuDeepEndWideBoard) == true } } },
                modId = CoreMods.ActiveModId, lexicon = ArcademyLexicon(), palette = ArcademyModPalette(),
                words = s.SubliminalPool.Where(kv => kv.Value && !string.IsNullOrWhiteSpace(kv.Key)).Select(kv => kv.Key.Trim()).OrderBy(_ => Random.Shared.Next()).Take(60).ToArray(),
                platform = new { isTouch = false, hasHaptics = false, host = "desktop" },
                masterIntensity = s.ArcademyMasterIntensity, effectIntensity = s.ChaosEffectIntensity,
                caps = new { flashRate = s.ArcademyCapFlashRate, flashOpacity = s.ArcademyCapFlashOpacity,
                    subDensity = s.ArcademyCapSubDensity, duckDepth = s.ArcademyCapDuckDepth,
                    bubbleRate = s.ArcademyCapBubbleRate, binauralDepth = s.ArcademyCapBinauralDepth, bgIntensity = s.ArcademyCapBgIntensity },
                audioLevels = s.ArcademyAudioLevels, audioMute = s.ArcademyAudioMute, sfxSamples = ArcademySfxSamples(),
                autoplayOk = OperatingSystem.IsWindows() && Web.WindowsBrowserArguments != null,
                masterVolume = Math.Clamp(s.MasterVolume / 100.0, 0, 1), audioOnlySession = s.AudioOnlySession,
                audioAudible = s.SubAudioAudible, offlineMode = s.OfflineMode, remoteMediaEnabled = s.MediaSource != "local" && s.HasRemoteMediaConsent,
                remoteMediaRatio = Math.Clamp(s.RemoteMediaRatio / 100.0, 0, 1),
                protectBrowserVideo = s.ProtectBrowserVideoPlayback, presenceShare = s.ArcademyPresenceShare,
                motionLevel = ArcademyMotion(), reducedMotion = ArcademyMotion() != 2,
                utcDateSeed = DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                localDate = DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                settings = ArcademySettingsBag(Parse(s.ArcademySettingsJson)), keybinds = Parse(s.ArcademyKeybindsJson),
                hideTutorial = s.ArcademyHideTutorial, meta = _arcademyMeta?.Snapshot() ?? new JObject(),
                panicKey = s.PanicKey, panicKeyEnabled = s.PanicKeyEnabled,
            };
        }

        // WPF ArcademyHostService.cs:1209-1228: names of the bundled flat mp3 samples.
        private static string[] ArcademySfxSamples()
        {
            try
            {
                var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "web", "arcademy", "assets", "sfx");
                return System.IO.Directory.Exists(dir) ? System.IO.Directory.EnumerateFiles(dir, "*.mp3")
                    .Select(System.IO.Path.GetFileNameWithoutExtension).Where(n => !string.IsNullOrWhiteSpace(n))
                    .OrderBy(n => n, StringComparer.Ordinal).ToArray()! : Array.Empty<string>();
            }
            catch (Exception ex) { Log.Debug(ex, "[Arcademy] sound samples unavailable"); return Array.Empty<string>(); }
        }

        // WPF ArcademyHostService.cs:3358-3408: only the active mod's Arcademy resources.
        private static string? ArcademyModFile(string name)
        {
            try
            {
                var root = CoreMods.ActiveModPackage?.InstalledPath;
                if (string.IsNullOrWhiteSpace(root)) return null;
                var path = System.IO.Path.Combine(root, "resources", "arcademy", name);
                return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
            }
            catch (Exception ex) { Log.Debug(ex, "[Arcademy] mod table unavailable"); return null; }
        }

        private static JObject ArcademyLexicon()
        {
            var result = JObject.FromObject(ArcademyNeutralLexicon);
            try
            {
                foreach (var p in JObject.Parse(ArcademyModFile("lexicon.json") ?? "{}").Properties())
                    if (ArcademyNeutralLexicon.ContainsKey(p.Name) && p.Value.Type == JTokenType.String && (string?)p.Value is { Length: > 0 and <= 96 } text && !string.IsNullOrWhiteSpace(text))
                        result[p.Name] = text;
            }
            catch (Exception ex) { Log.Debug(ex, "[Arcademy] invalid lexicon"); }
            return result;
        }

        private static JObject ArcademyModPalette() => ArcademyPalette.Resolve(ArcademyModFile("palette.json"));

        // WPF ArcademyHostService.cs:3444-3615: values are clamped before their echo.
        private object? ApplyArcademySetting(AppSettings s, string key, JToken? value)
        {
            double Num(double fallback)
            {
                var n = (double?)value ?? fallback;
                return double.IsFinite(n) ? n : fallback;
            }
            bool Flag() => (bool?)value ?? false;
            string name = key.StartsWith("caps.", StringComparison.Ordinal) ? key[5..] : key;
            switch (name)
            {
                case "masterIntensity": s.ArcademyMasterIntensity = Num(.7); return s.ArcademyMasterIntensity;
                case "flashRate": s.ArcademyCapFlashRate = Num(1); return s.ArcademyCapFlashRate;
                case "flashOpacity": s.ArcademyCapFlashOpacity = Num(1); return s.ArcademyCapFlashOpacity;
                case "subDensity": s.ArcademyCapSubDensity = Num(1); return s.ArcademyCapSubDensity;
                case "duckDepth": s.ArcademyCapDuckDepth = Num(1); return s.ArcademyCapDuckDepth;
                case "bubbleRate": s.ArcademyCapBubbleRate = Num(1); return s.ArcademyCapBubbleRate;
                case "binauralDepth": s.ArcademyCapBinauralDepth = Num(1); return s.ArcademyCapBinauralDepth;
                case "bgIntensity": s.ArcademyCapBgIntensity = Num(1); return s.ArcademyCapBgIntensity;
                case "effectIntensity": s.ChaosEffectIntensity = Num(.85); return s.ChaosEffectIntensity;
                case "audioMute": s.ArcademyAudioMute = Flag(); return s.ArcademyAudioMute;
                case "hideTutorial": s.ArcademyHideTutorial = Flag(); return s.ArcademyHideTutorial;
                case "presenceShare": s.ArcademyPresenceShare = value?.Type == JTokenType.String ? (string?)value ?? "off" : "off"; return s.ArcademyPresenceShare;
                case "masterVolume": s.MasterVolume = (int)Math.Round(Math.Clamp(Num(.32), 0, 1) * 100); return s.MasterVolume / 100.0;
                case "remoteMediaRatio": s.RemoteMediaRatio = (int)Math.Round(Math.Clamp(Num(.3), 0, 1) * 100); return s.RemoteMediaRatio / 100.0;
            }
            var group = key.StartsWith("audioLevels.", StringComparison.Ordinal) ? key[12..] : key;
            if (AppSettings.DefaultArcademyAudioLevels().ContainsKey(group))
            {
                var levels = s.ArcademyAudioLevels;
                levels[group] = Math.Clamp(Num(.5), 0, AppSettings.ArcademyAudioCeiling(group));
                s.ArcademyAudioLevels = levels;
                return levels[group];
            }
            JObject bag;
            try { bag = JObject.Parse(s.ArcademySettingsJson); } catch { bag = new JObject(); }
            var previous = bag[key]?.DeepClone();
            if (key == "de_board_size" && (value as JValue)?.Value as string == "5x5" && (string?)previous != "5x5"
                && _arcademyMeta?.WalletOwns(ArcademyEconomy.SkuDeepEndWideBoard) != true) return previous;
            if (value == null || value.Type is JTokenType.Object or JTokenType.Array
                || (value.Type == JTokenType.String && ((string?)value)?.Length > 256)
                || (previous == null && bag.Count >= 200)) return previous;
            bag[key] = value.DeepClone();
            bag.Remove("localAssets");
            string json = bag.ToString(Formatting.None);
            if (json.Length > 60000) return previous;
            s.ArcademySettingsJson = json;
            return bag[key];
        }

        internal static Func<DateTime> ArcademyUtcNow = () => DateTime.UtcNow;
        internal static bool ArcademyRelaunchedOnce;
        private DispatcherTimer? _arcademyWatch;
        private DateTime _arcademyLastBeat, _arcademyBeatChecked;
        private bool _arcademyClassActive, _arcademyRecovering;
        internal bool ArcademyWatchRunning => _arcademyWatch?.IsEnabled == true;

        private void StartArcademyWatch()
        {
            _arcademyLastBeat = _arcademyProgress = _arcademyBeatChecked = ArcademyUtcNow();
            _arcademyWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _arcademyWatch.Tick += (_, _) => ArcademyWatchTick(ArcademyUtcNow());
            _arcademyWatch.Start();
        }

        internal string ArcademyWatchTick(DateTime now)
        {
            ArcademyPushSuspension();
            if (CheckArcademyBoot(now)) return "boot";
            // WPF ArcademyHostService.cs:5939 polls the heartbeat every 5 s; only suspension polls faster.
            if (now - _arcademyBeatChecked < TimeSpan.FromSeconds(5)) return "wait";
            _arcademyBeatChecked = now;
            return CheckArcademyHeartbeat(now);
        }

        internal bool CheckArcademyBoot(DateTime now)
        {
            if (_closing || _shellExiting || IsReady || now - _arcademyProgress < TimeSpan.FromSeconds(45)) return false;
            ArcademyBootFailed("boot deadline: no progress for 45s");
            return true;
        }

        internal static Action<string> ShowArcademyBootError = message =>
            _ = new Dialogs.MessageDialog(Loc.Get("launcher_game_arcademy_title"), message, showCancel: false).ShowDialogSafe<bool?>(null);

        // WPF ArcademyHostService.cs:5986-6005: tear down, then say so; a black window is the worse failure.
        private void ArcademyBootFailed(string? msg)
        {
            Close();
            ShowArcademyBootError(Loc.GetF("arcademy_boot_error_body", Loc.Get("launcher_game_arcademy_title"), msg ?? ""));
        }

        private void StopArcademyWatch()
        {
            _arcademyWatch?.Stop();
            _arcademyWatch = null;
            if (_arcademySettings != null) _arcademySettings.PropertyChanged -= ArcademySettingChanged;
            if (_arcademySettingsService != null) _arcademySettingsService.CurrentReplaced -= ArcademySettingsReplaced;
            if (_arcademyVideo != null) { _arcademyVideo.VideoStarted -= ArcademyVideoChanged; _arcademyVideo.VideoEnded -= ArcademyVideoChanged; }
            ArcademyClassLeft();
            ArcademySyncService.Detach();
            ArcademyWalletSyncService.Detach();
            ArcademyPresenceService.Detach();   // WPF ArcademyHostService.cs:6106
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
                    _arcademyWriting = true;
                    try
                    {
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
                            // The page queues settings until init (arcademy/bridge.js:114-128).
                            if (!IsReady || key.Length == 0 || key.Length > 64) return;
                            try { echo = ApplyArcademySetting(settings, key, value); }
                            catch (Exception ex) { Log.Debug(ex, "[Arcademy] invalid setting {Key}", key); return; }
                            break;
                    }
                    }
                    finally { _arcademyWriting = false; }
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
                case "resume-request":
                    ArcademyResume(message);
                    handled = true;
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
                case "local-sample-request":
                    ArcademyLocalSample(message);
                    handled = true;
                    break;
                case "probe-sub":
                    ArcademyProbeSub(message);
                    handled = true;
                    break;
                case "share-image":
                    ArcademyShareImage((string?)message["png"]);
                    handled = true;
                    break;
                case "library-remove":
                    FypRules.RemoveLibrarySub(CoreSettings.Current, (string?)message["name"] ?? (string?)message["sub"]);
                    Post(new { type = "library", subLibrary = FypRules.BuildLibraryPayload(CoreSettings.Current) });
                    handled = true;
                    break;
                case "enrollment-done":
                    ArcademyEnroll(message);
                    handled = true;
                    break;
                case "prize-buy":
                    ArcademyBuy(message);
                    handled = true;
                    break;
                case "class-started":
                    ArcademyClassStarted(message);
                    ArcademyPresenceService.NoteRoomEnter((string?)message["gameKey"]);   // WPF ArcademyHostService.cs:618
                    _arcademyClassActive = true;
                    handled = true;
                    break;
                case "class-left":
                    ArcademyClassLeft();
                    _arcademyClassActive = false;
                    handled = true;
                    break;
                case "class-ended":
                    ArcademyClassEnded(message);
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
