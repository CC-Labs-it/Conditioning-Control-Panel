// Adapted from WPF Services/AudioSyncService.cs on the verified base; the decoder is supplied by the head.
using System;
using System.Threading;
using System.Threading.Tasks;

using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Audio;
using Serilog;

namespace ConditioningControlPanel.Services
{
    public class BrowserAudioSyncService : IDisposable
    {
        private readonly HapticService _hapticService;
        private readonly AudioSyncSettings _settings;
        private readonly Func<string, TimeSpan, TimeSpan, CancellationToken, float[]> _read;
        private CancellationTokenSource? _session;
        private int _generation;
        private ChunkManager? _chunkManager;

        private bool _disposed;
        private bool _isProcessing;
        private bool _isPaused;
        private bool _isWaitingForChunk;
        private TimeSpan _lastPlaybackPosition;
        private DateTime _lastSyncTime;
        private DateTime _lastResyncTime;
        private const int RESYNC_INTERVAL_MS = 5000; // Force resync every 5 seconds

        public event EventHandler<string>? ProcessingStarted;

        public event EventHandler<ChunkProgressEventArgs>? ProcessingProgress;

        public event EventHandler? ProcessingCompleted;

        public event EventHandler<string>? Error;

        public event EventHandler<int>? ChunkLoadingRequired;

        public event EventHandler? ChunkLoadingCompleted;

        public bool IsEnabled => _settings.Enabled;

        public bool IsProcessing => _isProcessing;

        public bool IsReadyToPlay => _chunkManager?.IsFirstChunkReady ?? false;

        public bool IsWaitingForChunk => _isWaitingForChunk;

        public BrowserAudioSyncService(HapticService hapticService, AudioSyncSettings settings,
            Func<string, TimeSpan, TimeSpan, CancellationToken, float[]> read)
        {
            _hapticService = hapticService;
            _settings = settings;
            _read = read;
        }

