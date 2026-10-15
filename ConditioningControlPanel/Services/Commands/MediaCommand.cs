using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Logging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.CommandData;
using ConditioningControlPanel.Services.Browser;

namespace ConditioningControlPanel.Services.Commands
{
    /// <summary>
    /// Plays a video or audio file from inside the user's assets root. Path is normalized
    /// and rejected if it escapes the assets directory after resolution. The AI doesn't
    /// know which files actually exist on disk. A request that names nothing is a random pick
    /// of the right kind. A named video plays the closest local match by name
    /// (<see cref="VideoTitleMatcher"/>), or opens a HypnoTube link in the browser takeover, or
    /// plays nothing (see <see cref="Decide"/>); a named audio still falls back to a random one.
    /// </summary>
    public class MediaCommand : ICommand
    {
        private readonly Media _data;
        private readonly AICommandType _kind;

        public MediaCommand(Media data, AICommandType kind = AICommandType.video)
        {
            _data = data;
            _kind = kind;
        }

        /// <summary>What an AI media request plays.</summary>
        internal enum MediaPick { Random, Named, HypnoTube, Nothing }

        /// <summary>Pure decision (ccp-bugs #1325, #1330). An explicit Random, or a request that
        /// names nothing at all ("play any video"), is a random pick. A request that NAMES
        /// something (a Title or a Path) plays the local file it resolves to: the path itself, or
        /// for a video the closest library match by name (<paramref name="namedFilePlayable"/>).
        /// A named video with no local match opens its HypnoTube link in the browser takeover when
        /// Path or Title is one (<paramref name="hypnoTubeLink"/>). Otherwise a video request plays
        /// NOTHING: the chat text was about that one video, and a random local file in its place is
        /// the unrelated video #1325 describes. Audio keeps its random fallback.</summary>
        internal static MediaPick Decide(Media data, AICommandType kind, bool namedFilePlayable, bool hypnoTubeLink = false)
        {
            if (data.Random) return MediaPick.Random;
            var named = !string.IsNullOrWhiteSpace(data.Path) || !string.IsNullOrWhiteSpace(data.Title);
            if (!named) return MediaPick.Random;
            if (namedFilePlayable) return MediaPick.Named;
            if (kind == AICommandType.audio) return MediaPick.Random;
            return hypnoTubeLink ? MediaPick.HypnoTube : MediaPick.Nothing;
        }

        /// <summary>The first of Path, Title that is a HypnoTube video page, or null.</summary>
        internal static string? HypnoTubeLinkOf(Media data)
        {
            var path = data.Path?.Trim();
            if (HtUrlHelper.IsEligibleHtUrl(path)) return path;
            var title = data.Title?.Trim();
            if (HtUrlHelper.IsEligibleHtUrl(title)) return title;
            return null;
        }

        /// <summary>The AI names a pool video by its TITLE ("Sissy Dreams 3"), never by URL, so a
        /// request resolves through the mod's link pool: an exact name first, then the closest name.
        /// Without this every effect-control video came back "didn't play" (ccp-bugs #1344).</summary>
        internal static string? PoolLinkOf(Media data, IReadOnlyDictionary<string, string>? pool)
        {
            if (pool == null || pool.Count == 0) return null;
            foreach (var q in new[] { data.Title, data.Path })
            {
                var name = q?.Trim();
                if (string.IsNullOrEmpty(name)) continue;
                foreach (var kv in pool)
                    if (string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase) && HtUrlHelper.IsEligibleHtUrl(kv.Value))
                        return kv.Value;
            }
            var names = pool.Where(kv => HtUrlHelper.IsEligibleHtUrl(kv.Value)).Select(kv => kv.Key).ToList();
            var hit = VideoTitleMatcher.FindBest(new[] { data.Title, data.Path }, names);
            return hit != null && pool.TryGetValue(hit, out var url) ? url : null;
        }

