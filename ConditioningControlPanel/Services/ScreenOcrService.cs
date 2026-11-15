using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Services.Ocr;
using ConditioningControlPanel.Services.ScreenCapture;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace ConditioningControlPanel.Services
{
    public class ScreenOcrService : IDisposable
    {
        private const int ConfirmationDelayMs = 200;

        /// <summary>
        /// Upper bound on quick follow-up scans per tick. With OcrConfirmationScans up
        /// to 3 a single discovery scan plus two quick confirms is enough to fire
        /// without waiting a full interval; the cap stops a flickering candidate from
        /// spinning the confirm loop indefinitely.
        /// </summary>
        private const int MaxQuickConfirmScans = 3;

        private Timer? _timer;
        private bool _disposed;
        private bool _isRunning;
        private bool _scanInProgress;
        private readonly IScreenOcrEngine _ocrEngine;
        private readonly object _lock = new();

        /// <summary>True when the OCR models are available and the engine is usable.</summary>
        internal bool IsOcrAvailable => _ocrEngine.IsAvailable;

        public ScreenOcrService() : this(new RapidScreenOcrEngine()) { }

        internal ScreenOcrService(IScreenOcrEngine engine)
        {
            _ocrEngine = engine;
            if (!engine.IsAvailable)
                App.Logger?.Warning("ScreenOcrService: OCR models unavailable");
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning || !_ocrEngine.IsAvailable) return;

                var intervalMs = App.Settings?.Current?.ScreenOcrIntervalMs ?? 3000;
                _timer = new Timer(OnTimerTick, null, intervalMs, intervalMs);
                _isRunning = true;
                App.Logger?.Information("ScreenOcrService started (interval: {Interval}ms)", intervalMs);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!_isRunning) return;
                _timer?.Dispose();
                _timer = null;
                _isRunning = false;
                App.Logger?.Debug("ScreenOcrService stopped");
            }
        }

        public void UpdateInterval(int intervalMs)
        {
            lock (_lock)
            {
                if (!_isRunning || _timer == null) return;
                _timer.Change(intervalMs, intervalMs);
            }
        }

        private async void OnTimerTick(object? state)
        {
            if (_disposed || !_isRunning || !_ocrEngine.IsAvailable) return;
            if (_scanInProgress) return;
            _scanInProgress = true;

            try
            {
                // Discovery scan — runs at the regular interval
                var allWords = await ScanAllScreensAsync();
                if (_disposed) return;

                await DispatchOcrResultsAsync(allWords);

                // Quick confirmation scans: while the discovery scan found keyword
                // candidates still building their consecutive-scan streak, re-scan
                // after a short delay (instead of waiting a full interval tick) until
                // they fire, disappear, or we hit the quick-scan cap.
                int quickScans = 0;
                while (!_disposed
                       && App.KeywordTriggers?.NeedsOcrConfirmation == true
                       && quickScans < MaxQuickConfirmScans)
                {
                    quickScans++;
                    await Task.Delay(ConfirmationDelayMs);
                    if (_disposed) return;

                    var confirmWords = await ScanAllScreensAsync();
                    if (_disposed) return;

                    await DispatchOcrResultsAsync(confirmWords);
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ScreenOcrService: Scan error: {Error}", ex.Message);
            }
            finally
            {
                _scanInProgress = false;
            }
        }

        private async Task<List<OcrWordHit>> ScanAllScreensAsync()
        {
            var allWords = new List<OcrWordHit>();
            var screens = App.GetAllScreensCached();

            foreach (var screen in screens)
            {
                var (_, words) = await CaptureAndRecognizeAsync(screen);
                if (words != null)
                    allWords.AddRange(words);
            }

            return allWords;
        }

        private async Task DispatchOcrResultsAsync(List<OcrWordHit> words)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;

            // Self-exclusion: drop any OCR hits that fall inside our own UI so the app
            // cannot react to its own output (subliminal flashes, avatar bubbles, the
            // keyword editor textbox, etc.). Controlled by AwarenessIgnoreOwnUi setting.
            var filtered = words;
            if (App.Settings?.Current?.AwarenessIgnoreOwnUi == true && words.Count > 0)
            {
                var ccpRects = App.GetCcpWindowRectsCached();
                if (ccpRects.Length > 0)
                {
                    filtered = new List<OcrWordHit>(words.Count);
                    int dropped = 0;
                    foreach (var w in words)
                    {
                        bool insideCcp = false;
                        for (int i = 0; i < ccpRects.Length; i++)
                        {
                            if (ccpRects[i].IntersectsWith(w.ScreenRect))
                            {
                                insideCcp = true;
                                break;
                            }
                        }
                        if (!insideCcp) filtered.Add(w);
                        else dropped++;
                    }

                    // Diagnostic: log how many hits got dropped + the CCP rects
                    // being used to filter, so over-aggressive filtering is visible.
                    // Debug, not Information: this fires on EVERY scan and buried the
                    // triage-relevant lines in the activity log.
                    if (dropped > 0 && App.Logger?.IsEnabled(Serilog.Events.LogEventLevel.Debug) == true)
                    {
                        var rectSb = new System.Text.StringBuilder();
                        for (int i = 0; i < ccpRects.Length; i++)
                        {
                            if (i > 0) rectSb.Append(' ');
                            var r = ccpRects[i];
                            rectSb.Append('(').Append(r.X).Append(',').Append(r.Y)
                                  .Append(' ').Append(r.Width).Append('x').Append(r.Height).Append(')');
                        }
                        App.Logger.Debug(
                            "OCR self-exclusion: dropped {Dropped}/{Total} words inside {N} CCP rect(s): {Rects}",
                            dropped, words.Count, ccpRects.Length, rectSb.ToString());
                    }
                }
            }

            await dispatcher.InvokeAsync(() =>
            {
                if (_disposed) return;
                App.KeywordTriggers?.CheckOcrWords(filtered);
            });
        }

        private Task<(string? text, List<OcrWordHit>? words)> CaptureAndRecognizeAsync(WinForms.Screen screen)
        {
            var bounds = screen.Bounds;
            try
            {
                // GDI writes directly into the array consumed by Core, without a bitmap conversion.
                var pixels = new byte[checked(bounds.Width * bounds.Height * 4)];
                var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    using var bitmap = new Bitmap(bounds.Width, bounds.Height, bounds.Width * 4,
                        PixelFormat.Format32bppArgb, pin.AddrOfPinnedObject());
                    using (var g = Graphics.FromImage(bitmap))
                        g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                    // GDI does not populate alpha; the captured pixels are opaque.
                    for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                    return Task.FromResult(Recognize(pixels, bounds, screen));
                }
                finally { pin.Free(); }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("ScreenOcrService: Capture failed for {Screen}: {Error}", screen.DeviceName, ex.Message);
                return Task.FromResult<(string?, List<OcrWordHit>?)>((null, null));
            }
        }

        // Kept for existing callers that exercise recognition with synthetic bitmaps.
        internal Task<(string? text, List<OcrWordHit>? words)> DecodeAndRecognizeAsync(
            Bitmap bitmap, Rectangle bounds, WinForms.Screen? screen)
        {
            try
            {
                var pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
                var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < bitmap.Height; y++)
                        Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width * 4, bitmap.Width * 4);
                }
                finally { bitmap.UnlockBits(data); }
                return Task.FromResult(Recognize(pixels, bounds, screen));
            }
            finally { bitmap.Dispose(); }
        }

        private (string? text, List<OcrWordHit>? words) Recognize(byte[] pixels, Rectangle bounds, WinForms.Screen? screen)
        {
            if (!_ocrEngine.IsAvailable) return (null, null);
            var frame = new ScreenFrame(screen?.DeviceName ?? "", bounds, 1, bounds.Width, bounds.Height,
                bounds.Width * 4, ScreenPixelFormat.Bgra32, pixels);
            var hits = new List<OcrWordHit>();
            foreach (var word in _ocrEngine.Recognize(frame))
                hits.Add(new OcrWordHit { Text = word.Text, ScreenRect = frame.ToScreen(word.Bounds), Screen = screen! });
            return (string.Join(" ", hits.ConvertAll(w => w.Text)), hits);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            _ocrEngine.Dispose();
        }
    }
}