        internal static TimeSpan? ParseReportedDuration(string? message)
        {
            if (string.IsNullOrEmpty(message)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(message, "\"duration\":([0-9]+(?:\\.[0-9]+)?)");
            if (!m.Success) return null;
            if (!double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                return null;
            if (seconds <= 0 || seconds > 86400) return null;
            return TimeSpan.FromSeconds(seconds);
        }

        public async Task OnVideoDetectedAsync(string videoUrl, TimeSpan? duration = null)
        {
            Reset();
            if (_disposed) return;
            var generation = _generation;
            if (!_settings.Enabled || !_hapticService.IsConnected || !VideoDownloader.IsLikelyVideoUrl(videoUrl))
            {
                ProcessingCompleted?.Invoke(this, EventArgs.Empty);
                return;
            }
            using var session = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            _session = session;
            var manager = new ChunkManager(_settings, _read);
            _chunkManager = manager;
            manager.Progress += (_, e) => { if (generation == _generation) ProcessingProgress?.Invoke(this, e); };
            try
            {
                _isProcessing = true;
                ProcessingStarted?.Invoke(this, "Preparing haptic sync...");
                manager.Initialize(videoUrl, duration);
                await manager.StartFirstChunkAsync(session.Token);
                if (!manager.IsFirstChunkReady && !session.IsCancellationRequested)
                    throw new AudioSyncException("Failed to prepare first chunk");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Warning(ex, "Browser audio sync preparation failed");
                if (generation == _generation) Error?.Invoke(this, "Failed to prepare haptic sync");
            }
            finally
            {
                if (generation == _generation)
                {
                    _session = null;
                    _isProcessing = false;
                    ProcessingCompleted?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void OnPlaybackStateUpdate(double currentTimeSeconds, bool paused)
        {
            if (_disposed || !_settings.Enabled || _chunkManager == null)
                return;

            if (!double.IsFinite(currentTimeSeconds) || currentTimeSeconds < 0 || currentTimeSeconds > 86400) return;
            var currentTime = TimeSpan.FromSeconds(currentTimeSeconds);

            if (paused && !_isPaused)
            {
                _isPaused = true;

                _ = _hapticService.StopAsync();
                Log.Debug("AudioSyncService: Playback paused at {Time}", currentTime);
                return;
            }

            if (!paused && _isPaused)
            {
                _isPaused = false;
                _lastResyncTime = DateTime.UtcNow; // Reset resync timer on resume
                Log.Debug("AudioSyncService: Playback resumed at {Time}", currentTime);
            }

            if (paused || _isWaitingForChunk)
                return;

            _lastPlaybackPosition = currentTime;
            _lastSyncTime = DateTime.UtcNow;

            _chunkManager.CheckBufferAndProcess(currentTime);

            var now = DateTime.UtcNow;
            var needsResync = (now - _lastResyncTime).TotalMilliseconds >= RESYNC_INTERVAL_MS;

            TimeSpan lookAheadTime;
            if (needsResync)
            {
                lookAheadTime = currentTime;
                _lastResyncTime = now;
                Log.Debug("AudioSyncService: Forced resync at {Time}", currentTime);
            }
            else
            {
                var latencyMs = 300 + _hapticService.ProviderLatencyMs + _settings.ManualLatencyOffsetMs;
                lookAheadTime = currentTime + TimeSpan.FromMilliseconds(latencyMs);
            }

            var track = _chunkManager.Track;
            if (!track.HasDataForTime(lookAheadTime))
            {
                Log.Debug("AudioSyncService: No haptic data for time {Time} - chunk not loaded", lookAheadTime);
                return;
            }

            var intensity = track.GetIntensityAt(lookAheadTime);

            if (_settings.BandSplit && _hapticService.MaxVibrateMotors >= 2 &&
                _chunkManager.LowBandTrack.HasDataForTime(lookAheadTime))
            {
                var low = _chunkManager.LowBandTrack.GetIntensityAt(lookAheadTime);
                var high = _chunkManager.HighBandTrack.GetIntensityAt(lookAheadTime);
                _ = SendHapticBandsAsync(low, high);
                return;
            }

            _ = SendHapticAsync(intensity);
        }

        public void OnVideoSeek(double newTimeSeconds)
        {
            if (_disposed || !_settings.Enabled || _chunkManager == null)
                return;

            if (!double.IsFinite(newTimeSeconds) || newTimeSeconds < 0 || newTimeSeconds > 86400) return;
            var newTime = TimeSpan.FromSeconds(newTimeSeconds);
            Log.Information("AudioSyncService: User seeked to {Time}", newTime);

            _lastPlaybackPosition = newTime;
            _lastResyncTime = DateTime.UtcNow; // Reset resync timer on seek

            var chunkIndex = _chunkManager.GetChunkIndexForTime(newTime);

            if (!_chunkManager.IsChunkReady(chunkIndex))
            {
                _ = LoadChunkForSeekAsync(chunkIndex);
            }
        }

        private async Task LoadChunkForSeekAsync(int chunkIndex)
        {
            if (_chunkManager == null) return;
            var generation = _generation;
            var manager = _chunkManager;

            _isWaitingForChunk = true;
            ChunkLoadingRequired?.Invoke(this, chunkIndex);

            try
            {
                await manager.EnsureChunkReadyAsync(chunkIndex);

                Log.Information("AudioSyncService: Chunk {Index} loaded after seek", chunkIndex);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AudioSyncService: Failed to load chunk {Index} after seek", chunkIndex);
                Error?.Invoke(this, $"Failed to load haptic data: {ex.Message}");
            }
            finally
            {
                if (generation == _generation)
                {
                    _isWaitingForChunk = false;
                    ChunkLoadingCompleted?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void OnVideoEnded()
        {
            Log.Information("AudioSyncService: Video ended");
            StopSync();
        }

        public void StopSync()
        {
            _generation++;
            _session?.Cancel();
            _session = null;
            _isProcessing = false;
            _isPaused = false;
            _isWaitingForChunk = false;
            _ = _hapticService.StopAsync();

            _chunkManager?.Stop();
        }

        public void Reset()
        {
            StopSync();
            _chunkManager?.Dispose();
            _chunkManager = null;
            _isProcessing = false;
        }

        private async Task SendHapticAsync(float intensity)
        {
            try
            {
                await _hapticService.SetSyncIntensityAsync(ApplyLivePower(intensity));
            }
            catch (Exception ex)
            {
                Log.Debug("AudioSyncService: Failed to send haptic: {Error}", ex.Message);
            }
        }

        private async Task SendHapticBandsAsync(float low, float high)
        {
            try
            {
                await _hapticService.SetSyncIntensityAsync(ApplyLivePower(low), ApplyLivePower(high));
            }
            catch (Exception ex)
            {
                Log.Debug("AudioSyncService: Failed to send band-split haptic: {Error}", ex.Message);
            }
        }

        private float ApplyLivePower(float intensity)
        {
            const float minDeviceIntensity = 0.08f;
            var liveIntensity = (float)_settings.LiveIntensity;

            if (intensity <= 0.01f || liveIntensity <= 0)
            {
                return 0;
            }
            if (liveIntensity <= minDeviceIntensity)
            {
                return intensity * liveIntensity;
            }
            return minDeviceIntensity + intensity * (liveIntensity - minDeviceIntensity);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Reset();
        }
    }
}
