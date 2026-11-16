using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;
using ConditioningControlPanel.Services.Chaos;

namespace ConditioningControlPanel;

/// <summary>
/// Per-zone backdrop plates rendered UNDER the chaos bubbles. The Step-1 layering spike proved
/// that bubbles/FX/HUD are each their own <b>Topmost</b> window, so a <b>non-topmost</b> fullscreen
/// window is deterministically below all of them — no z-order bookkeeping needed. Unlike the rest of
/// the chaos overlays this one is NOT click-through: it is the play surface and absorbs stray clicks
/// (bubbles still pop — they are topmost windows above it; the right-click Ripple is a global hook).
///
/// Gated entirely on <c>NarrativeModeEnabled &amp;&amp; BackdropEnabled</c>: when off, no window spawns
/// and classic Chaos keeps its desktop click-through behavior exactly. Each depth maps to a scene plate
/// in <see cref="DepthScenes"/>; the plate is drawn on a Skia surface so the authored glint masks
/// (<c>{scene}_fx.png</c>, R=Glow G=Twinkle B=Sheen — painted with <c>tools/backdrop_glint_painter.py</c>)
/// render as true additive light, tuned by <c>backdrops/backdrop_fx.json</c>. The FX layer gates on
/// Enhanced FX (<c>ChaosSkiaFxEnabled</c>); with it off (or no mask) the scene just shows plainly.
/// </summary>
internal static class ChaosBackdropService
{
    // Depth (ActIndex, 1-based) -> scene stem under assets/Chaos/backdrops/. All five have authored
    // _fx.png masks. Edit/reorder this to remap the descent; spares: dollhouse_exterior_background,
    // paradise_alley_exterior, popping_lounge.
    private static readonly string[] DepthScenes =
    {
        "alley_background",               // DEPTH I
        "alley_deep",                     // DEPTH II
        "dollhouse_background",           // DEPTH III
        "dollhouse_backroom_background",  // DEPTH IV
        "paradise_background",            // DEPTH V
    };

    private static string SceneFor(int depth)
    {
        int i = Math.Clamp(depth - 1, 0, DepthScenes.Length - 1);
        return DepthScenes[i];
    }

    private static Window? _active;
    private static SKElement? _sk;
    private static int _currentDepth = -1;
    private static ImageSource? _currentArtSource;   // current scene plate (no FX) for story-card reuse

    // ---- Skia scene + authored FX state ----
    private const float Dt = 0.033f;            // ~30fps render tick
    private const float SweepDur = 1.4f;        // seconds the sheen band takes to cross
    private const float SweepPeriodBase = 7.5f; // base seconds between sweeps (÷ frequency)

    private static SKImage? _art, _artOut, _fx, _bloom;
    private static (float nx, float ny, float w)[] _twSpots = Array.Empty<(float, float, float)>();
    private static SKColorFilter? _rToA, _bToA;
    private static float _glowI = 1f, _glowF = 1f, _twkI = 1f, _twkF = 1f, _shI = 1f, _shF = 1f;

    private struct Tw { public float Nx, Ny, Age, Life, Size; public SKColor Col; }
    private static readonly List<Tw> _tw = new();
    private static float _breath, _sweep, _twAccum;
    private static float _fadeT = 1f; private static bool _fading;   // scene crossfade on a depth swap
    private static readonly Random _rng = new();

    private static DispatcherTimer? _timer;

    private static bool Enabled =>
        Services.Chaos.ChaosModeService.ActiveMode == Services.Chaos.ChaosPlayMode.Story &&
        App.Settings?.Current?.NarrativeModeEnabled == true &&
        App.Settings?.Current?.BackdropEnabled == true;

    private static bool FxOn => App.Settings?.Current?.ChaosSkiaFxEnabled == true;

    /// <summary>The live zone plate (scene art, no FX), for reuse as a story-card background. Null when no backdrop is up.</summary>
    public static ImageSource? CurrentSource => _currentArtSource;

    /// <summary>Spawn the backdrop for a depth (act index, 1-based). No-op when the feature is off.</summary>
    public static void Show(int depth)
    {
        if (!Enabled) { CloseActive(); return; }
        try
        {
            if (_active == null) Build();
            SetDepth(depth);
        }
        catch (Exception ex) { App.Logger?.Warning("ChaosBackdropService.Show failed: {E}", ex.Message); }
    }

