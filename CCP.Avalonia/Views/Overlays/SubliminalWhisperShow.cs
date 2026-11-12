using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The head half of WPF <c>SubliminalService.FlashPhrase</c> (:237), <c>TriggerBambiFreeze</c> (:329)
    /// and <c>PlayBambiReset</c> (:428): whisper (with ducking) first, the card
    /// <see cref="SubliminalWhisper.HapticLeadMs"/> + <see cref="SubliminalWhisper.VisualAfterHapticMs"/>
    /// later, and a Reset after a Freeze. Which clip, how loud and when come from Core
    /// <see cref="SubliminalWhisper"/>; this class only plays and draws.
    ///
    /// <para>Haptics through <see cref="CoreHaptics"/> as WPF: HapticLeadMs after the whisper, or
    /// straight away and the card after <c>SubliminalAnticipationMs</c> when nothing is whispered
    /// (TriggerSubliminalWithHapticPattern :563).</para>
    /// <para>A new whisper cuts the previous one, and <see cref="StopAll"/> (Stop, tray Stop everything)
    /// silences one mid-play and drops a card still waiting behind it (WPF StopAudio + the
    /// ShowSubliminalVisuals arrival guard). An audible whisper holds the bark floor for its length
    /// (WPF SubliminalService :543 MarkWhisperAudio). Video Freeze defers Reset until playback ends.</para>
    /// </summary>
    internal static class SubliminalWhisperShow
    {
        private static readonly Random Rng = new();
        private static Action? _stopWhisper;
        private static bool _deferredReset;
        private static int _gen;   // bumped by StopAll: anything scheduled before it is stale

        /// <summary>WPF <c>_audioPath</c>: BaseDirectory/Resources/sub_audio.</summary>
        internal static bool HasRecordedVoice(string text) => FindRecordedVoice(text) != null;

        private static string? FindRecordedVoice(string text) => SubliminalWhisper.FindLinkedAudio(text,
            SubliminalWhisper.ModAudioDir(App.Mods?.ActiveMod?.InstalledPath), SubAudioDir, App.Mods?.ActiveModId);

        internal static string SubAudioDir => Path.Combine(AppContext.BaseDirectory, "Resources", "sub_audio");

        /// <summary>Test seams: the card, and the clock every delay runs on.</summary>
        internal static Action<string> DrawNow = text => Present(text, null);
        internal static Action<string> Draw = text => DrawWithOpacity(text, null);
        /// <summary>The AI command's card (WPF FlashSubliminalCustom) may carry its own opacity.</summary>
        internal static Action<string, int?> DrawWithOpacity = (text, opacity) =>
        {
            var gen = Volatile.Read(ref _gen);
            Dispatcher.UIThread.Post(() =>
            {
                // WPF SubliminalService.cs:612-620: Stop retires arrivals waiting on the UI queue too.
                if (Volatile.Read(ref _gen) != gen) return;
                if (opacity == null) DrawNow(text);
                else Present(text, opacity);
            });
        };

        // Speak uses the one-shot generation, independently of ambient subliminals.
        private static object _speakOwner = new();
        internal static void DrawSpeak(string text)
        {
            var owner = Volatile.Read(ref _speakOwner);
            var gen = Volatile.Read(ref _gen);
            Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(owner, Volatile.Read(ref _speakOwner)) || gen != Volatile.Read(ref _gen)) return;
                SubliminalOverlay.WithOwner(owner, () => DrawNow(text));
            });
        }
        internal static void StopSpeak()
        {
            var owner = Interlocked.Exchange(ref _speakOwner, new object());
            if (Dispatcher.UIThread.CheckAccess()) SubliminalOverlay.CloseOwned(owner);
            else Dispatcher.UIThread.Post(() => SubliminalOverlay.CloseOwned(owner));
        }

        private static void Present(string text, int? opacity)
        {
            if ((global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is { } host)
                SubliminalOverlay.Show(host, text, opacity);
        }

        internal static Action<int, Action> After = (ms, then) => Task.Delay(ms).ContinueWith(_ => then());
        internal static Func<double> Roll = () => { lock (Rng) return Rng.NextDouble(); };

        /// <summary>WPF TearDownSurfaces: silence the whisper and retire every pending card/Reset.</summary>
        internal static void StopAll()
        {
            _deferredReset = false;
            Interlocked.Increment(ref _gen);
            Interlocked.Exchange(ref _stopWhisper, null)?.Invoke();
        }

        /// <summary><see cref="After"/>, dropped when a Stop lands in between.</summary>
        private static void Later(int ms, Action then, int? since = null)
        {
            var gen = since ?? Volatile.Read(ref _gen);
            After(ms, () => { if (Volatile.Read(ref _gen) == gen) then(); });
        }

        /// <summary>WPF FlashPhrase: Core's ambient scheduler calls this for every show.</summary>
        public static void Phrase(string text)
        {
            if (Whisper(text))
            {
                if (SubliminalWhisper.IsFreezePhrase(text)) ScheduleReset();
                CoreProgression.AddXP(20, "Subliminal");
            }
            else
            {
                HapticThenDraw(text);
                CoreProgression.AddXP(10, "Subliminal");
            }
        }

        /// <summary>WPF FlashSubliminalCustom for the AI command: trim, cap, remove tags, then
        /// haptic anticipation and a silent card (10 XP), even when the ambient scheduler is off.</summary>
        internal static void Custom(string text, int? opacity = null)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = text.Trim();
            if (text.Length > 200) text = text[..200];
            text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]*>", "");
            var phrase = text;
            HapticThenDraw(phrase, () => DrawWithOpacity(phrase, opacity));
            CoreProgression.AddXP(10, "Subliminal");
        }

        /// <summary>WPF TriggerBambiFreeze(deferReset: false): the mod's Freeze text, even with subliminals off.</summary>
        public static void Freeze() => Freeze(false);
        internal static void FreezeForVideo() => Freeze(true);
        private static void Freeze(bool deferReset)
        {
            var text = App.Mods?.GetFreezeTriggerText() ?? "Freeze";
            if (Whisper(text))
            {
                if (deferReset) _deferredReset = true; else ScheduleReset();
                Log.Information("Bambi Freeze triggered with audio");
            }
            else
            {
                HapticThenDraw(text);
                Log.Information("Bambi Freeze triggered (no audio file, or whispers muted)");
            }
        }

        // WPF SubliminalService.cs:388-406; cancellation also retires pending audio/cards.
        internal static void FinishVideoFreeze(bool cancelled)
        {
            if (cancelled) { StopAll(); return; }
            if (!_deferredReset) return;
            _deferredReset = false;
            ScheduleReset(deferred: true);
        }

        private static void ScheduleReset(bool deferred = false)
        {
            if (!SubliminalWhisper.ResetFollows(Roll()))
            {
                Log.Debug("Bambi Reset skipped (10% chance roll)");
                return;
            }
            int delay;
            lock (Rng) delay = deferred ? SubliminalWhisper.DeferredResetDelayMs(Rng) : SubliminalWhisper.ResetDelayMs(Rng);
            Later(delay, () =>
            {
                var text = App.Mods?.GetResetTriggerText() ?? "Reset";
                if (!Whisper(text)) HapticThenDraw(text);
                Log.Debug("Bambi Reset triggered after Bambi Freeze");
            });
        }

        /// <summary>Duck, play the phrase's linked clip, then draw the card. False (nothing played) when
        /// there is no clip or whispers are muted: the caller then draws the card straight away.</summary>
        internal static bool Whisper(string text)
        {
            var entry = Volatile.Read(ref _gen);   // a Stop after this drops this whisper and its card
            if (!PlayLinkedClip(text)) return false;
            Later(SubliminalWhisper.HapticLeadMs, () =>
            {
                _ = CoreHaptics.Service?.TriggerSubliminalPatternAsync(text);
                Later(SubliminalWhisper.VisualAfterHapticMs, () => Draw(text), entry);
            }, entry);
            return true;
        }

        /// <summary>WPF SubliminalService.PlayTriggerAudio (:461): the tube's trigger mode voices its
        /// word with the same ducked whisper, without a card or a haptic of its own.</summary>
        internal static void PlayTriggerAudio(string trigger) => PlayLinkedClip(trigger);

        /// <summary>Duck and play the phrase's linked clip; false when there is none or whispers are muted.</summary>
        private static bool PlayLinkedClip(string text)
        {
            var s = CoreSettings.Current;
            var path = FindRecordedVoice(text);
            if (path == null || !s.SubAudioAudible) return false;

            var entry = Volatile.Read(ref _gen);
            if (s.AudioDuckingEnabled) CoreAudio.Duck(s.DuckingLevel);
            var gen = CoreAudio.DuckGeneration;
            Interlocked.Exchange(ref _stopWhisper, null)?.Invoke();   // WPF PlayWhisperAudio: StopAudio first
            _stopWhisper = CoreAudio.PlayStoppable(path, SubliminalWhisper.Volume(s.MasterVolume, s.SubAudioVolume), "whisper",
                onStarted: duration => Platform.BarkHead.Engine?.MarkWhisperAudio(duration.TotalSeconds),
                onFinished: () => After(SubliminalWhisper.UnduckDelayMs, () => CoreAudio.Unduck(gen)));
            if (Volatile.Read(ref _gen) != entry) Interlocked.Exchange(ref _stopWhisper, null)?.Invoke();   // Stop raced the start
            Log.Debug("Playing subliminal audio: {Path}", Path.GetFileName(path));
            return true;
        }

        /// <summary>WPF TriggerSubliminalWithHapticPattern: haptic first, the card after the provider's
        /// spin-up head start (0 with no toy connected).</summary>
        internal static void HapticThenDraw(string text, Action? draw = null)
        {
            var haptics = CoreHaptics.Service;
            _ = haptics?.TriggerSubliminalPatternAsync(text);
            var anticipationMs = haptics?.SubliminalAnticipationMs ?? 0;
            draw ??= () => Draw(text);
            if (anticipationMs > 0) Later(anticipationMs, draw);
            else draw();
        }
    }
}
