using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
                        var samples = new float[Math.Min(count, bytes.Length) / 2];
                        for (int i = 0; i < samples.Length; i++)
                            samples[i] = (short)(bytes[2 * i] | bytes[2 * i + 1] << 8) / 32768f;
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
