using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Speech;
using SherpaOnnx;

namespace ConditioningControlPanel.Avalonia.Platform
{
    // One native spotter and at most one microphone capture at a time.
    internal sealed class SherpaWakeSpotter : IDisposable
    {
        private readonly object _gate = new();
        private readonly string _root;
        private readonly Func<SherpaWakeService.ModelFiles, float, float, IDisposable> _factory;
        private IDisposable? _spotter;
        private string? _fingerprint;
        private string? _failedFingerprint;
        private bool _disposed;
        private bool _active;
        private bool _listening;
        private readonly Func<int, (Func<float[], bool> Update, IDisposable Lifetime)?> _vadFactory;
        private readonly Func<IDisposable, IDisposable> _createStream;
        private readonly Func<IDisposable, IDisposable, float[], bool> _decode;
        internal event EventHandler<bool>? ListeningChanged;
        internal bool IsConfigured => SherpaWakeService.FindModel(_root) != null;
        internal bool IsListening { get { lock (_gate) return _listening; } }

        internal SherpaWakeSpotter(string root, Func<SherpaWakeService.ModelFiles, float, float, IDisposable>? factory = null,
            Func<IDisposable, IDisposable>? createStream = null, Func<IDisposable, IDisposable, float[], bool>? decode = null,
            Func<int, (Func<float[], bool> Update, IDisposable Lifetime)?>? vadFactory = null)
        {
            _root = root;
            _factory = factory ?? BuildSpotter;
            _createStream = createStream ?? (spotter => ((KeywordSpotter)spotter).CreateStream());
            _decode = decode ?? Decode;
            _vadFactory = vadFactory ?? (rate => SileroVadGate.TryCreate(rate) is { } vad ? (vad.Update, vad) : null);
        }

        internal bool IsAvailable(bool hasDevice, double threshold, double boost)
        {
            lock (_gate)
            {
                if (_disposed || !hasDevice) return false;
                if (_active) return _spotter != null;
                var model = SherpaWakeService.FindModel(_root);
                if (model is not { } m) return false;
                float t = (float)Math.Clamp(threshold, 0.02, 0.6);
                float b = (float)Math.Clamp(boost, 0.0, 5.0);
                var fp = string.Join("|", m.Encoder, m.Decoder, m.Joiner, m.Tokens, m.Keywords,
                    t.ToString("0.000", CultureInfo.InvariantCulture), b.ToString("0.0", CultureInfo.InvariantCulture),
                    File.GetLastWriteTimeUtc(m.Keywords).Ticks);
                if (_spotter != null && _fingerprint == fp) return true;
                if (_failedFingerprint == fp) return false;
                _spotter?.Dispose();
                _spotter = null;
                try
                {
                    _spotter = _factory(m, t, b);
                    _fingerprint = fp;
                    _failedFingerprint = null;
                    return true;
                }
                catch (Exception)
                {
                    _fingerprint = null;
                    _failedFingerprint = fp;
                    return false;
                }
            }
        }

        internal void ResetInitState()
        {
            lock (_gate)
            {
                if (_active) { _failedFingerprint = null; return; }
                _spotter?.Dispose();
                _spotter = null;
                _fingerprint = null;
                _failedFingerprint = null;
            }
        }

