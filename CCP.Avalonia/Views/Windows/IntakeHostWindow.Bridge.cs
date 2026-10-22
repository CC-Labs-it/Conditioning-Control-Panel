using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Content;
using ConditioningControlPanel.Services.Quiz;
using ConditioningControlPanel.Services.Speech;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// The intake page's file, media and mic requests (WPF IntakeHostService.cs OnLoomSave /
    /// OnSaveSpiralImage / ServeRemoteBatch / RequestAudioPack and IntakeHostService.Speech.cs).
    /// Every rule is Core: <see cref="DtrhLoomStore"/> (slug whitelist, cap, GIF magic),
    /// <see cref="IntakeRun.SaveSpiralImage"/> (PNG magic, name built host-side),
    /// <see cref="IntakeRun.RemoteStills"/> and <see cref="IntakeSpeechPolicy"/>.
    /// </summary>
    internal sealed partial class IntakeHostWindow
    {
        /// <summary>WPF SpiralImageFolder: beside the user data, not the Loom rack's Spirals folder.</summary>
        internal string SpiralImageFolder { get; init; } = Path.Combine(CorePaths.UserData, "intake_spirals");

        /// <summary>The engine the say-it beat listens through (the head's Vosk engine; tests swap it).</summary>
        internal Func<SpeechEngine?> Speech { get; init; } = () => Platform.PulseMicSource.Speech;

        // ---------------- loom-save / intake-save-image ----------------

        private void OnLoomSave(JObject o)
        {
            try
            {
                var (ok, slug, error) = DtrhLoomStore.Save(
                    (string?)o["name"], (string?)o["gifBase64"], o["params"], (bool?)o["overwrite"] ?? false);
                Post(new { type = "loom-result", op = "save", ok, slug, error });
                if (ok) Log.Information("IntakeHostService: recap spiral kept as {Slug}", slug);
            }
            catch (Exception ex)
            {
                Log.Warning("IntakeHostService.OnLoomSave: {E}", ex.Message);
                Post(new { type = "loom-result", op = "save", ok = false, slug = (string?)null, error = "io-failed" });
            }
        }

        internal static object SaveSpiralImage(JObject o, string folder)
        {
            var (path, error) = IntakeRun.SaveSpiralImage(o, folder, DateTime.Now);
            return new { type = "intake-save-image-result", ok = error == null, path, error };
        }

        // ---------------- need-remote ----------------

        private int _remoteFetchInFlight;   // single-flight; the page re-asks after every reply

        /// <summary>Test seam: the batch source (WPF FypOnlineCoordinator "intake" tenant, stills only).</summary>
        internal Func<Task<(System.Collections.Generic.List<Services.Fyp.FypAssetManifest.Entry> Entries, string? Error)>> FetchRemote { get; init; }
            = () => IntakeRun.RemoteCoordinator().FetchBatchAsync(CancellationToken.None);

        internal async Task ServeRemoteBatchAsync()
        {
            if (!IntakeRun.RemoteMediaEnabled(CoreSettings.Current)) return;
            if (Interlocked.CompareExchange(ref _remoteFetchInFlight, 1, 0) != 0) return;
            try
            {
                var (entries, error) = await FetchRemote().ConfigureAwait(false);
                var urls = IntakeRun.RemoteStills(entries);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_closed) return;   // intake closed while fetching
                    if (urls.Count > 0) Post(new { type = "assets-append", images = urls });
                    // Sent either way: it clears the page's in-flight latch.
                    Post(new { type = "online-status", ok = error == null, error, added = urls.Count });
                });
                if (urls.Count > 0) Log.Information("IntakeHost: appended {N} remote stills", urls.Count);
            }
            catch (Exception ex) { Log.Warning("IntakeHost: remote batch failed: {E}", ex.Message); }
            finally { Interlocked.Exchange(ref _remoteFetchInFlight, 0); }
        }

        private bool _closed;

        // ---------------- audio-web pack ----------------

        /// <summary>WPF RequestAudioPack: kicked on open, never awaited, the outcome logged (bug #1032).</summary>
        internal static void RequestAudioPack(ReleaseContentService? svc)
        {
            try
            {
                if (svc == null) return;
                var task = svc.RequestPackAsync(ReleaseContentService.PackAudioWeb);
                if (task.IsCompletedSuccessfully && task.Result) return;
                _ = task.ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully && t.Result) { Log.Information("IntakeHost: audio-web pack ready"); return; }
                    var reason = t.IsFaulted ? (t.Exception?.GetBaseException().Message ?? "faulted")
                        : t.IsCanceled ? "cancelled"
                        : CoreSettings.Current.OfflineMode ? "offline mode is on"
                        : "download or manifest fetch failed (see the ReleaseContentService lines above)";
                    if (svc.IsInstalled(ReleaseContentService.PackAudioWeb))
                        Log.Information("IntakeHost: audio-web refresh failed ({Reason}) - the installed clips still play", reason);
                    else
                        Log.Warning("IntakeHost: audio-web pack unavailable ({Reason}) - this run has no recorded VO or music and "
                            + "falls back to the in-page synth sfx", reason);
                }, TaskScheduler.Default);
            }
            catch (Exception ex) { Log.Debug("IntakeHost: audio-web request failed: {E}", ex.Message); }
        }

        // ---------------- panic (WPF GameSurfaces 'intake' -> CloseActive -> DisposeAll) ----------------

        private static readonly System.Collections.Generic.List<IntakeHostWindow> OpenWindows = new();

        internal static IntakeHostWindow[] Snapshot() { lock (OpenWindows) return OpenWindows.ToArray(); }

        /// <summary>PanicSurfaces 'intake' screen probe (WPF AnyGameSurfaceOwnsTheScreen).</summary>
        internal static bool IsAnyOpen() { lock (OpenWindows) return OpenWindows.Count > 0; }

        /// <summary>The panic key's first step, before anything aborts the capture: cancel each say-it loop so
        /// an aborted listen is never read as silence and the mic never reopens. The page is told "stopped",
        /// because a rung that keeps the intake open (palette Escape, lock-card dismiss) would leave it
        /// showing "listening".</summary>
        internal static void StopMicsForPanic()
        {
            foreach (var w in Snapshot()) w.ReleaseSpeechMic("panic", notifyPage: true);
        }

        /// <summary>Panic closes the intake like WPF's GameSurfaces close pass. True when one was up
        /// (the panic key then skips the exit ladder, as WPF does while a game owns the screen).</summary>
        internal static bool CloseAllForPanic()
        {
            var open = Snapshot();
            foreach (var w in open)
            {
                w.ReleaseSpeechMic("panic", notifyPage: false);
                try { w.Close(); } catch (Exception ex) { Log.Warning("PANIC: closing intake failed: {E}", ex.Message); }
            }
            return open.Length > 0;
        }

        // ---------------- speech bridge (WPF IntakeHostService.Speech.cs) ----------------

        private readonly object _speechGate = new();
        private CancellationTokenSource? _speechCts;
        private int _speechId;

        // The same holder rule as WPF: an old question's late return cannot re-arm the wake
        // loop underneath the next answer. Each UI handover is posted, preserving their order.
        internal IntakeMicBorrow Mic { get; init; } = new(WakeArmed,
            () => Dispatcher.UIThread.Post(() => MainShellWindow.Current?.StopVoiceInput()),
            () => Dispatcher.UIThread.Post(() => MainShellWindow.Current?.RefreshVoiceInputModes()));

        private static bool WakeArmed() => MainShellWindow.Current is { } shell
            && (shell.WakeLoopArmed || shell.PushToTalkArmed);

        private object SpeechCaps()
        {
            var reason = SpeechUnavailability(countMicHeld: !WakeArmed());
            return new { bridge = true, available = reason == null, reason };
        }

        /// <summary>Null = the mic can open now. <c>IsListening</c> covers anything already holding the engine,
        /// including the voice-command wake-word loop (MainShellWindow.VoiceCommands.cs).</summary>
        private string? SpeechUnavailability(bool countMicHeld = true)
        {
            try
            {
                var speech = Speech();
                var model = speech == null ? SpeechModelStatus.NoModelFound
                          : (speech.IsAvailable ? SpeechModelStatus.Ok : speech.ModelStatus);
                return IntakeSpeechPolicy.Unavailability(CoreSettings.Current.MicConsentGiven,
                    CoreSpeech.HasCaptureDevice, model, countMicHeld && speech?.IsListening == true);
            }
            catch (Exception ex)
            {
                Log.Debug("IntakeHostService.speech: availability probe failed: {E}", ex.Message);
                return IntakeSpeechPolicy.ReasonError;
            }
        }

        private void OnSpeechStart(JObject o)
        {
            int id = (int?)o["id"] ?? 0;
            var phrase = ((string?)o["phrase"] ?? "").Trim();
            if (id <= 0 || phrase.Length == 0) { PostSpeechEvent(id, "unavailable", new { reason = IntakeSpeechPolicy.ReasonError }); return; }

            StopSpeechBridge("superseded", notifyPage: false);   // at most one loop ever touches the mic
            var reason = SpeechUnavailability(countMicHeld: false);
            if (reason != null)
            {
                Log.Information("IntakeHostService: speech-start #{Id} refused ({Reason})", id, reason);
                PostSpeechEvent(id, "unavailable", new { reason });
                return;
            }
            Mic.Take(id);
            var cts = new CancellationTokenSource();
            lock (_speechGate) { _speechCts = cts; _speechId = id; }
            Log.Information("IntakeHostService: speech-start #{Id} ({Chars}-char target)", id, phrase.Length);
            _ = Task.Run(() => RunSpeechLoopAsync(id, phrase, cts));
        }

        private void OnSpeechStop(JObject o)
        {
            int id = (int?)o["id"] ?? 0;
            lock (_speechGate) { if (id != 0 && id != _speechId) return; }   // a stale beat's cleanup
            StopSpeechBridge("page", notifyPage: true);
        }

        /// <summary>Cancel the listen loop, if any; idempotent. Closing the window calls it.</summary>
        internal void StopSpeechBridge(string why, bool notifyPage)
        {
            CancellationTokenSource? cts;
            int id;
            lock (_speechGate) { cts = _speechCts; id = _speechId; _speechCts = null; _speechId = 0; }
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
            Log.Debug("IntakeHostService: speech #{Id} stopped ({Why})", id, why);
            if (notifyPage) PostSpeechEvent(id, "stopped", null);
        }

        private void ReleaseSpeechMic(string why, bool notifyPage)
        {
            StopSpeechBridge(why, notifyPage);
            Mic.ReturnAll();
        }

        private async Task RunSpeechLoopAsync(int id, string phrase, CancellationTokenSource cts)
        {
            var ct = cts.Token;
            SpeechEngine? speech = null;
            string lastPartial = "";
            void OnPartial(object? _, string text)
            {
                if (string.IsNullOrWhiteSpace(text) || text == lastPartial) return;   // Vosk re-emits every buffer
                lastPartial = text;
                PostSpeechEvent(id, "partial", new { transcript = text });
            }

            int silent = 0, misses = 0;
            try
            {
                speech = Speech();
                if (speech == null) { PostSpeechEvent(id, "unavailable", new { reason = IntakeSpeechPolicy.ReasonNoModel }); return; }
                speech.PartialTranscript += OnPartial;
                var options = new RecognizeOptions { Timeout = TimeSpan.FromSeconds(10) };
                bool announced = false;
                while (!ct.IsCancellationRequested)
                {
                    var stoodDown = Mic.Take(id);
                    if (stoodDown || Mic.IsOwed)
                    {
                        // Drain the posted stand-down before checking capture; never cancel our own
                        // listen with a late StopVoiceInput. WPF gives the capture up to 600 ms.
                        await Dispatcher.UIThread.InvokeAsync(() => { });
                        for (var waited = 0; waited < 600 && speech.IsListening; waited += 25)
                            await Task.Delay(25, ct).ConfigureAwait(false);
                    }
                    if (ct.IsCancellationRequested) return;
                    var reason = SpeechUnavailability();
                    if (reason != null) { PostSpeechEvent(id, "unavailable", new { reason }); return; }
                    if (!announced) { announced = true; PostSpeechEvent(id, "listening", null); }
                    lastPartial = "";

                    PhraseResult res;
                    try { res = await speech.RecognizePhraseAsync(phrase, options, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    if (ct.IsCancellationRequested) return;
                    if (res.Unavailable) { PostSpeechEvent(id, "unavailable", new { reason = IntakeSpeechPolicy.ReasonBusy }); return; }
                    if (res.Matched)
                    {
                        PostSpeechEvent(id, "final", new { matched = true, transcript = res.Transcript, score = res.Score, loudEnough = res.LoudEnough });
                        return;
                    }
                    if (res.TimedOut && string.IsNullOrWhiteSpace(res.Transcript))
                    {
                        silent++; misses = 0;
                        PostSpeechEvent(id, "silence", null);
                    }
                    else
                    {
                        misses++; silent = 0;
                        PostSpeechEvent(id, "final", new { matched = false, transcript = res.Transcript, score = res.Score, loudEnough = res.LoudEnough });
                        try { await Task.Delay(350, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
                    }
                    if (IntakeSpeechPolicy.ShouldGoIdle(silent, misses)) { PostSpeechEvent(id, "idle", null); return; }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Log.Warning("IntakeHostService: speech #{Id} loop failed: {E}", id, ex.Message);
                PostSpeechEvent(id, "unavailable", new { reason = IntakeSpeechPolicy.ReasonError });
            }
            finally
            {
                if (speech != null) speech.PartialTranscript -= OnPartial;
                Mic.Return(id);
                lock (_speechGate) { if (ReferenceEquals(_speechCts, cts)) { _speechCts = null; _speechId = 0; } }
                cts.Dispose();
            }
        }

        /// <summary>One <c>speech-event</c>, marshalled to the UI thread; dropped once the window is gone.</summary>
        private void PostSpeechEvent(int id, string kind, object? extra)
        {
            void Send()
            {
                if (_closed) return;
                var frame = new JObject { ["type"] = "speech-event", ["id"] = id, ["kind"] = kind };
                if (extra != null) frame.Merge(JObject.FromObject(extra));
                Post(frame);
            }
            if (Dispatcher.UIThread.CheckAccess()) Send();
            else Dispatcher.UIThread.Post(Send);
        }
    }
}