    /// <summary>Swap the plate when the run crosses a depth/zone border.</summary>
    public static void SwapTo(int depth)
    {
        if (!Enabled) return;
        if (_active == null) { Show(depth); return; }
        if (depth == _currentDepth) return;
        try { SetDepth(depth); } catch (Exception ex) { App.Logger?.Debug("ChaosBackdropService.SwapTo: {E}", ex.Message); }
    }

    public static void CloseActive()
    {
        try { _timer?.Stop(); } catch { }
        try { _active?.Close(); } catch { }
        _active = null; _sk = null; _currentDepth = -1; _currentArtSource = null;
        DisposeSkia();
    }

    private static void SetDepth(int depth)
    {
        _currentDepth = depth;
        var scene = SceneFor(depth);

        // WPF source for story-card reuse (scene art only).
        _currentArtSource = ChaosArt.Resolve("backdrops", scene)
                            ?? ChaosArt.Resolve("backdrops", "depth" + depth);

        // Skia scene + authored FX.
        var artPath = ChaosArt.PathFor("backdrops", scene) ?? ChaosArt.FilePath($"backdrops/depth{depth}.png");
        var fxPath = ChaosArt.FilePath($"backdrops/{scene}_fx.png");

        _artOut?.Dispose(); _artOut = _art;          // outgoing plate for the crossfade
        _art = LoadSk(artPath);
        _fx?.Dispose(); _fx = LoadSk(fxPath);
        _twSpots = ExtractSpots(fxPath, 1);          // green channel = twinkle anchors
        _rToA ??= ChanToAlpha(0); _bToA ??= ChanToAlpha(2);
        _bloom?.Dispose(); _bloom = BuildBloom(_art, _fx);
        LoadFxTuning();
        _tw.Clear(); _twAccum = 0f; _sweep = 0f;
        _fadeT = _artOut != null ? 0f : 1f; _fading = _artOut != null;

        if (_active != null) _active.Opacity = App.Settings?.Current?.BackdropOpacity ?? 0.55;
        EnsureTimer();
        _sk?.InvalidateVisual();
    }