        public Task<bool> ExecuteAsync()
        {
            var fullPath = string.IsNullOrWhiteSpace(_data.Path) ? null : GetValidatedPath(_data.Path);
            var ext = fullPath == null ? "" : Path.GetExtension(fullPath).ToLowerInvariant();
            var playable = fullPath != null && (IsVideo(ext) || IsAudio(ext));

            // A named video that is not a literal file: the closest match by name in the library.
            if (!playable && !_data.Random && _kind == AICommandType.video)
            {
                var match = VideoTitleMatcher.FindBest(new[] { _data.Path, _data.Title }, LocalVideoLibrary());
                if (match != null)
                {
                    fullPath = match;
                    ext = Path.GetExtension(match).ToLowerInvariant();
                    playable = true;
                    App.Logger?.Information("MediaCommand: AI named video matched a library file by name");
                }
            }

            var htLink = HypnoTubeLinkOf(_data)
                ?? (_kind == AICommandType.video ? PoolLinkOf(_data, App.Mods?.GetVideoLinks()) : null);
            switch (Decide(_data, _kind, playable, htLink != null))
            {
                case MediaPick.Random:
                    if (_kind == AICommandType.audio)
                        return Task.FromResult(PlayRandomAudio());
                    return Task.FromResult(PlayRandomVideo());
                case MediaPick.HypnoTube:
                    return Task.FromResult(OpenHypnoTube(htLink!));
                case MediaPick.Nothing:
                    // No title or path in the log: a named video can be a private file name.
                    App.Logger?.Information("MediaCommand: AI named a video with no close local match and no HypnoTube link, playing nothing");
                    return Task.FromResult(false);
            }

            App.Logger?.Information("MediaCommand: AI play media {Path}", fullPath);

            if (IsVideo(ext))
            {
                // The main Videos feature toggle is authoritative for AI-triggered videos
                // too (#512). Guarded at the playback sink so every path is covered; an
                // audio-kind request that resolved to a video file still gets its audio
                // fallback instead of a silent drop.
                if (App.Settings?.Current?.MandatoryVideosEnabled != true)
                {
                    App.Logger?.Information("MediaCommand: AI video ignored — Videos feature is disabled");
                    if (_kind == AICommandType.audio) return Task.FromResult(PlayRandomAudio());
                    return Task.FromResult(false);
                }

                return Task.FromResult(Application.Current.Dispatcher.Invoke(() =>
                {
                    if (App.Video == null) return false;
                    if (App.Video.IsPlaying)
                    {
                        App.Logger?.Information("MediaCommand: video already playing — skipping {Path}", fullPath);
                        return false;
                    }
                    App.Video.PlaySpecificVideo(fullPath, false);
                    return true;
                }));
            }

            return Task.FromResult(PlayFile(fullPath!));   // Named means video or audio
        }

        /// <summary>
        /// Opens a HypnoTube video page in the embedded browser's fullscreen takeover, the way the
        /// companion's own web videos open (AutonomyService web video): the Videos toggle governs
        /// it like any AI video, it never navigates over a video that is playing, and it claims the
        /// fullscreen slot before navigating. True when the navigation was issued.
        /// </summary>
        private static bool OpenHypnoTube(string url)
        {
            if (App.Settings?.Current?.MandatoryVideosEnabled != true)
            {
                App.Logger?.Information("MediaCommand: AI HypnoTube video ignored, Videos feature is disabled");
                return false;
            }

            return Application.Current.Dispatcher.Invoke(() =>
            {
                if (App.RemoteControl?.ControllerConnected == true)
                {
                    App.Logger?.Information("MediaCommand: AI HypnoTube video skipped, a remote controller is connected");
                    return false;
                }
                if (App.Video?.IsPlaying == true || App.BrowserMedia?.ShouldDeferNewVideo == true)
                {
                    App.Logger?.Information("MediaCommand: AI HypnoTube video skipped, a video is already playing");
                    return false;
                }

                var mainWindow = App.MainWindowRef
                    ?? Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                if (mainWindow == null) return false;

                // A video asked for in chat plays whole: the longest-video limit is about what
                // the app makes the player watch (owner, 2026-10-13).
                if (App.BrowserMedia?.BeginTakeover(BrowserMediaService.MediaOwner.Autonomy, playerAskedFor: true) == false)
                    return false;

                // userInitiated: false - the AI decided this, so an offline block stays silent.
                if (mainWindow.NavigateToUrlInBrowser(url, autoPlayFullscreen: true, userInitiated: false))
                {
                    App.Logger?.Information("MediaCommand: AI HypnoTube video opened on {Host}", UrlLog.Host(url));
                    return true;
                }

                App.BrowserMedia?.OnMediaStopped("navigation-failed");
                App.Logger?.Warning("MediaCommand: AI HypnoTube video not opened, browser not available");
                return false;
            });
        }