        // The mic handle is closed before the stream and engine can be freed. The callback and
        // teardown share the same gate, so a late PCM callback cannot touch a disposed stream.
        internal async Task<bool> WaitForWakeAsync(IMicSource mic, double threshold, double boost, CancellationToken ct, bool noiseSuppression = false, double gateFactor = 4.0)
        {
            IDisposable? stream;
            lock (_gate)
            {
                if (_active || ct.IsCancellationRequested || !IsAvailable(mic.HasDevice, threshold, boost)) return false;
                try { stream = _createStream(_spotter!); }
                catch { return false; }
                _active = true;
            }
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IDisposable? capture = null;
            var filter = noiseSuppression ? new HighPassFilter(16000) : null;
            var vad = noiseSuppression ? _vadFactory(16000) : null;
            var gate = noiseSuppression && vad == null ? new NoiseGate(gateFactor, hangoverFrames: 12) : null;
            var preRoll = noiseSuppression ? new PreRollBuffer(16000) : null;
            using var registration = ct.Register(() => done.TrySetResult(false));
            try
            {
                if (ct.IsCancellationRequested) return false;
                capture = mic.Start((bytes, count) =>
                {
                    try
                    {
                        var samples = Pcm(bytes, count);
                        lock (_gate)
                        {
                            if (done.Task.IsCompleted) return;
                            filter?.ProcessInPlace(samples, samples.Length);
                            if (gate != null || vad != null)
                            {
                                double sum = 0;
                                foreach (var sample in samples) sum += sample * sample;
                                if (!(vad?.Update(samples) ?? gate!.Update(samples.Length == 0 ? 0 : Math.Sqrt(sum / samples.Length))))
                                {
                                    preRoll!.Push(samples);
                                    return;
                                }
                                foreach (var held in preRoll!.Drain())
                                    if (_decode(_spotter!, stream!, held)) { done.TrySetResult(true); return; }
                            }
                            if (_decode(_spotter!, stream!, samples)) done.TrySetResult(true);
                        }
                    }
                    catch { done.TrySetResult(false); }
                });
                lock (_gate) _listening = true;
                try { ListeningChanged?.Invoke(this, true); } catch { }
                return await done.Task.ConfigureAwait(false);
            }
            catch { return false; }
            finally
            {
                done.TrySetResult(false);
                try { capture?.Dispose(); } catch { }
                lock (_gate)
                {
                    try { stream?.Dispose(); } catch { }
                    try { vad?.Lifetime.Dispose(); } catch { }
                    _active = false;
                    _listening = false;
                    if (_disposed) { _spotter?.Dispose(); _spotter = null; }
                }
                try { ListeningChanged?.Invoke(this, false); } catch { }
            }
        }

        private static float[] Pcm(byte[] bytes, int count)
        {
            var samples = new float[Math.Min(count, bytes.Length) / 2];
            for (int i = 0; i < samples.Length; i++) samples[i] = (short)(bytes[2 * i] | bytes[2 * i + 1] << 8) / 32768f;
            return samples;
        }

