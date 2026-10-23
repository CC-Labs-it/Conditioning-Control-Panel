// Adapted from WPF SherpaWakeService at e138d8bfc84cee4008eaf78a91156fb6b8563110.
// Capture uses the shared IMicSource; audio remains in memory.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SherpaOnnx;
namespace ConditioningControlPanel.Services.Speech
{
    public sealed class SherpaWakeService : IDisposable
    {
        private readonly IMicSource _mic;
        private readonly string _modelRoot;
        // The sweep seam lets tests replay PCM without native inference or a real microphone.
        internal Func<double, float[], bool>? CalibrationScorer;
        public SherpaWakeService(IMicSource mic, string? modelRoot = null)
            => (_mic, _modelRoot) = (mic, modelRoot ?? ModelRoot);
        private const int SampleRate = 16000; // KWS zipformer models are 16 kHz mono.
        private const int FeatureDim = 80;
        private readonly object _gate = new();
        private KeywordSpotter? _spotter;
        private string? _initFingerprint; // the model set the live spotter was built from
        private string? _failedFingerprint; // a model set we already tried and that threw — don't hammer it
        private bool _disposed;
        private CancellationTokenSource? _activeCts;
        public void StopListening() { lock (_gate) { try { _activeCts?.Cancel(); } catch { } } }
        private int _sessionActive; // 0/1 via Interlocked.
        public event EventHandler<bool>? ListeningChanged;
        private bool _isListening;
        public bool IsListening
        {
            get => _isListening;
            private set
            {
                if (_isListening == value) return;
                _isListening = value;
                try { ListeningChanged?.Invoke(this, value); } catch { /* UI handler hygiene */ }
            }
        }
        public static string ModelRoot =>
            Path.Combine(AppContext.BaseDirectory, "Resources", "Models", "sherpa-kws");
        public bool HasCaptureDevice => _mic.HasDevice;
        public readonly record struct ModelFiles(string Encoder, string Decoder, string Joiner, string Tokens, string Keywords);
        public static ModelFiles? FindModel(string root)
        {
            try
            {
                if (!Directory.Exists(root)) return null;
                var onnx = Directory.EnumerateFiles(root, "*.onnx", SearchOption.AllDirectories).ToList();
                string? Pick(string part) =>
                    onnx.Where(f => Path.GetFileName(f).Contains(part, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => Path.GetFileName(f).Contains("int8", StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault();
                var enc = Pick("encoder");
                var dec = Pick("decoder");
                var join = Pick("joiner");
                var tokens = Directory.EnumerateFiles(root, "tokens.txt", SearchOption.AllDirectories).FirstOrDefault();
                var keywords = Directory.EnumerateFiles(root, "keywords.txt", SearchOption.AllDirectories).FirstOrDefault();
                if (enc == null || dec == null || join == null || tokens == null || keywords == null) return null;
                return new ModelFiles(enc, dec, join, tokens, keywords);
            }
            catch { return null; }
        }
        public bool IsConfigured => !_disposed && FindModel(_modelRoot) != null;
        public bool IsAvailable
        {
            get
            {
                if (_disposed || !HasCaptureDevice || !IsConfigured) return false;
                EnsureEngine();
                return _spotter != null;
            }
        }
        internal static float WakeThreshold() => (float)Math.Clamp(CoreSettings.Current?.SpeechWakeThreshold ?? 0.15, 0.02, 0.6);
        internal static float WakeBoost() => (float)Math.Clamp(CoreSettings.Current?.SpeechWakeBoost ?? 2.0, 0.0, 5.0);
        private static string Fingerprint(ModelFiles m)
        {
            string kwStamp = "";
            try { kwStamp = File.GetLastWriteTimeUtc(m.Keywords).Ticks.ToString(); } catch { }
            return string.Join("|", m.Encoder, m.Decoder, m.Joiner, m.Tokens, m.Keywords,
                                WakeThreshold().ToString("0.000"), WakeBoost().ToString("0.0"), kwStamp);
        }
        private void EnsureEngine()
        {
            var model = FindModel(_modelRoot);
            if (model is not { } m) return;
            var fp = Fingerprint(m);
            if (_spotter != null && _initFingerprint == fp) return;
            if (_failedFingerprint == fp) return; // this exact model set already threw; wait for a change/reset
            lock (_gate)
            {
                if (_disposed || Volatile.Read(ref _sessionActive) != 0) return;
                if (_spotter != null && _initFingerprint == fp) return;
                if (_failedFingerprint == fp) return;
                if (_spotter != null) { try { _spotter.Dispose(); } catch { } _spotter = null; }
                try
                {
                    _spotter = BuildSpotter(m, WakeThreshold(), WakeBoost());
                    _initFingerprint = fp;
                    _failedFingerprint = null;
                    Log.Information("SherpaWakeService: KWS engine initialised (keywords {Kw})", Path.GetFileName(m.Keywords));
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "SherpaWakeService: failed to initialise — wake falls back to Vosk");
                    _spotter = null;
                    _failedFingerprint = fp;
                }
            }
        }
        public void ResetInitState()
        {
            lock (_gate)
            {
                _failedFingerprint = null;
                if (_sessionActive != 0) return; // status paints must never free an in-use native handle
                _spotter?.Dispose();
                _spotter = null;
                _initFingerprint = null;
            }
        }
        public async Task<bool> WaitForWakeAsync(CancellationToken cancellation)
        {
            using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var ct = session.Token;
            KeywordSpotter? spotter;
            lock (_gate)
            {
                if (!IsAvailable || _sessionActive != 0 || ct.IsCancellationRequested) return false;
                _sessionActive = 1;
                _activeCts = session;
                spotter = _spotter;
            }
            try
            {
                if (spotter == null) return false;
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                IDisposable? mic = null;
                OnlineStream? stream = null;
                SileroVadGate? vadGate = null;
                var engineLock = new object(); // serialize engine/stream access with teardown
                var done = 0;
                void Finish(bool detected)
                {
                    if (Interlocked.Exchange(ref done, 1) != 0) return;
                    tcs.TrySetResult(detected);
                }
                try
                {
                    stream = spotter.CreateStream();
                    var nsEnabled = CoreSettings.Current?.SpeechNoiseSuppression ?? true;
                    var gateFactor = CoreSettings.Current?.SpeechNoiseGateFactor ?? 4.0;
                    var hpf = nsEnabled ? new HighPassFilter(SampleRate) : null;
                    vadGate = nsEnabled ? SileroVadGate.TryCreate(SampleRate) : null;
                    var gate = nsEnabled && vadGate == null ? new NoiseGate(gateFactor, hangoverFrames: 12) : null;
                    var preRoll = nsEnabled ? new PreRollBuffer(SampleRate) : null;
                    Action<byte[], int> capture = (pcm, bytes) =>
                    {
                        lock (engineLock)
                        {
                            if (Volatile.Read(ref done) != 0) return;
                            try
                            {
                                int n = bytes / 2;
                                if (n <= 0) return;
                                var samples = new float[n];
                                for (int i = 0, j = 0; i + 1 < bytes; i += 2, j++)
                                    samples[j] = (short)(pcm[i] | (pcm[i + 1] << 8)) / 32768f;
                                hpf?.ProcessInPlace(samples, n);
                                double rms = 0;
                                if (gate != null)
                                {
                                    double sumSq = 0;
                                    for (int k = 0; k < n; k++) sumSq += samples[k] * samples[k];
                                    rms = Math.Sqrt(sumSq / n);
                                }
                                bool voiced = vadGate?.Update(samples) ?? gate?.Update(rms) ?? true;
                                if (!voiced && preRoll != null)
                                {
                                    preRoll.Push(samples);
                                    return;
                                }
                                if (Volatile.Read(ref done) != 0 || stream == null) return;
                                if (preRoll != null)
                                    foreach (var held in preRoll.Drain())
                                        stream.AcceptWaveform(SampleRate, held);
                                stream.AcceptWaveform(SampleRate, samples);
                                while (spotter.IsReady(stream))
                                {
                                    spotter.Decode(stream);
                                    var result = spotter.GetResult(stream);
                                    if (!string.IsNullOrEmpty(result.Keyword))
                                    {
                                        spotter.Reset(stream);
                                        Finish(true);
                                        return;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "SherpaWakeService: capture callback failed");
                                Finish(false);
                            }
                        }
                    };
                    using var ctReg = ct.Register(() => Finish(false));
                    IsListening = true; // mic is now physically open — light the privacy pill
                    mic = _mic.Start(capture);
                    return await tcs.Task.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "SherpaWakeService: wake session failed");
                    return false;
                }
                finally
                {
                    Interlocked.Exchange(ref done, 1);
                    IsListening = false; // mic closing — drop the privacy pill (unless another session re-opens it)
                    try { mic?.Dispose(); } catch { }
                    lock (engineLock)
                    {
                        try { stream?.Dispose(); } catch { }
                        stream = null;
                        try { vadGate?.Dispose(); } catch { }
                        vadGate = null;
                    }
                }
            }
            finally
            {
                lock (_gate)
                {
                    _activeCts = null;
                    Interlocked.Exchange(ref _sessionActive, 0);
                    if (_disposed) { _spotter?.Dispose(); _spotter = null; }
                }
            }
        }
        public sealed class CalibrationProgress
        {
            public string Phase = "";   // "listen" while collecting says, "analyze" during the sweep
            public int Captured;        // utterances captured so far
            public int Target;          // how many we want
            public double Level;        // current input RMS (0..1) for a meter
        }
        public sealed class CalibrationResult
        {
            public bool Success;
            public string Message = "";
            public double ChosenThreshold;
            public int Utterances;
            public int CaughtAtChosen;
        }
        public async Task<CalibrationResult> CalibrateAsync(int target = 5, IProgress<CalibrationProgress>? progress = null, CancellationToken cancellation = default)
        {
            using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            var ct = session.Token;
            if (!IsConfigured) return new CalibrationResult { Message = "The wake-word model isn't installed." };
            if (!HasCaptureDevice) return new CalibrationResult { Message = "No microphone detected." };
            var model = FindModel(_modelRoot);
            if (model is not { } m) return new CalibrationResult { Message = "The wake-word model isn't installed." };
            lock (_gate)
            {
                if (_disposed || ct.IsCancellationRequested) return new CalibrationResult { Message = "Calibration cancelled." };
                if (_sessionActive != 0) return new CalibrationResult { Message = "The microphone is busy — stop listening, then calibrate." };
                _sessionActive = 1;
                _activeCts = session;
            }
            try
            {
                // WPF capture: seed 300 ms room tone, keep 800 ms pre-roll, segment 250–2500 ms says.
                var utterances = new List<float[]>();
                var ambient = new List<float>(SampleRate * 3); // up to ~3s of room tone
                var cur = new List<float>();
                double noiseFloor = 0.01;     // adaptive room-tone estimate; re-seeded from real room tone below
                int trailingSilenceMs = 0;
                bool inSpeech = false;
                var captureLock = new object();
                var doneTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                const double MinUttMs = 250, MaxUttMs = 2500, EndSilenceMs = 480;
                const double MaxPreRollMs = 800;
                const double FloorSeedMs = 300;
                int MsToSamples(double ms) => (int)(SampleRate * ms / 1000.0);
                int seedTargetSamples = MsToSamples(FloorSeedMs);
                int seedSamples = 0; double seedMinRms = double.MaxValue; bool floorSeeded = false;
                var preRoll = new PreRollBuffer(SampleRate, MaxPreRollMs / 1000.0);
                int preRollSamples = 0;       // pre-roll drained into the CURRENT utterance
                IDisposable? mic = null;
                var hpfCal = (CoreSettings.Current?.SpeechNoiseSuppression ?? true) ? new HighPassFilter(SampleRate) : null;
                try
                {
                    Action<byte[], int> capture = (pcm, bytes) =>
                    {
                        try
                        {
                            int n = bytes / 2;
                            if (n <= 0) return;
                            var buf = new float[n];
                            for (int i = 0, j = 0; i + 1 < bytes; i += 2, j++)
                                buf[j] = (short)(pcm[i] | (pcm[i + 1] << 8)) / 32768f;
                            hpfCal?.ProcessInPlace(buf, n);
                            double sumSq = 0;
                            for (int k = 0; k < n; k++) sumSq += buf[k] * buf[k];
                            double rms = Math.Sqrt(sumSq / n);
                            double bufMs = 1000.0 * n / SampleRate;
                            lock (captureLock)
                            {
                                if (doneTcs.Task.IsCompleted) return;
                                if (!floorSeeded)
                                {
                                    seedMinRms = Math.Min(seedMinRms, rms);
                                    seedSamples += n;
                                    preRoll.Push(buf);
                                    if (seedSamples >= seedTargetSamples)
                                    {
                                        noiseFloor = Math.Clamp(seedMinRms, 0.0005, 0.05);
                                        floorSeeded = true;
                                    }
                                }
                                else
                                {
                                    if (!inSpeech) noiseFloor = Math.Min(noiseFloor * 1.02 + 1e-5, Math.Max(noiseFloor, rms));
                                    if (rms < noiseFloor * 1.5) noiseFloor = 0.9 * noiseFloor + 0.1 * rms;
                                    double onsetGate = Math.Clamp(noiseFloor * 4.0, 0.006, 0.08);
                                    double endGate = onsetGate * 0.6;
                                    if (!inSpeech)
                                    {
                                        if (rms < endGate && ambient.Count < SampleRate * 2) ambient.AddRange(buf);
                                        if (rms >= onsetGate)
                                        {
                                            inSpeech = true; trailingSilenceMs = 0;
                                            cur.Clear(); preRollSamples = 0;
                                            foreach (var held in preRoll.Drain()) { cur.AddRange(held); preRollSamples += held.Length; }
                                            cur.AddRange(buf);
                                        }
                                        else preRoll.Push(buf); // held by reference — buf is a fresh array per callback
                                    }
                                    else
                                    {
                                        cur.AddRange(buf);
                                        trailingSilenceMs = rms < endGate ? trailingSilenceMs + (int)bufMs : 0;
                                        double uttMs = 1000.0 * cur.Count / SampleRate;
                                        double spokenMs = uttMs - 1000.0 * preRollSamples / SampleRate;
                                        if (trailingSilenceMs >= EndSilenceMs || spokenMs >= MaxUttMs)
                                        {
                                            if (spokenMs - trailingSilenceMs >= MinUttMs && uttMs <= MaxUttMs + MaxPreRollMs + 200)
                                            {
                                                utterances.Add(cur.ToArray());
                                                progress?.Report(new CalibrationProgress { Phase = "listen", Captured = utterances.Count, Target = target, Level = rms });
                                            }
                                            inSpeech = false; cur.Clear(); preRollSamples = 0; trailingSilenceMs = 0;
                                            if (utterances.Count >= target) doneTcs.TrySetResult(true);
                                        }
                                    }
                                }
                            }
                            progress?.Report(new CalibrationProgress { Phase = "listen", Captured = utterances.Count, Target = target, Level = rms });
                        }
                        catch { /* never let a capture frame throw out */ }
                    };
                    using var ctReg = ct.Register(() => doneTcs.TrySetResult(false));
                    using var capTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
                    using var toReg = capTimeout.Token.Register(() => doneTcs.TrySetResult(false));
                    IsListening = true;
                    mic = _mic.Start(capture);
                    await doneTcs.Task.ConfigureAwait(false);
                }
                finally
                {
                    IsListening = false;
                    try { mic?.Dispose(); } catch { }
                }
                if (ct.IsCancellationRequested)
                    return new CalibrationResult { Message = "Calibration cancelled." };
                if (utterances.Count < 2)
                {
                    return new CalibrationResult { Message = $"Only heard {utterances.Count} clear say(s). Try again — say “Hey Bambi” clearly, with a pause between each." };
                }
                progress?.Report(new CalibrationProgress { Phase = "analyze", Captured = utterances.Count, Target = target });
                var ambientArr = ambient.ToArray();
                float boost = WakeBoost();
                // WPF recall-first sweep: strict and quiet, then recall, then best effort.
                double[] candidates = { 0.28, 0.24, 0.20, 0.16, 0.13, 0.10, 0.08, 0.06 };
                int needed = Math.Max(2, (int)Math.Ceiling(utterances.Count * 0.7));
                int n = utterances.Count;
                var caughtAt = new int[candidates.Length];
                var ambientAt = new bool[candidates.Length];
                bool ambientUsable = ambientArr.Length > SampleRate / 2; // need ~0.5s+ of room tone to judge
                await Task.Run(() =>
                {
                    for (int k = 0; k < candidates.Length; k++)
                    {
                        ct.ThrowIfCancellationRequested();
                        using var spk = CalibrationScorer == null ? BuildSpotter(m, (float)candidates[k], boost) : null;
                        bool Fires(float[] audio) => CalibrationScorer?.Invoke(candidates[k], audio) ?? SpotFires(spk!, audio);
                        ambientAt[k] = ambientUsable && Fires(ambientArr);
                        int c = 0; foreach (var u in utterances) if (Fires(u)) c++;
                        caughtAt[k] = c;
                    }
                }, ct).ConfigureAwait(false);
                var tbl = string.Join("  ", candidates.Select((t, k) => $"{t:0.00}:{caughtAt[k]}/{n}{(ambientAt[k] ? "!amb" : "")}"));
                Log.Information("SherpaWakeService: calibration sweep utts={Utt} ambientMs={Amb} needed={Need} | {Table}",
                    n, ambientUsable ? ambientArr.Length * 1000 / SampleRate : 0, needed, tbl);
                int pick = -1;
                for (int k = 0; k < candidates.Length; k++) if (caughtAt[k] >= needed && !ambientAt[k]) { pick = k; break; }
                bool ambientRisk = false;
                if (pick < 0)
                {
                    for (int k = 0; k < candidates.Length; k++) if (caughtAt[k] >= needed) { pick = k; ambientRisk = true; break; }
                }
                if (pick < 0)
                {
                    int best = 0; for (int k = 1; k < candidates.Length; k++) if (caughtAt[k] > caughtAt[best]) best = k;
                    pick = best; ambientRisk = ambientAt[best];
                }
                double chosen = candidates[pick];
                int caught = caughtAt[pick];
                if (caught < 2)
                {
                    return new CalibrationResult { Message = $"Only caught {caught}/{n} clearly — didn't change anything. Try again: say “Hey Bambi” a bit louder, with a clear pause between each." };
                }
                string msg = caught >= needed && !ambientRisk
                    ? $"Calibrated to your voice — caught {caught}/{n} at sensitivity {chosen:0.00}."
                    : ambientRisk
                        ? $"Calibrated (recall-biased) — caught {caught}/{n} at sensitivity {chosen:0.00}. It may occasionally wake on background noise; re-run somewhere quieter to tighten it."
                        : $"Set sensitivity {chosen:0.00} — caught {caught}/{n}. Re-run for a better fit (say it clearly, pausing between each).";
                ct.ThrowIfCancellationRequested();
                return ApplyAndReturn(chosen, n, caught, msg);
            }
            catch (OperationCanceledException) { return new CalibrationResult { Message = "Calibration cancelled." }; }
            finally
            {
                lock (_gate)
                {
                    _activeCts = null;
                    Interlocked.Exchange(ref _sessionActive, 0);
                    if (_disposed) { _spotter?.Dispose(); _spotter = null; }
                }
            }
        }
        private CalibrationResult ApplyAndReturn(double threshold, int utt, int caught, string msg)
        {
            try
            {
                CoreSettings.Current.SpeechWakeThreshold = threshold;
                CoreSettings.Save();
            }
            catch (Exception ex) { Log.Warning(ex, "SherpaWakeService: failed to persist calibrated threshold"); }
            ResetInitState(); // rebuild the engine with the new threshold on next IsAvailable
            Log.Information("SherpaWakeService: calibrated threshold={Thr:0.000} (caught {Caught}/{Utt})", threshold, caught, utt);
            return new CalibrationResult { Success = true, ChosenThreshold = threshold, Utterances = utt, CaughtAtChosen = caught, Message = msg };
        }
        private KeywordSpotter BuildSpotter(ModelFiles m, float threshold, float boost)
        {
            var config = new KeywordSpotterConfig();
            config.FeatConfig.SampleRate = SampleRate;
            config.FeatConfig.FeatureDim = FeatureDim;
            config.ModelConfig.Transducer.Encoder = m.Encoder;
            config.ModelConfig.Transducer.Decoder = m.Decoder;
            config.ModelConfig.Transducer.Joiner = m.Joiner;
            config.ModelConfig.Tokens = m.Tokens;
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.NumThreads = 1;
            config.KeywordsFile = m.Keywords;
            config.KeywordsThreshold = threshold;
            config.KeywordsScore = boost;
            return new KeywordSpotter(config);
        }
        private static bool SpotFires(KeywordSpotter spk, float[] audio)
        {
            var s = spk.CreateStream();
            try
            {
                s.AcceptWaveform(SampleRate, audio);
                s.AcceptWaveform(SampleRate, new float[SampleRate / 3]); // trailing silence to flush
                s.InputFinished();
                while (spk.IsReady(s))
                {
                    spk.Decode(s);
                    if (!string.IsNullOrEmpty(spk.GetResult(s).Keyword)) return true;
                }
                return false;
            }
            finally { try { s.Dispose(); } catch { } }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            lock (_gate)
            {
                StopListening();
                if (Volatile.Read(ref _sessionActive) == 0)
                {
                    try { _spotter?.Dispose(); } catch { }
                    _spotter = null;
                }
            }
        }
    }
}