        /// <summary>Every local video the random picker could draw: under the assets videos
        /// folder, minus the files and folders the user switched off.</summary>
        private static IReadOnlyList<string> LocalVideoLibrary()
        {
            try
            {
                var assetsRoot = App.EffectiveAssetsPath;
                var videosRoot = Path.Combine(assetsRoot, "videos");
                if (!Directory.Exists(videosRoot)) return Array.Empty<string>();

                var settings = App.Settings?.Current;
                static string Norm(string p) => p.Replace('\\', '/');
                var disabled = new HashSet<string>(
                    (settings?.DisabledAssetPaths ?? Enumerable.Empty<string>()).Select(Norm),
                    StringComparer.OrdinalIgnoreCase);
                var folders = settings?.DisabledAssetFolders?.ToArray() ?? Array.Empty<string>();

                return Directory.EnumerateFiles(videosRoot, "*.*", SearchOption.AllDirectories)
                    .Where(f => IsVideo(Path.GetExtension(f).ToLowerInvariant()))
                    .Where(f =>
                    {
                        var rel = Norm(Path.GetRelativePath(assetsRoot, f));
                        return !disabled.Contains(rel) && !AssetFolderExclusion.IsUnderAny(rel, folders);
                    })
                    .Take(5000)
                    .ToList();
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MediaCommand: video library scan threw");
                return Array.Empty<string>();
            }
        }

        private static bool PlayRandomVideo()
        {
            // Playback sink gate: the main Videos feature toggle governs AI videos (#512).
            if (App.Settings?.Current?.MandatoryVideosEnabled != true)
            {
                App.Logger?.Information("MediaCommand: random video ignored — Videos feature is disabled");
                return false;
            }

            return Application.Current.Dispatcher.Invoke(() =>
            {
                if (App.Video == null) return false;
                if (App.Video.IsPlaying)
                {
                    App.Logger?.Information("MediaCommand: random video requested but a video is already playing — skipping");
                    return false;
                }
                App.Video.TriggerVideo();
                return true;
            });
        }

        /// <summary>The folder a random AI audio pick draws from first: <c>&lt;assets&gt;\audio</c>.
        /// It is local files only - no online source feeds it (ccp-bugs #1428). When it is empty
        /// <see cref="ChooseAudioPool"/> names the recorded mod clips that stand in.</summary>
        internal static string AudioFolderUnder(string assetsRoot) => Path.Combine(assetsRoot, "audio");

        /// <summary>Every playable audio file under <paramref name="audioRoot"/>, subfolders
        /// included. Empty when the folder is missing.</summary>
        internal static string[] AudioFilesIn(string audioRoot)
        {
            if (string.IsNullOrWhiteSpace(audioRoot) || !Directory.Exists(audioRoot)) return Array.Empty<string>();
            return Directory.GetFiles(audioRoot, "*.*", SearchOption.AllDirectories)
                .Where(f => IsAudio(Path.GetExtension(f).ToLowerInvariant()))
                .ToArray();
        }

        /// <summary>Where a random AI audio pick comes from.</summary>
        internal enum AudioPool { None, Own, ModVoice, SharedClips, NeutralClips }

        /// <summary>
        /// Pure choice of the pool a random AI audio pick draws from (owner, 2026-10-13). The
        /// player's own files in the assets audio folder always win. With none, a RECORDED clip
        /// from the active mod stands in, in the order <see cref="SpokenClipRule"/> gives a spoken
        /// phrase: the mod's own voice first; then the bundled Bambi-voiced clips, only for a mod
        /// <see cref="ModAudioPolicy.UsesSharedSubAudio"/> allows; every other mod (CCP Default,
        /// Locked, an id not known yet) gets the recorded neutral words and nothing else. A pool
        /// the policy rules out is never even read. No pool means nothing plays: there is no
        /// synthetic voice behind this.
        /// </summary>
        internal static (AudioPool Pool, IReadOnlyList<string> Files) ChooseAudioPool(
            IReadOnlyList<string>? ownFiles,
            string? modId,
            Func<IReadOnlyList<string>?> modVoice,
            Func<IReadOnlyList<string>?> sharedClips,
            Func<IReadOnlyList<string>?> neutralClips)
        {
            if (ownFiles != null && ownFiles.Count > 0) return (AudioPool.Own, ownFiles);

            // With no mod id the voice-line folder still resolves to the Bambi baseline
            // (CompanionContentResolver.OwnsBaselineVoiceLines keeps it for boot), so an unknown
            // mod has no voice of its own here.
            if (!string.IsNullOrWhiteSpace(modId))
            {
                var voice = modVoice();
                if (voice != null && voice.Count > 0) return (AudioPool.ModVoice, voice);
            }

            if (ModAudioPolicy.UsesSharedSubAudio(modId))
            {
                var shared = sharedClips();
                return shared != null && shared.Count > 0
                    ? (AudioPool.SharedClips, shared)
                    : (AudioPool.None, Array.Empty<string>());
            }

            var neutral = neutralClips();
            return neutral != null && neutral.Count > 0
                ? (AudioPool.NeutralClips, neutral)
                : (AudioPool.None, Array.Empty<string>());
        }

