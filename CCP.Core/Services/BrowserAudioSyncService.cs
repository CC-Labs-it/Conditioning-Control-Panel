using System;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Audio;
using Serilog;

namespace ConditioningControlPanel.Services
{
    // Core counterpart of WPF Services/AudioSyncService.cs:103-424.
    // The head supplies decoding; browser transport wiring is a separate port.
    public sealed class BrowserAudioSyncService : IDisposable
    {
        private readonly HapticService _hapticService;
        private readonly AudioSyncSettings _settings;
        private readonly Func<string, TimeSpan, TimeSpan, CancellationToken, float[]> _read;
        private CancellationTokenSource? _session;
        private Task _pending = Task.CompletedTask;
        private int _generation;
        private ChunkManager? _chunkManager;

        private bool _disposed;
        private bool _stopped = true;
        private bool _isProcessing;
        private bool _isPaused;
        private bool _isWaitingForChunk;
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
            if (_disposed) return;
            if (!_settings.Enabled || !_hapticService.IsConnected)
            {
                Log.Debug("BrowserAudioSyncService: Skipping - Enabled={Enabled}, Connected={Connected}",
                    _settings.Enabled, _hapticService.IsConnected);
                ProcessingCompleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!VideoDownloader.IsLikelyVideoUrl(videoUrl))
            {
                Log.Debug("BrowserAudioSyncService: URL doesn't look like a video: {Host}", Logging.UrlLog.Host(videoUrl));
                ProcessingCompleted?.Invoke(this, EventArgs.Empty);
                return;
            }

            Log.Information("BrowserAudioSyncService: Video detected on {Host}, starting processing", Logging.UrlLog.Host(videoUrl));

