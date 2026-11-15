using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using ConditioningControlPanel.Services.Ocr;
using ConditioningControlPanel.Services.ScreenCapture;
using Windows.Graphics.Imaging;
using OcrEngine = Windows.Media.Ocr.OcrEngine;

namespace ConditioningControlPanel.Services
{
    /// <summary>Preserves the installed Windows profile language when the Core Latin model cannot cover it.</summary>
    internal sealed class WindowsProfileOcrEngine : IScreenOcrEngine
    {
        private readonly OcrEngine _engine;
        private readonly object _gate = new();
        private bool _disposed;

        internal WindowsProfileOcrEngine(OcrEngine engine) => _engine = engine;
        public bool IsAvailable { get { lock (_gate) return !_disposed; } }

        public IReadOnlyList<OcrWord> Recognize(ScreenFrame frame, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposed || frame.Format != ScreenPixelFormat.Bgra32 || frame.Width <= 0 || frame.Height <= 0 ||
                    frame.Stride != (long)frame.Width * 4 || frame.Pixels.Length < (long)frame.Stride * frame.Height)
                    return Array.Empty<OcrWord>();
                try
                {
                    var segment = MemoryMarshal.TryGetArray(frame.Pixels, out var pixels)
                        ? pixels : new ArraySegment<byte>(frame.Pixels.ToArray());
                    using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
                        segment.Array!.AsBuffer(segment.Offset, frame.Stride * frame.Height),
                        BitmapPixelFormat.Bgra8, frame.Width, frame.Height, BitmapAlphaMode.Ignore);
                    var result = _engine.RecognizeAsync(bitmap).AsTask(cancellationToken).GetAwaiter().GetResult();
                    var words = new List<OcrWord>();
                    foreach (var line in result.Lines)
                        foreach (var word in line.Words)
                        {
                            var rect = word.BoundingRect;
                            words.Add(new OcrWord(word.Text, new Rectangle((int)rect.X, (int)rect.Y,
                                (int)rect.Width, (int)rect.Height)));
                        }
                    return words;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    App.Logger?.Debug("ScreenOcrService: Windows OCR failed: {Error}", ex.Message);
                    return Array.Empty<OcrWord>();
                }
            }
        }

        public void Dispose() { lock (_gate) _disposed = true; }
    }
}