        // WPF SherpaWakeService.CalibrateAsync (SherpaWakeService.cs:394-635): record says endpointed on
        // silence plus room tone, sweep the thresholds, and return the pick. The caller frees the mic first.
        internal async Task<(double? Threshold, string Message)> CalibrateAsync(IMicSource mic, double boost, bool highPass,
            IProgress<(bool Analyze, int Captured)>? progress, CancellationToken ct, int target = 5)
        {
            SherpaWakeService.ModelFiles m;
            lock (_gate)
            {
                if (_disposed || SherpaWakeService.FindModel(_root) is not { } model) return (null, Loc.Get("she_wake_cal_no_model"));
                if (_active || !mic.HasDevice) return (null, Loc.Get("she_wake_cal_busy"));
                (m, _active) = (model, true);
            }
            var says = new List<float[]>(); var ambient = new List<float>(); var cur = new List<float>();
            var preRoll = new PreRollBuffer(16000); var filter = highPass ? new HighPassFilter(16000) : null;
            double floor = double.MaxValue; int seeded = 0, silentMs = 0, held = 0; bool speaking = false;
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cap.CancelAfter(TimeSpan.FromSeconds(40)); // a silent user must not hang the flow
            IDisposable? capture = null;
            void Release()
            {
                try { Interlocked.Exchange(ref capture, null)?.Dispose(); } catch { }
                bool was;
                lock (_gate) { was = _listening; _listening = false; }
                if (was) try { ListeningChanged?.Invoke(this, false); } catch { }
            }
            // A borrower's StopVoiceInput cancels on its own thread: the mic is free when Cancel returns.
            using var registration = cap.Token.Register(() => { done.TrySetResult(false); Release(); });
            try
            {
                if (!done.Task.IsCompleted) capture = mic.Start((bytes, count) => { lock (says) {
                    if (done.Task.IsCompleted) return;
                    var buf = Pcm(bytes, count); filter?.ProcessInPlace(buf, buf.Length);
                    double sum = 0; foreach (var x in buf) sum += x * x;
                    double rms = buf.Length == 0 ? 0 : Math.Sqrt(sum / buf.Length);
                    if (seeded < 4800)
                    {
                        floor = Math.Min(floor, rms); seeded += buf.Length; preRoll.Push(buf);
                        if (seeded >= 4800) floor = Math.Clamp(floor, .0005, .05);
                        return;
                    }
                    // WPF SherpaWakeService.cs:485-486: the floor drifts with the room between says.
                    if (!speaking) floor = Math.Min(floor * 1.02 + 1e-5, Math.Max(floor, rms));
                    if (rms < floor * 1.5) floor = .9 * floor + .1 * rms;
                    double onset = Math.Clamp(floor * 4, .006, .08);
                    if (!speaking)
                    {
                        if (rms < onset * .6 && ambient.Count < 32000) ambient.AddRange(buf);
                        if (rms < onset) { preRoll.Push(buf); return; }
                        (speaking, silentMs) = (true, 0); cur.Clear();
                        foreach (var h in preRoll.Drain()) cur.AddRange(h);
                        held = cur.Count;
                    }
                    cur.AddRange(buf);
                    silentMs = rms < onset * .6 ? silentMs + buf.Length / 16 : 0;
                    int spokenMs = (cur.Count - held) / 16;
                    if (silentMs < 480 && spokenMs < 2500) return;
                    speaking = false;
                    if (spokenMs - silentMs >= 250) { says.Add(cur.ToArray()); progress?.Report((false, says.Count)); }
                    if (says.Count >= target) done.TrySetResult(true);
                } });
                lock (_gate) _listening = capture != null; // WPF SherpaWakeService.cs:541-547: the mic pill stays lit while calibration holds the mic
                if (capture != null) try { ListeningChanged?.Invoke(this, true); } catch { }
                await done.Task.ConfigureAwait(false);
                Release();
                lock (says) done.TrySetResult(false); // waits out an in-flight frame before the lists are read
                if (ct.IsCancellationRequested) return (null, Loc.Get("she_wake_cal_cancelled"));
                if (says.Count < 2) return (null, Loc.GetF("she_wake_cal_few_heard", says.Count));
                progress?.Report((true, says.Count));
                var amb = ambient.Count > 8000 ? ambient.ToArray() : null; // ~0.5s+ of room tone to judge
                var caught = new int[SherpaWakeService.CalibrationCandidates.Length]; var fired = new bool[caught.Length];
                await Task.Run(() =>
                {
                    bool Fires(IDisposable spk, float[] audio)
                    {
                        using var stream = _createStream(spk);
                        return _decode(spk, stream, audio) || _decode(spk, stream, new float[16000 / 3]); // trailing silence flushes
                    }
                    for (int k = 0; k < caught.Length && !ct.IsCancellationRequested; k++)
                    {
                        using var spk = _factory(m, (float)SherpaWakeService.CalibrationCandidates[k], (float)Math.Clamp(boost, 0.0, 5.0));
                        fired[k] = amb != null && Fires(spk, amb);
                        foreach (var say in says) if (!ct.IsCancellationRequested && Fires(spk, say)) caught[k]++;
                    }
                }).ConfigureAwait(false);
                // Stop or panic during the sweep: a half-swept table must not be saved as the sensitivity.
                if (ct.IsCancellationRequested) return (null, Loc.Get("she_wake_cal_cancelled"));
                if (SherpaWakeService.SelectCalibrationThreshold(says.Count, caught, fired) is not { } pick)
                    return (null, Loc.GetF("she_wake_cal_few_caught", caught.Max(), says.Count));
                var (t, c, risk) = pick;
                // WPF SherpaWakeService.cs:625-629: below 70% of the says is a partial fit, not a calibration.
                bool fit = c >= Math.Max(2, (int)Math.Ceiling(says.Count * 0.7));
                return (t, Loc.GetF(risk ? "she_wake_cal_done_noisy" : fit ? "she_wake_cal_done" : "she_wake_cal_partial", c, says.Count, t));
            }
            finally
            {
                Release();
                lock (_gate) { _active = false; if (_disposed) { _spotter?.Dispose(); _spotter = null; } }
            }
        }

        private static bool Decode(IDisposable engine, IDisposable stream, float[] samples)
        {
            var spotter = (KeywordSpotter)engine;
            var online = (OnlineStream)stream;
            online.AcceptWaveform(16000, samples);
            while (spotter.IsReady(online))
            {
                spotter.Decode(online);
                if (!string.IsNullOrEmpty(spotter.GetResult(online).Keyword)) return true;
            }
            return false;
        }

        private static IDisposable BuildSpotter(SherpaWakeService.ModelFiles m, float threshold, float boost)
        {
            var config = new KeywordSpotterConfig();
            config.FeatConfig.SampleRate = 16000;
            config.FeatConfig.FeatureDim = 80;
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

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (!_active) ResetInitState();
            }
        }
    }
}
