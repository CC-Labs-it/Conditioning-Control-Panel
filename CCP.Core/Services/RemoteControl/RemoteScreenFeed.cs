using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Remote
{
    /// <summary>
    /// WPF RemoteControlService.Screen.cs:71-317: what the controller's live preview is read from. The
    /// event ring (flash, word, pop, typo, done, count), the controller's own word, the last count result
    /// and the asset counts. The relay opens it for a session and closes it at the end (WPF
    /// Attach/DetachScreenFeeds); a note while closed is dropped. Heads note from wherever they draw.
    /// </summary>
    public static class RemoteScreenFeed
    {
        private const double CountResultShownSeconds = 20.0;
        private static readonly RemoteEventRing Ring = new();
        private static readonly object Gate = new();
        private static volatile bool _open;
        private static string? _word, _videoSeen;
        private static DateTime _wordUtc, _countEndedUtc, _videoSince, _countedUtc = DateTime.MinValue;
        private static bool? _countRight;
        private static int _pictures, _videos, _counting;

        /// <summary>Clock seam.</summary>
        internal static Func<DateTime> Now = () => DateTime.UtcNow;
        /// <summary>The last asset count started (tests await it).</summary>
        internal static Task LastCount { get; private set; } = Task.CompletedTask;

        /// <summary>What only the head can read: Brain Drain, ducking, a web video, idle seconds.</summary>
        public static volatile Action<RemoteScreenInputs>? HeadFill;

        private static long UnixMs => new DateTimeOffset(Now()).ToUnixTimeMilliseconds();

        internal static void Open() { Close(); _open = true; }   // a new session starts with an empty feed

        internal static void Close()
        {
            _open = false;
            Ring.Clear();
            lock (Gate) (_word, _countRight, _videoSeen) = (null, null, null);
        }

        public static void Flash(bool online) => Add(new RemoteScreenEvent("flash", UnixMs, Src: online ? "online" : "local"));
        /// <summary>"pop", "typo" or "done" (a lock card finished).</summary>
        public static void Note(string kind) => Add(new RemoteScreenEvent(kind, UnixMs));

        /// <summary>A subliminal was shown. Only a word the controller sent in the last 5 s travels as text.</summary>
        public static void Word()
        {
            string? text = null;
            lock (Gate)
                if (_word != null && (Now() - _wordUtc).TotalSeconds < 5) (text, _word) = (_word, null);
            Add(new RemoteScreenEvent("word", UnixMs, Text: text));
        }

        /// <summary>WPF NoteControllerWord: trigger_custom_subliminal, the next word shown is the controller's.</summary>
        internal static void ControllerWord(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (Gate) (_word, _wordUtc) = (text.Trim(), Now());
        }

        /// <summary>WPF NoteCountEnded: a bubble count game was answered right or wrong.</summary>
        public static void CountEnded(bool right)
        {
            if (!_open) return;
            lock (Gate) (_countRight, _countEndedUtc) = (right, Now());
            Add(new RemoteScreenEvent("count", UnixMs));
        }

        private static void Add(RemoteScreenEvent e) { if (_open) Ring.Add(e); }

        /// <summary>WPF BuildScreenSafe: null if anything in it throws, the push still goes.</summary>
        internal static JObject? BuildSafe()
        {
            try { return RemoteScreenState.Build(Read()); }
            catch (Exception ex) { Serilog.Log.Debug("[RemoteControl] Screen preview skipped: {Error}", ex.Message); return null; }
        }

        internal static RemoteScreenInputs Read()
        {
            var s = CoreSettings.Current;
            var now = Now();
            var i = new RemoteScreenInputs
            {
                Spiral = s.SpiralEnabled, Pink = s.PinkFilterEnabled,
                Flash = CoreFlash.IsRunning, Subliminal = CoreSubliminal.IsRunning,
                Bubbles = CoreBubbles.IsRunning, Bounce = CoreBouncingText.IsRunning,
                MindWipe = CoreMindWipe.IsRunning, Autonomy = RemoteCommands.AutonomyRunning?.Invoke() == true,
                LockCards = LockCardScheduler.Instance.IsRunning,
                SpiralOpacity = s.SpiralOpacity, PinkOpacity = s.PinkFilterOpacity,
                Easy = RemoteCommands.EasyFactor,
                Events = Ring.Recent(UnixMs),
                VideoKind = CoreEngine.Video?.IsPlaying == true ? "local" : null,
                HapticDevice = CoreHaptics.Service?.IsConnected == true,
                ShareOnlineNames = s.RemoteShareMediaSources,
            };
            HeadFill?.Invoke(i);
            lock (Gate)
            {
                // How long a video has been up is counted from the first preview that saw it.
                if (i.VideoKind != _videoSeen) (_videoSeen, _videoSince) = (i.VideoKind, now);
                if (i.VideoKind != null) i.VideoElapsedMs = (long)(now - _videoSince).TotalMilliseconds;
                if (CoreEngine.BubbleCount?.IsBusy == true) i.CountActive = true;
                else if (_countRight != null && (now - _countEndedUtc).TotalSeconds < CountResultShownSeconds) i.CountRight = _countRight;
            }
            var haptics = RemoteCommands.RemoteHaptics;
            if (haptics.IsPlaying)
            {
                i.HapticLevel = (int)Math.Round(haptics.CurrentLevel * RemoteCommands.EasyFactor);
                i.HapticPattern = haptics.Plan?.IsHold == true ? null : haptics.Plan?.Name;
                i.HapticLoop = haptics.IsLooping;
            }
            if (s.MediaSource != "local" && s.HasRemoteMediaConsent)
                i.OnlineNames = FypOnlineCoordinator.ResolveChannels(s.FypOnlineNiches, s.FypOnlineCustomSubs);
            RefreshMediaCountsIfStale(now);
            (i.Pictures, i.Videos) = (_pictures, _videos);
            return i;
        }

        private static readonly HashSet<string> PictureExts = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" };
        private static readonly HashSet<string> VideoExts = new(StringComparer.OrdinalIgnoreCase)
            { ".mp4", ".webm", ".mkv", ".mov", ".avi", ".m4v", ".wmv" };

        /// <summary>Counts the assets folder off the caller's thread, at most every two minutes. Counts only:
        /// no name leaves this method.</summary>
        private static void RefreshMediaCountsIfStale(DateTime now)
        {
            if ((now - _countedUtc).TotalMinutes < 2 || Interlocked.Exchange(ref _counting, 1) == 1) return;
            string root;
            try { root = CorePaths.EffectiveAssets; }
            catch { _counting = 0; return; }
            LastCount = Task.Run(() =>
            {
                try
                {
                    _pictures = CountFiles(Path.Combine(root, "images"), PictureExts);
                    _videos = CountFiles(Path.Combine(root, "videos"), VideoExts);
                }
                catch { }
                finally { _countedUtc = Now(); _counting = 0; }
            });
        }

        /// <summary>Forget the counts so the next read counts again (a test points the assets elsewhere).</summary>
        internal static void ResetCounts() => (_countedUtc, _pictures, _videos) = (DateTime.MinValue, 0, 0);

        private static int CountFiles(string dir, HashSet<string> exts)
        {
            if (!Directory.Exists(dir)) return 0;
            var n = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                if (exts.Contains(Path.GetExtension(f))) n++;
                if (n >= 100_000) break;
            }
            return n;
        }
    }
}
