using System;
using System.Globalization;
using System.IO;
using ConditioningControlPanel.Services.Speech;
using SherpaOnnx;

namespace ConditioningControlPanel.Avalonia.Platform
{
    // Owns only idle initialization. Capture and wake-loop integration are a separate port.
    internal sealed class SherpaWakeSpotter : IDisposable
    {
        private readonly object _gate = new();
        private readonly string _root;
        private readonly Func<SherpaWakeService.ModelFiles, float, float, IDisposable> _factory;
        private IDisposable? _spotter;
        private string? _fingerprint;
        private string? _failedFingerprint;
        private bool _disposed;

        internal SherpaWakeSpotter(string root, Func<SherpaWakeService.ModelFiles, float, float, IDisposable>? factory = null)
        {
            _root = root;
            _factory = factory ?? BuildSpotter;
        }

        internal bool IsAvailable(bool hasDevice, double threshold, double boost)
        {
            lock (_gate)
            {
                if (_disposed || !hasDevice) return false;
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
                _spotter?.Dispose();
                _spotter = null;
                _fingerprint = null;
                _failedFingerprint = null;
            }
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
                ResetInitState();
                _disposed = true;
            }
        }
    }
}