            Reset();
            if (_disposed) return;
            _stopped = false;
            var generation = _generation;
            var session = new CancellationTokenSource();
            _session = session;
            try
            {
                _isProcessing = true;
                ProcessingStarted?.Invoke(this, "Preparing haptic sync...");

                var manager = new ChunkManager(_settings, _read);
                _chunkManager = manager;
                manager.ChunkReady += OnChunkReady;
                manager.Progress += (s, e) => { if (generation == _generation) OnChunkProgress(s, e); };
                manager.Error += (s, e) => { if (generation == _generation) OnChunkError(s, e); };
                manager.ChunkLoadingStarted += OnChunkLoadingStarted;

                manager.Initialize(videoUrl, duration);
                await (_pending = manager.StartFirstChunkAsync(session.Token));

                if (!manager.IsFirstChunkReady && !session.IsCancellationRequested)
                {
                    throw new AudioSyncException("Timeout waiting for first chunk to process");
                }

                Log.Information("BrowserAudioSyncService: First chunk ready, video can start playing");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log.Error(ex, "BrowserAudioSyncService: Failed to process video");
                if (generation == _generation) Error?.Invoke(this, $"Failed to prepare haptic sync: {ex.Message}");
            }
            finally
            {
                if (generation == _generation)
                {
                    _isProcessing = false;
                    ProcessingCompleted?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public void OnPlaybackStateUpdate(double currentTimeSeconds, bool paused)
        {
            if (_disposed || _stopped || !_settings.Enabled || _chunkManager == null)
                return;

            if (!double.IsFinite(currentTimeSeconds) || currentTimeSeconds < 0 || currentTimeSeconds > 86400) return;
            var currentTime = TimeSpan.FromSeconds(currentTimeSeconds);

            if (paused && !_isPaused)
            {
                _isPaused = true;

                _ = _hapticService.StopAsync();
                Log.Debug("BrowserAudioSyncService: Playback paused at {Time}", currentTime);
                return;
            }

            if (!paused && _isPaused)
            {
                _isPaused = false;
                _lastResyncTime = DateTime.UtcNow; // Reset resync timer on resume
                Log.Debug("BrowserAudioSyncService: Playback resumed at {Time}", currentTime);
            }

            if (paused || _isWaitingForChunk)
                return;

            _chunkManager.CheckBufferAndProcess(currentTime);

            var lookAheadTime = GetLookAhead(currentTime, DateTime.UtcNow);

            var track = _chunkManager.Track;
            if (!track.HasDataForTime(lookAheadTime))
            {
                Log.Debug("BrowserAudioSyncService: No haptic data for time {Time} - chunk not loaded", lookAheadTime);
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

        internal TimeSpan GetLookAhead(TimeSpan currentTime, DateTime now)
        {
            var needsResync = (now - _lastResyncTime).TotalMilliseconds >= RESYNC_INTERVAL_MS;

            TimeSpan lookAheadTime;
            if (needsResync)
            {
                lookAheadTime = currentTime;
                _lastResyncTime = now;
                Log.Debug("BrowserAudioSyncService: Forced resync at {Time}", currentTime);
            }
            else
            {
                var latencyMs = 300 + _hapticService.ProviderLatencyMs + _settings.ManualLatencyOffsetMs;
                lookAheadTime = currentTime + TimeSpan.FromMilliseconds(latencyMs);
            }

            return lookAheadTime;
        }

        public void OnVideoSeek(double newTimeSeconds)
        {
            if (_disposed || _stopped || !_settings.Enabled || _chunkManager == null)
                return;

            if (!double.IsFinite(newTimeSeconds) || newTimeSeconds < 0 || newTimeSeconds > 86400) return;
            var newTime = TimeSpan.FromSeconds(newTimeSeconds);
            Log.Information("BrowserAudioSyncService: User seeked to {Time}", newTime);

            _lastResyncTime = DateTime.UtcNow; // Reset resync timer on seek

            var chunkIndex = _chunkManager.GetChunkIndexForTime(newTime);

            if (!_chunkManager.IsChunkReady(chunkIndex))
            {
                _pending = Task.WhenAll(_pending, LoadChunkForSeekAsync(chunkIndex, _session!.Token));
            }
        }

        private async Task LoadChunkForSeekAsync(int chunkIndex, CancellationToken token)
        {
            if (_chunkManager == null) return;
            var generation = _generation;
            var manager = _chunkManager;

            _isWaitingForChunk = true;
            ChunkLoadingRequired?.Invoke(this, chunkIndex);

            try
            {
                await manager.EnsureChunkReadyAsync(chunkIndex, token);

                Log.Information("BrowserAudioSyncService: Chunk {Index} loaded after seek", chunkIndex);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "BrowserAudioSyncService: Failed to load chunk {Index} after seek", chunkIndex);
                if (generation == _generation) Error?.Invoke(this, $"Failed to load haptic data: {ex.Message}");
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
            Log.Information("BrowserAudioSyncService: Video ended");
            // WPF AudioSyncService.cs:322-339 keeps cached chunks replayable.
            _isPaused = false;
            _isWaitingForChunk = false;
            _ = _hapticService.StopAsync();
            _chunkManager?.Stop();
        }

        public void StopSync()
        {
            _stopped = true;
            _generation++;
            _session?.Cancel();
            _session?.Dispose();
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
            var manager = _chunkManager;
            _chunkManager = null;
            if (manager != null) _ = DisposeWhenIdleAsync(manager, _pending);
            _pending = Task.CompletedTask;
            _isProcessing = false;
        }

        private static async Task DisposeWhenIdleAsync(ChunkManager manager, Task pending)
        {
            try { await pending.ConfigureAwait(false); }
            catch (Exception) { } // Seek/prepare report errors through their existing events.
            finally { manager.Dispose(); }
        }

        private async Task SendHapticAsync(float intensity)
        {
            try
            {
                await _hapticService.SetSyncIntensityAsync(ApplyLivePower(intensity));
            }
            catch (Exception ex)
            {
                Log.Debug("BrowserAudioSyncService: Failed to send haptic: {Error}", ex.Message);
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
                Log.Debug("BrowserAudioSyncService: Failed to send band-split haptic: {Error}", ex.Message);
            }
        }

        internal float ApplyLivePower(float intensity)
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

        private void OnChunkReady(object? sender, AudioChunk chunk)
        {
            Log.Debug("BrowserAudioSyncService: Chunk {Index} ready", chunk.Index);
        }

        private void OnChunkProgress(object? sender, ChunkProgressEventArgs e)
        {
            ProcessingProgress?.Invoke(this, e);
        }

        private void OnChunkError(object? sender, string error)
        {
            Error?.Invoke(this, error);
        }

        private void OnChunkLoadingStarted(object? sender, int chunkIndex)
        {
            Log.Information("BrowserAudioSyncService: Chunk {Index} loading started", chunkIndex);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Reset();
        }
    }
}
