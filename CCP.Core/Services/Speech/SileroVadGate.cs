// Adapted from WPF SileroVadGate at e138d8bfc84cee4008eaf78a91156fb6b8563110.
using System;
using System.IO;
using System.Linq;
using SherpaOnnx;
using Serilog;

namespace ConditioningControlPanel.Services.Speech
{
    internal sealed class SileroVadGate : IDisposable
    {
        private readonly VoiceActivityDetector _vad;
        private bool _disposed;
        public static string ModelRoot =>
            Path.Combine(AppContext.BaseDirectory, "Resources", "Models", "silero-vad");

        private SileroVadGate(VoiceActivityDetector vad) => _vad = vad;
        public static string? FindModel()
        {
            try
            {
                if (!Directory.Exists(ModelRoot)) return null;
                return Directory.EnumerateFiles(ModelRoot, "*.onnx", SearchOption.AllDirectories)
                    .OrderByDescending(f => Path.GetFileName(f).Contains("silero", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
            }
            catch { return null; }
        }
        public static SileroVadGate? TryCreate(int sampleRate)
        {
            var model = FindModel();
            if (model == null) return null;
            try
            {
                var config = new VadModelConfig();
                config.SileroVad.Model = model;
                config.SileroVad.Threshold = 0.25f;
                config.SileroVad.MinSilenceDuration = 0.6f;
                config.SileroVad.MinSpeechDuration = 0.1f;
                config.SileroVad.WindowSize = 512; // fixed by the silero model at 16 kHz
                config.SileroVad.MaxSpeechDuration = 10.0f;
                config.SampleRate = sampleRate;
                config.NumThreads = 1;
                config.Provider = "cpu";
                config.Debug = 0;
                var vad = new VoiceActivityDetector(config, bufferSizeInSeconds: 10.0f);
                return new SileroVadGate(vad);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SileroVadGate: init failed — falling back to the energy gate");
                return null;
            }
        }
        public bool Update(float[] samples)
        {
            if (_disposed) return true; // never swallow audio after teardown
            try
            {
                _vad.AcceptWaveform(samples);
                while (!_vad.IsEmpty()) _vad.Pop();
                return _vad.IsSpeechDetected();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "SileroVadGate: update failed — passing audio through");
                return true; // fail-open: worse to drop the user's speech than to skip gating
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _vad.Dispose(); } catch { }
        }
    }
}
