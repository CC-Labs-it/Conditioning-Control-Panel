using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using ConditioningControlPanel.Services.ScreenCapture;
using RapidOcrNet;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Ocr;

/// <param name="Bounds">Frame pixels; <see cref="ScreenFrame.ToScreen"/> maps them to the desktop.</param>
public readonly record struct OcrWord(string Text, Rectangle Bounds);

public interface IScreenOcrEngine : IDisposable
{
    /// <summary>False once the models are missing or failed to load; <see cref="Recognize"/> then returns no words.</summary>
    bool IsAvailable { get; }
    /// <summary>CPU-bound: call off the UI thread. Never throws except for cancellation.</summary>
    IReadOnlyList<OcrWord> Recognize(ScreenFrame frame, CancellationToken cancellationToken = default);
}

/// <summary>PP-OCRv5 through RapidOcrNet (https://github.com/BobLd/RapidOcrNet), CPU only, with the recognizer for the
/// UI language (WPF reads with the user's OCR languages: ScreenOcrService.cs:41 TryCreateFromUserProfileLanguages).
/// Models load on the first <see cref="Recognize"/> and on a language change, from &lt;app dir&gt;/models/v5 (CCP.Core.csproj).</summary>
public sealed class RapidScreenOcrEngine : IScreenOcrEngine
{
    // Two intra-op threads: a scan must never compete with the UI for every core.
    private const int Threads = 2;
    // DoAngle off: screen text is upright, so the 180 degree classifier per crop is wasted work (README "Key options":
    // "Disable if you know your text is upright"). RecMaxDegreeOfParallelism stays at its default 1.
    private static readonly RapidOcrOptions Options = RapidOcrOptions.Default with { ReturnWordBox = true, DoAngle = false };
    private readonly string _modelDir;
    private readonly Func<string?> _language;
    private readonly object _gate = new();
    private RapidOcr? _ocr;
    private string? _loadedRec;
    private bool _failed, _disposed;

    /// <param name="language">UI language code; defaults to <see cref="Localization.LocalizationManager"/>'s.</param>
    public RapidScreenOcrEngine(string? modelDir = null, Func<string?>? language = null)
    {
        _modelDir = Path.GetFullPath(modelDir ?? Path.Combine(AppContext.BaseDirectory, RapidOcr.ModelsFolderName, RapidOcr.ModelsVersion));
        _language = language ?? (() => Localization.LocalizationManager.Instance.CurrentLanguage);
    }

    // PP-OCRv5 per-script recognizers (RapidOCR default_models.yaml): "ch" reads Chinese and Japanese, "eslav" Russian;
    // the multilingual PP-OCRv6 has no Korean or Cyrillic. Det and cls are shared; every other UI language reads Latin.
    internal static (string Rec, string Keys) Recognizer(string? language) => language switch
    {
        "zh-CN" or "ja" => ("ch_PP-OCRv5_rec_mobile.onnx", "ppocrv5_dict.txt"),
        "ko" => ("korean_PP-OCRv5_rec_mobile.onnx", "ppocrv5_korean_dict.txt"),
        "ru" => ("eslav_PP-OCRv5_rec_mobile.onnx", "ppocrv5_eslav_dict.txt"),
        _ => (RapidOcr.DefaultRecModelPath, RapidOcr.DefaultKeysFilePath),
    };

    private string[] ModelPaths
    {
        get
        {
            var (rec, keys) = Recognizer(_language());
            return new[] { RapidOcr.DefaultDetModelPath, RapidOcr.DefaultClsModelPath, rec, keys }.Select(f => Path.Combine(_modelDir, f)).ToArray();
        }
    }

    public bool IsAvailable { get { lock (_gate) return !_failed && !_disposed && ModelPaths.All(File.Exists); } }

    public IReadOnlyList<OcrWord> Recognize(ScreenFrame frame, CancellationToken cancellationToken = default)
    {
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Stride < (long)frame.Width * 4 ||
            frame.Pixels.Length < (long)frame.Stride * frame.Height) return Array.Empty<OcrWord>();
        // One engine, one detect at a time: RapidOcr reuses its ORT sessions and buffers.
        lock (_gate)
        {
            if (!EnsureLoaded()) return Array.Empty<OcrWord>();
            try
            {
                // Wraps the capture buffer (no copy unless it is not array-backed); Detect only reads it while pinned.
                var segment = MemoryMarshal.TryGetArray(frame.Pixels, out var s) ? s : new ArraySegment<byte>(frame.Pixels.ToArray());
                var pin = GCHandle.Alloc(segment.Array!, GCHandleType.Pinned);
                OcrResult result;
                try
                {
                    using var bitmap = new SKBitmap();
                    var info = new SKImageInfo(frame.Width, frame.Height,
                        frame.Format == ScreenPixelFormat.Bgra32 ? SKColorType.Bgra8888 : SKColorType.Rgba8888, SKAlphaType.Unpremul);
                    if (!bitmap.InstallPixels(info, pin.AddrOfPinnedObject() + segment.Offset, frame.Stride)) return Array.Empty<OcrWord>();
                    result = _ocr!.Detect(bitmap, Options, cancellationToken);
                }
                finally { pin.Free(); }
                var words = new List<OcrWord>();
                foreach (var block in result.TextBlocks)
                    foreach (var (text, box) in block.WordResults is { Length: > 0 } w
                                 ? w.Select(x => (x.Text, x.BoxPoints)) : new[] { (block.Text, block.BoxPoints) })
                    {
                        var rect = Bounds(box, frame.Width, frame.Height);
                        if (!string.IsNullOrWhiteSpace(text) && rect.Width > 0 && rect.Height > 0) words.Add(new OcrWord(text.Trim(), rect));
                    }
                return words;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { Log.Debug(ex, "Screen OCR recognition failed"); return Array.Empty<OcrWord>(); }
        }
    }

    private bool EnsureLoaded()
    {
        if (_failed || _disposed) return false;
        var paths = ModelPaths;
        if (_ocr != null && _loadedRec == paths[2]) return true;
        _ocr?.Dispose(); _ocr = null; // the UI language changed: load its recognizer
        if (!paths.All(File.Exists))
        {
            _failed = true;
            Log.Warning("Screen OCR unavailable: models missing in {Dir}", _modelDir);
            return false;
        }
        var ocr = new RapidOcr();
        try { ocr.InitModels(paths[0], paths[1], paths[2], paths[3], Threads); _ocr = ocr; _loadedRec = paths[2]; return true; }
        catch (Exception ex)
        {
            ocr.Dispose(); _failed = true;
            Log.Warning(ex, "Screen OCR unavailable: models failed to load from {Dir}", _modelDir);
            return false;
        }
    }

    private static Rectangle Bounds(SKPointI[] box, int width, int height)
    {
        if (box.Length == 0) return Rectangle.Empty;
        int left = Math.Clamp(box.Min(p => p.X), 0, width), top = Math.Clamp(box.Min(p => p.Y), 0, height);
        int right = Math.Clamp(box.Max(p => p.X), 0, width), bottom = Math.Clamp(box.Max(p => p.Y), 0, height);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _ocr?.Dispose(); _ocr = null; }
    }
}