    private static void EnsureTimer()
    {
        // The render loop runs whenever the plate is up (Skia draws the scene); the FX inside gate on
        // Enhanced FX. A scene crossfade also needs ticks, so run while fading even if FX is off.
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _timer.Tick += (_, _) => Step();
        }
        _timer.Start();
    }

    private static void Step()
    {
        if (_active == null) { _timer?.Stop(); return; }
        bool fx = FxOn;
        if (_fading)
        {
            _fadeT += Dt / 0.45f;
            if (_fadeT >= 1f) { _fadeT = 1f; _fading = false; _artOut?.Dispose(); _artOut = null; }
        }
        _breath += Dt;
        if (fx) StepTwinkles();
        // Idle (no FX, not fading): nothing animates — stop ticking until the next swap.
        if (!fx && !_fading) { _timer?.Stop(); }
        _sk?.InvalidateVisual();
    }

    private static void Build()
    {
        _sk = new SKElement { IsHitTestVisible = false };
        _sk.PaintSurface += OnPaint;
        var grid = new Grid();
        grid.Children.Add(_sk);

        _active = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Black,    // covers the desktop under the (semi-opaque) window
            Topmost = false,               // proven: sits under every topmost bubble/overlay window
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Width = SystemParameters.PrimaryScreenWidth,
            Height = SystemParameters.PrimaryScreenHeight,
            Opacity = App.Settings?.Current?.BackdropOpacity ?? 0.55,
            Content = grid,
        };
        _active.SourceInitialized += (_, _) => ApplyExStyles(_active);
        _active.Closed += (_, _) => { _timer?.Stop(); };
        _active.Show();
        App.Logger?.Information("ChaosBackdropService window up (non-topmost, click-absorbing, Skia FX)");
    }

    // ============================ Skia rendering ============================

    private static void OnPaint(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.Black);
        var info = e.Info;
        if (_art == null || info.Width <= 0 || info.Height <= 0) return;

        bool fx = FxOn;
        var rect = CoverRect(_art, info);

        canvas.Save();
        if (fx) ApplyBreath(canvas, info);

        // scene plate (crossfade the outgoing → incoming on a depth swap)
        using (var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High })
        {
            if (_fading && _artOut != null)
                canvas.DrawImage(_artOut, CoverRect(_artOut, info), p);
            p.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(_fadeT, 0f, 1f) * 255));
            canvas.DrawImage(_art, rect, p);
        }

        if (fx) { try { DrawAuthoredFx(canvas, rect); } catch (Exception ex) { App.Logger?.Debug("ChaosBackdrop.DrawAuthoredFx: {E}", ex.Message); } }
        canvas.Restore();
    }

    /// <summary>Very gentle breathing scale + drift so the plate feels alive behind the field. Subtler
    /// than the menu's — this sits behind fast bubble play, so it must not pull the eye. CoverRect already
    /// overscans, so the small scale never bares an edge.</summary>
    private static void ApplyBreath(SKCanvas canvas, SKImageInfo info)
    {
        float t = _breath;
        float s = 1.015f + 0.006f * (float)Math.Sin(t * 0.55);
        float dx = 0.0015f * info.Width * (float)Math.Sin(t * 0.33);
        float dy = 0.0020f * info.Height * (float)Math.Sin(t * 0.47);
        canvas.Translate(info.Width / 2f, info.Height / 2f);
        canvas.Scale(s, s);
        canvas.Translate(-info.Width / 2f + dx, -info.Height / 2f + dy);
    }

    /// <summary>The authored, masked, additive glint pass — matches the painter's preview: (Glow) breathing
    /// bloom of the painted glow pixels, (Sheen) a band masked to the painted gloss that sweeps across,
    /// (Twinkle) sparkle pops on the painted twinkle spots. Tuned by backdrop_fx.json. Fades in with the
    /// scene on a depth swap so the FX never hard-pops ahead of the art.</summary>
    private static void DrawAuthoredFx(SKCanvas canvas, SKRect rect)
    {
        float t = _breath;
        float fade = Math.Clamp(_fadeT, 0f, 1f);

        if (_glowI > 0.001f && _bloom != null)
        {
            float pulse = (0.42f + 0.22f * (float)Math.Sin(t * 1.6 * _glowF)) * _glowI * fade;
            DrawBloom(canvas, rect, _bloom, pulse);
        }

        if (_bToA != null && _shI > 0.001f && _fx != null)
        {
            float period = SweepPeriodBase / Math.Max(0.05f, _shF);
            float ph = _sweep % period;
            if (ph < SweepDur)
            {
                float pp = ph / SweepDur;
                float env = (float)Math.Sin(Math.PI * pp);
                float center = -0.15f + 1.3f * pp;
                DrawSheen(canvas, rect, _fx, center, 0.5f * env * _shI * fade);
            }
        }

        if (_tw.Count > 0 && _twkI > 0.001f)
        {
            float surf = Math.Min(rect.Width, rect.Height) / 760f;
            using var paint = new SKPaint { IsAntialias = true, BlendMode = SKBlendMode.Plus };
            foreach (var tw in _tw)
            {
                float env = (float)Math.Sin(Math.PI * (tw.Age / tw.Life)) * _twkI * fade;
                if (env <= 0.01f) continue;
                float cx = rect.Left + tw.Nx * rect.Width;
                float cy = rect.Top + tw.Ny * rect.Height;
                float r = tw.Size * surf * (0.7f + 0.3f * env);
                using (var glow = SKShader.CreateRadialGradient(new SKPoint(cx, cy), r * 2.4f,
                    new[] { tw.Col.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 150)), tw.Col.WithAlpha(0) }, null, SKShaderTileMode.Clamp))
                {
                    paint.Shader = glow; canvas.DrawCircle(cx, cy, r * 2.4f, paint);
                }
                paint.Shader = null;
                paint.Color = SKColors.White.WithAlpha((byte)(Math.Clamp(env, 0, 1) * 220));
                canvas.DrawCircle(cx, cy, r * 0.45f, paint);
            }
        }
    }

    private static void StepTwinkles()
    {
        _sweep += Dt;
        if (_twSpots.Length > 0)
        {
            _twAccum -= Dt;
            int maxn = Math.Max(1, (int)Math.Round(3 * _twkI));
            if (_twAccum <= 0f)
            {
                _twAccum = (0.35f + (float)_rng.NextDouble() * 0.45f) / Math.Max(0.05f, _twkF);
                if (_tw.Count < maxn)
                {
                    float total = 0; foreach (var s in _twSpots) total += s.w + 0.05f;
                    float pick = (float)_rng.NextDouble() * total; var hs = _twSpots[0];
                    foreach (var s in _twSpots) { pick -= s.w + 0.05f; if (pick <= 0) { hs = s; break; } }
                    double r = _rng.NextDouble();
                    var col = r < 0.55 ? new SKColor(255, 255, 255) : r < 0.8 ? new SKColor(255, 230, 176) : new SKColor(255, 199, 230);
                    _tw.Add(new Tw
                    {
                        Nx = hs.nx, Ny = hs.ny, Age = 0,
                        Life = 0.7f + (float)_rng.NextDouble() * 0.45f,
                        Size = 6f + (float)_rng.NextDouble() * 8f,
                        Col = col,
                    });
                }
            }
        }
        for (int i = _tw.Count - 1; i >= 0; i--)
        {
            var t = _tw[i]; t.Age += Dt;
            if (t.Age >= t.Life) _tw.RemoveAt(i); else _tw[i] = t;
        }
    }

    private static void LoadFxTuning()
    {
        // defaults, overridden by backdrops/backdrop_fx.json when present
        _glowI = 1f; _glowF = 1f; _twkI = 1f; _twkF = 1f; _shI = 1f; _shF = 1f;
        try
        {
            var p = ChaosArt.FilePath("backdrops/backdrop_fx.json");
            if (p == null || !File.Exists(p)) return;
            var o = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(p));
            float Get(string fx, string k, float def) => (float?)o[fx]?[k] ?? def;
            _glowI = Get("glow", "intensity", 1f); _glowF = Get("glow", "frequency", 1f);
            _twkI = Get("twinkle", "intensity", 1f); _twkF = Get("twinkle", "frequency", 1f);
            _shI = Get("sheen", "intensity", 1f); _shF = Get("sheen", "frequency", 1f);
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosBackdrop.LoadFxTuning: {E}", ex.Message); }
    }

    // ---- shared Skia helpers (ported from the menu glint renderer) ----

    private static SKImage? LoadSk(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            using var s = File.OpenRead(path);
            return SKImage.FromEncodedData(s);
        }
        catch { return null; }
    }

    /// <summary>Colour filter: output white with alpha = the given source channel (0=R,1=G,2=B).</summary>
    private static SKColorFilter ChanToAlpha(int ch)
    {
        var m = new float[20];
        m[4] = 1; m[9] = 1; m[14] = 1;   // RGB -> white
        m[15 + ch] = 1;                   // A = source channel
        return SKColorFilter.CreateColorMatrix(m);
    }

    /// <summary>Cover/crop fit (fill, centered, edges may crop) — matches the old Stretch.UniformToFill.</summary>
    private static SKRect CoverRect(SKImage img, SKImageInfo info)
    {
        float ew = info.Width, eh = info.Height, iw = img.Width, ih = img.Height;
        float s = Math.Max(ew / iw, eh / ih);
        float dw = iw * s, dh = ih * s;
        return new SKRect((ew - dw) / 2f, (eh - dh) / 2f, (ew + dw) / 2f, (eh + dh) / 2f);
    }

    /// <summary>Bake the scene's glow pixels (art × glow-channel), blurred, into an SKImage.</summary>
    private static SKImage? BuildBloom(SKImage? src, SKImage? mask)
    {
        if (src == null || mask == null || _rToA == null) return null;
        try
        {
            int bw = Math.Min(src.Width, 540);
            int bh = Math.Max(1, src.Height * bw / src.Width);
            var bi = new SKImageInfo(bw, bh, SKColorType.Rgba8888, SKAlphaType.Premul);
            var rect = new SKRect(0, 0, bw, bh);
            using var s1 = SKSurface.Create(bi);
            s1.Canvas.Clear(SKColors.Transparent);
            using (var ap = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium })
                s1.Canvas.DrawImage(src, rect, ap);
            using (var mp = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = _rToA, FilterQuality = SKFilterQuality.Medium })
                s1.Canvas.DrawImage(mask, rect, mp);
            using var masked = s1.Snapshot();
            using var s2 = SKSurface.Create(bi);
            s2.Canvas.Clear(SKColors.Transparent);
            using (var bp = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(bw * 0.013f, bw * 0.013f) })
                s2.Canvas.DrawImage(masked, rect, bp);
            return s2.Snapshot();
        }
        catch (Exception ex) { App.Logger?.Debug("ChaosBackdrop.BuildBloom: {E}", ex.Message); return null; }
    }

    /// <summary>Additive glow bloom at the given alpha (0..1).</summary>
    private static void DrawBloom(SKCanvas canvas, SKRect rect, SKImage? bloom, float alpha)
    {
        if (bloom == null || alpha <= 0.002f) return;
        byte a = (byte)Math.Clamp(alpha * 255f, 0, 255);
        using var p = new SKPaint { BlendMode = SKBlendMode.Plus, IsAntialias = true, FilterQuality = SKFilterQuality.High, Color = SKColors.White.WithAlpha(a) };
        canvas.DrawImage(bloom, rect, p);
    }

    /// <summary>One sheen-band pass masked to the gloss (B channel) at the given alpha.</summary>
    private static void DrawSheen(SKCanvas canvas, SKRect rect, SKImage? mask, float center, float alpha)
    {
        if (mask == null || _bToA == null || alpha <= 0.002f) return;
        byte a = (byte)Math.Clamp(alpha * 255f, 0, 255);
        const float hw = 0.16f;
        float c0 = Math.Max(0f, center - hw), c2 = Math.Min(1f, center + hw);
        if (a <= 1 || c2 <= c0) return;
        float c1 = Math.Min(Math.Max(center, c0), c2);
        using var layer = new SKPaint { BlendMode = SKBlendMode.Plus };
        canvas.SaveLayer(layer);
        using (var band = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top), new SKPoint(rect.Right, rect.Bottom),
            new[] { SKColors.Transparent, new SKColor(0xFF, 0xFF, 0xFF, a), SKColors.Transparent },
            new[] { c0, c1, c2 }, SKShaderTileMode.Clamp))
        using (var bp = new SKPaint { Shader = band })
            canvas.DrawRect(rect, bp);
        using (var mp = new SKPaint { BlendMode = SKBlendMode.DstIn, ColorFilter = _bToA, FilterQuality = SKFilterQuality.Medium })
            canvas.DrawImage(mask, rect, mp);
        canvas.Restore();
    }

    /// <summary>Scan a downsized copy of an fx mask for bright spots in one channel — the twinkle spawn
    /// anchors. Returns normalized (x,y,weight), the brightest ~10 cells of a coarse grid.</summary>
    private static (float nx, float ny, float w)[] ExtractSpots(string? path, int channel)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return Array.Empty<(float, float, float)>();
            using var raw = SKBitmap.Decode(path);
            if (raw == null) return Array.Empty<(float, float, float)>();
            int tw = 96, th = Math.Max(1, raw.Height * 96 / Math.Max(1, raw.Width));
            using var small = raw.Resize(new SKImageInfo(tw, th), SKFilterQuality.Medium) ?? raw;
            int w = small.Width, h = small.Height;
            const int cell = 8;
            int cols = Math.Max(1, w / cell), rows = Math.Max(1, h / cell);
            var best = new (float v, int x, int y)[cols * rows];
            float gmax = 0.001f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = small.GetPixel(x, y);
                    float v = (channel == 0 ? c.Red : channel == 1 ? c.Green : c.Blue) / 255f;
                    int ci = Math.Min(rows - 1, y * rows / h) * cols + Math.Min(cols - 1, x * cols / w);
                    if (v > best[ci].v) best[ci] = (v, x, y);
                    if (v > gmax) gmax = v;
                }
            var list = new List<(float, float, float)>();
            foreach (var b in best)
                if (b.v > gmax * 0.5f)
                    list.Add(((b.x + 0.5f) / w, (b.y + 0.5f) / h, b.v / gmax));
            return list.OrderByDescending(z => z.Item3).Take(10).ToArray();
        }
        catch { return Array.Empty<(float, float, float)>(); }
    }

    private static void DisposeSkia()
    {
        _art?.Dispose(); _art = null;
        _artOut?.Dispose(); _artOut = null;
        _fx?.Dispose(); _fx = null;
        _bloom?.Dispose(); _bloom = null;
        _tw.Clear();
        _twSpots = Array.Empty<(float, float, float)>();
    }

    // Absorb clicks (NO WS_EX_TRANSPARENT) but never steal focus / show in Alt-Tab.
    private static void ApplyExStyles(Window w)
    {
        try
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        }
        catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