        /// <summary>The pool a random AI audio pick would draw from right now. Each mod pool is
        /// read through the lookup its own feature already uses: the companion's enabled voice
        /// lines, the trigger clips folder, the neutral words manifest.</summary>
        internal static (AudioPool Pool, IReadOnlyList<string> Files) CurrentAudioPool()
        {
            return ChooseAudioPool(
                AudioFilesIn(AudioFolderUnder(App.EffectiveAssetsPath)),
                App.Mods?.ActiveModId,
                modVoice: ModVoiceLines,
                sharedClips: () => AudioFilesIn(Path.Combine(AppContext.BaseDirectory, "Resources", "sub_audio")),
                neutralClips: NeutralWordClips);
        }

        /// <summary>The active mod's voice lines the player left switched on
        /// (<see cref="CompanionPhraseService.GetEnabledVoiceLineFiles"/>, which may save a
        /// one-time id migration, so it runs on the UI thread).</summary>
        private static IReadOnlyList<string> ModVoiceLines()
        {
            try
            {
                var phrases = App.CompanionPhrases;
                if (phrases == null) return Array.Empty<string>();
                var dispatcher = Application.Current?.Dispatcher;
                return dispatcher == null
                    ? phrases.GetEnabledVoiceLineFiles()
                    : dispatcher.Invoke(() => phrases.GetEnabledVoiceLineFiles());
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MediaCommand: mod voice lines unreadable ({Type})", ex.GetType().Name);
                return Array.Empty<string>();
            }
        }

        /// <summary>Every recorded neutral word on disk (the clips
        /// <see cref="BackRoom.BackRoomVoice.FindNeutralClip"/> answers with).</summary>
        private static IReadOnlyList<string> NeutralWordClips()
        {
            try
            {
                var root = BackRoom.BackRoomVoice.WordsRoot();
                return BackRoom.BackRoomVoice.Manifest().Values
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Select(name => Path.Combine(root, name))
                    .Where(File.Exists)
                    .ToList();
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("MediaCommand: neutral words unreadable ({Type})", ex.GetType().Name);
                return Array.Empty<string>();
            }
        }

        private static bool PlayRandomAudio()
        {
            try
            {
                var (pool, candidates) = CurrentAudioPool();

                if (pool == AudioPool.None || candidates.Count == 0)
                {
                    App.Logger?.Warning("MediaCommand: no audio files under {Root} and no recorded clip for the active mod, cannot fulfill random audio",
                        AudioFolderUnder(App.EffectiveAssetsPath));
                    return false;
                }

                var pick = candidates[new Random().Next(candidates.Count)];
                App.Logger?.Information("MediaCommand: random audio pick from {Pool}: {Path}", pool, pick);
                return PlayFile(pick);
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MediaCommand: random audio pick threw");
                return false;
            }
        }

        /// <summary>
        /// Hands one audio file to <see cref="AudioService.PlaySound"/> and reports whether it
        /// actually started. PlaySound returns the clip duration and 0 when it could not play
        /// (no audio service yet, unreadable file, no output device), so the caller can tell the
        /// user "nothing played" instead of claiming success (#1120).
        /// </summary>
        private static bool PlayFile(string path)
        {
            var seconds = Application.Current.Dispatcher.Invoke(() => App.Audio?.PlaySound(path, 100) ?? 0);
            if (seconds <= 0)
            {
                App.Logger?.Warning("MediaCommand: audio {Path} did not start playing", path);
                return false;
            }
            return true;
        }

        private static bool IsVideo(string ext)
        {
            var v = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm" };
            return v.Contains(ext);
        }

        private static bool IsAudio(string ext)
        {
            var a = new[] { ".mp3", ".wav", ".wma", ".ogg", ".flac", ".aac", ".m4a" };
            return a.Contains(ext);
        }

        private static string? GetValidatedPath(string path)
        {
            try
            {
                // Defense-in-depth: reject obvious traversal attempts up front.
                if (path.Contains("..", StringComparison.Ordinal)) return null;

                var assetsRoot = Path.GetFullPath(App.EffectiveAssetsPath);
                var fullPath = Path.IsPathRooted(path)
                    ? Path.GetFullPath(path)
                    : Path.GetFullPath(Path.Combine(assetsRoot, path));

                // After normalization the resolved path must still live under assets root.
                if (!fullPath.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase)) return null;
                if (!File.Exists(fullPath)) return null;
                return fullPath;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "MediaCommand: path validation threw");
                return null;
            }
        }
    }
}
