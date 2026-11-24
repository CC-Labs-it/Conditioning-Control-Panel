using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Logo = ConditioningControlPanel.Services.BouncingTextEngine.Logo;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Bouncing Text - DVD screensaver style text that bounces across screens
/// Unlocks at Level 60, awards 15 XP per bounce (max 150 XP/min, 2s cooldown between rewards).
/// Supports one or two independent logos, three color modes (random / fixed / rainbow cycle)
/// and a set of per-frame transform effects (breathing, wobble, spin, velocity tilt,
/// squash &amp; stretch, corner burst).
///
/// <para>The motion itself is no longer here: position, velocity, wall and corner detection, the
/// colour modes, the effect transforms and the XP rate limit are
/// <see cref="BouncingTextEngine"/> in CCP.Core, so a second head cannot drift from this one's
/// physics. What remains is everything that needs Windows - the monitor enumeration that sets the
/// bounds, the glyph measurement that sizes the text, the composition clock, the layered
/// click-through windows, and the achievement/haptics/video hooks the engine reports back.</para>
/// </summary>
public class BouncingTextService : IDisposable
{
    private readonly BouncingTextEngine _engine = new();
    // Super Scrawl reads the engine's logos, bounds and font through these.
    private IReadOnlyList<Logo> _logos => _engine.Logos;
    private double _minX => _engine.MinX;
    private double _minY => _engine.MinY;
    private double _maxX => _engine.MaxX;
    private double _maxY => _engine.MaxY;
    private int _currentFontSize => _engine.FontSize;
    private string _currentFont => _engine.Font;
    private readonly Random _random = new();

    // Super Scrawl (Services/Super/CONTRACT.md): null while the switch is off, so the base path is untouched.
    private Super.ScrawlScene? _scrawl;
    private GlobalMouseHook? _scrawlHook;
    private readonly double[] _scrawlHitPx = new double[8]; // physical px boxes, x/y/r/b per logo, read on the hook
    private volatile int _scrawlHitCount;

    /// <summary>The speed slider as a multiplier: the engine's rule.</summary>
    public static double SpeedMultiplier(int slider) => BouncingTextEngine.SpeedMultiplier(slider);
    private readonly List<BouncingTextWindow> _windows = new();
    private bool _isRunning;

    // Composition-clock state. We drive motion off CompositionTarget.Rendering
    // (vsync-aligned, one callback per rendered frame) rather than a DispatcherTimer
    // (quantized to the ~15.6ms OS tick, which beats against the display refresh and
    // produces "low frame rate" judder). _lastRenderTime feeds delta-time movement.
    private TimeSpan _lastRenderTime = TimeSpan.MinValue;

    // Z-order re-assertion accumulator (re-assert topmost every ~0.5s of real time;
    // frame-rate-independent now that the tick rate varies with the display refresh).
    private double _zReassertAccum;

    public bool IsRunning => _isRunning;

    private static Color ToMedia((byte R, byte G, byte B) c) => Color.FromRgb(c.R, c.G, c.B);

    /// <summary>
    /// Current bounding rects of the bouncing logos in PHYSICAL virtual-desktop
    /// pixels, padded to absorb motion between OCR self-exclusion cache refreshes.
    /// Consumed by <see cref="App.GetCcpWindowRectsCached"/> so the avatar's
    /// awareness OCR doesn't read the app's own bouncing words (#287). The
    /// full-screen overlay window itself is dropped from the exclusion set by the
    /// per-monitor span filter, so these small moving rects have to be supplied
    /// separately. One rect per logo, in shared virtual-desktop DIP space.
    ///
    /// Caller must invoke on the UI thread (the animation timer mutates these
    /// fields there). Returns empty when not running.
    /// </summary>
    public System.Drawing.Rectangle[] GetActiveTextScreenRects()
    {
        if (!_isRunning) return Array.Empty<System.Drawing.Rectangle>();
        try
        {
            var dpiScale = GetDpiScale();
            var logos = _engine.Logos;
            var rects = new System.Drawing.Rectangle[logos.Count];
            for (int i = 0; i < logos.Count; i++)
            {
                var l = logos[i];
                // Pad generously: the text drifts a few px/frame and the OCR rect
                // cache is ~250ms stale, and the transform effects can scale the
                // text up to ~1.5x and rotate it (a rotated rect's bbox grows by up
                // to (w+h)/2 per axis). Bouncing text is large and isolated, so
                // modest over-exclusion costs nothing.
                double padX = l.TextWidth * 0.5 + l.TextHeight * 0.5 + 80;
                double padY = l.TextWidth * 0.5 + l.TextHeight * 0.5 + 80;
                int left   = (int)Math.Floor((l.PosX - padX) * dpiScale);
                int top    = (int)Math.Floor((l.PosY - padY) * dpiScale);
                int right  = (int)Math.Ceiling((l.PosX + l.TextWidth + padX) * dpiScale);
                int bottom = (int)Math.Ceiling((l.PosY + l.TextHeight + padY) * dpiScale);
                rects[i] = new System.Drawing.Rectangle(left, top, right - left, bottom - top);
            }
            return rects;
        }
        catch
        {
            return Array.Empty<System.Drawing.Rectangle>();
        }
    }

    public event EventHandler? OnBounce;
    /// <summary>Fires on a true/near DVD corner hit (distinct from a plain wall bounce). Used by the bark egg.</summary>
    public event EventHandler? OnCornerHit;

    public void Start(bool bypassLevelCheck = false, List<string>? pool = null)
    {
        if (_isRunning) return;

        var settings = App.Settings.Current;

        // Note: We don't check BouncingTextEnabled here because Start() is called
        // explicitly when we want to start (either by toggle or by session)

        _isRunning = true;

        // Bounds and glyph metrics are the two facts the engine cannot work out for itself.
        // main folded the dualMonitor flag into App.GetGlobalScreens(), so neither call takes it.
        CalculateScreenBounds();
        _engine.Measure = MeasureText;
        _engine.Start(settings, pool);

        // Create windows for each screen
        CreateWindows(settings.BouncingTextOpacity, _engine.Logos.Count, settings.BouncingTextOutline);

        Super.SuperAccess.Changed -= OnSuperChanged;
        Super.SuperAccess.Changed += OnSuperChanged;
        if (Super.SuperAccess.IsOn(Super.SuperEffect.Scrawl)) EnableScrawl();

        // Drive motion off the composition clock (vsync-aligned, one callback per
        // rendered frame) instead of a DispatcherTimer — see _lastRenderTime note.
        _lastRenderTime = TimeSpan.MinValue;
        _zReassertAccum = 0;
        CompositionTarget.Rendering -= Animate; // guard against a double subscribe
        CompositionTarget.Rendering += Animate;

        // During mandatory video the old code kept Animate subscribed and hid the windows + returned
        // every vsync — a permanent no-op render callback that never let the composition loop sleep
        // (a contributor to the idle/long-session freeze, #453). Instead, pause the loop entirely on
        // VideoStarted and resume on VideoEnded. When "Show Over Videos" is on we deliberately keep
        // animating on top of the video instead.
        if (App.Video != null)
        {
            App.Video.VideoStarted -= OnVideoStartedPause;
            App.Video.VideoStarted += OnVideoStartedPause;
            App.Video.VideoEnded -= OnVideoEndedResume;
            App.Video.VideoEnded += OnVideoEndedResume;
            if (App.Video.IsPlaying) OnVideoStartedPause(null, EventArgs.Empty);
        }

        // The mantra is the user's own text on their own screen, but it is still content and
        // logs get shared: count and length only.
        App.Logger?.Information("BouncingTextService started - Logos: {Count}, TextChars: {Chars}, ColorMode: {Mode}",
            _engine.Logos.Count, (_engine.Logos[0].Text ?? "").Length, settings.BouncingTextColorMode);
    }

    public void Stop()
    {
        _isRunning = false;

        CompositionTarget.Rendering -= Animate;
        // Panic, the emergency exit and every other stop come through here: Scrawl goes with the words.
        Super.SuperAccess.Changed -= OnSuperChanged;
        DisableScrawl();
        if (App.Video != null)
        {
            App.Video.VideoStarted -= OnVideoStartedPause;
            App.Video.VideoEnded -= OnVideoEndedResume;
        }

        // Always close and clear windows, even if we thought we weren't running
        foreach (var window in _windows)
        {
            try { window.Close(); } catch { }
        }
        _windows.Clear();
        _engine.Stop();

        App.Logger?.Information("BouncingTextService stopped");
    }

    /// <summary>Restart with current settings — used when a structural setting
    /// (second text, outlined style) changes while running.</summary>
    public void Restart()
    {
        if (!_isRunning) return;
        var pool = _engine.PoolOverride?.ToList();
        Stop();
        Start(true, pool);
    }

    /// <summary>Sleep the bounce render loop while a mandatory video plays (VideoStarted). Marshals to
    /// the UI thread since the video event can fire off a playback thread. No-op when the user asked
    /// for the text to stay visible over videos (BouncingTextAlwaysOnTop).</summary>
    private void OnVideoStartedPause(object? sender, EventArgs e)
    {
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp == null) return;
        if (!disp.CheckAccess()) { try { disp.BeginInvoke(new Action(() => OnVideoStartedPause(sender, e))); } catch { } return; }
        if (App.Settings?.Current?.BouncingTextAlwaysOnTop == true) return;
        _scrawlHitCount = 0; // hidden words cannot be slammed
        CompositionTarget.Rendering -= Animate;
        foreach (var w in _windows) { try { if (w.IsLoaded) w.Hide(); } catch { } }
    }

    /// <summary>Resume the bounce render loop when the video ends (VideoEnded), if still running.</summary>
    private void OnVideoEndedResume(object? sender, EventArgs e)
    {
        var disp = System.Windows.Application.Current?.Dispatcher;
        if (disp == null) return;
        if (!disp.CheckAccess()) { try { disp.BeginInvoke(new Action(() => OnVideoEndedResume(sender, e))); } catch { } return; }
        if (!_isRunning) return;
        foreach (var w in _windows) { try { if (w.IsLoaded && !w.IsVisible) w.Show(); } catch { } }
        _lastRenderTime = TimeSpan.MinValue; // reset dt so the first resumed frame doesn't jump
        CompositionTarget.Rendering -= Animate;
        CompositionTarget.Rendering += Animate;
    }

    /// <summary>A Bubble Count round holding the other effects (#1418) takes the mandatory video's
    /// pause, same rules ("show over videos" keeps the text up).</summary>
    internal void HoldForFullscreen() => OnVideoStartedPause(null, EventArgs.Empty);

    /// <summary>End of that hold: the mandatory video's resume. A video that took the screen in the
    /// meantime keeps its own pause.</summary>
    internal void ReleaseFullscreenHold()
    {
        if (App.Video?.IsPlaying == true) return;
        OnVideoEndedResume(null, EventArgs.Empty);
    }
    /// <summary>
    /// Measure the actual rendered size of a string at the engine's current font size. Handed to
    /// the engine as its <see cref="BouncingTextEngine.Measure"/> delegate; answering (0,0) makes
    /// it fall back to its own estimate, which is what the WPF catch block did inline.
    /// </summary>
    private (double Width, double Height) MeasureText(string text, int fontSize)
    {
        try
        {
            var typeface = new Typeface(
                Helpers.FontPickerHelper.Resolve(App.Settings?.Current?.BouncingTextFont, "Segoe UI"),
                FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            // MainWindow can be null during startup/shutdown; GetDpi(null) throws an
            // NRE that the catch below swallows into a noisy [WRN] (#305). Resolve a
            // DPI source that may be null and fall back to 1.0 PixelsPerDip.
            var dpiSource = Application.Current?.MainWindow
                            ?? (Visual?)_windows.FirstOrDefault();
            double pixelsPerDip = dpiSource != null
                ? VisualTreeHelper.GetDpi(dpiSource).PixelsPerDip
                : 1.0;
            var formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.White,
                new NumberSubstitution(),
                pixelsPerDip);

            App.Logger?.Debug("Measured text ({Chars} chars): {W}x{H}", (text ?? "").Length, formattedText.Width, formattedText.Height);
            return (formattedText.Width, formattedText.Height);
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "Failed to measure text, using the engine's estimate");
            return (0, 0);
        }
    }

    /// <summary>Bounds to bounce inside: the screens the app-wide "Show content on" picker
    /// targets (App.GetGlobalScreens), which may be all of them, the Windows primary, or one
    /// pinned monitor.</summary>
    private void CalculateScreenBounds()
    {
        var screens = App.GetGlobalScreens();
        if (screens.Length == 0) return;

        // Get DPI scale
        var dpiScale = GetDpiScale();

        // Find total bounds across all screens
        _engine.SetBounds(
            screens.Min(s => s.Bounds.X) / dpiScale,
            screens.Min(s => s.Bounds.Y) / dpiScale,
            screens.Max(s => s.Bounds.X + s.Bounds.Width) / dpiScale,
            screens.Max(s => s.Bounds.Y + s.Bounds.Height) / dpiScale);
    }

    private void CreateWindows(int opacity, int logoCount, bool outline)
    {
        var screens = App.GetGlobalScreens();

        foreach (var screen in screens)
        {
            var window = new BouncingTextWindow(screen, _engine.FontSize, opacity, logoCount, outline);
            window.Show();
            _windows.Add(window);
        }

        // Update text in all windows
        for (int i = 0; i < _engine.Logos.Count; i++)
            UpdateWindowsText(i);
    }

    private void Animate(object? sender, EventArgs e)
    {
        if (!_isRunning) return;

        // A display/DPI change is settling: every frame this writes to N layered surfaces, and the
        // synchronous surface rebuild WPF runs during the transition can only complete when those
        // surfaces go quiet (the mixed-DPI drag hang). Freeze for the ~1s storm; the baseline reset
        // below already handles the resulting dt gap as a stall.
        if (Services.UI.DisplayChangeCoordinator.SpawnsSuppressed) return;

        // Delta time from the composition clock. Establish a baseline on the first
        // frame, ignore duplicate callbacks, and clamp after a stall so the text
        // never teleports across the screen.
        double dt = 1.0 / 60.0;
        if (e is RenderingEventArgs r)
        {
            if (_lastRenderTime == TimeSpan.MinValue)
            {
                _lastRenderTime = r.RenderingTime;
                return;
            }
            dt = (r.RenderingTime - _lastRenderTime).TotalSeconds;
            _lastRenderTime = r.RenderingTime;
            if (dt <= 0) return;
            if (dt > 0.1) dt = 0.1;
        }

        var settings = App.Settings.Current;
        bool overVideos = settings.BouncingTextAlwaysOnTop;

        // Hide bouncing text while a mandatory video is playing (unless Show Over Videos is on)
        if (App.Video?.IsPlaying == true && !overVideos)
        {
            foreach (var w in _windows) { if (w.IsLoaded) w.Hide(); }
            _scrawlHitCount = 0;
            return;
        }
        else
        {
            foreach (var w in _windows) { if (w.IsLoaded && !w.IsVisible) w.Show(); }
        }

        _engine.Tick(dt);

        var logos = _engine.Logos;
        for (int i = 0; i < logos.Count; i++)
            StepLogo(logos[i], i, dt, settings);

        // Re-assert z-order every ~500ms — bouncing text is long-lived and will
        // lose topmost when competing with flash/video/overlay windows
        _zReassertAccum += dt;
        if (_zReassertAccum >= 0.5)
        {
            _zReassertAccum = 0;
            foreach (var window in _windows)
                window.ReassertTopmost();
        }

        // Update position + effect transforms in all windows
        for (int i = 0; i < logos.Count; i++)
        {
            var l = logos[i];
            var (sx, sy, angle) = _engine.ComputeEffectTransform(l, settings);
            // Super Scrawl: a word the pool has caught shrinks toward the eye and grows back on the way out
            if (_scrawl != null && l.Fling.Scale > 0 && l.Fling.Scale < 1) { sx *= l.Fling.Scale; sy *= l.Fling.Scale; }
            // Super Scrawl: a slammed word swells at the screen centre
            if (l.SlamT >= 0)
            {
                double slam = Super.ScrawlRules.SlamWordScale(l.SlamT, _scrawlMotion);
                sx *= slam; sy *= slam;
            }
            foreach (var window in _windows)
            {
                window.UpdatePosition(i, l.PosX, l.PosY);
                window.UpdateTransform(i, sx, sy, angle);
            }
        }

        if (_scrawl != null) TickScrawl(dt);
    }

    /// <summary>Step one logo through the engine, then run the head-only half of a bounce: the
    /// achievement, the burst particles, the haptic pulse, the events and the repaint. The XP award
    /// and its rate limit are the engine's now (through the CoreProgression seam), so nothing here
    /// counts XP a second time.</summary>
    private void StepLogo(BouncingTextEngine.Logo l, int index, double dt, Models.AppSettings settings)
    {
        if (_scrawl != null)
        {
            if (l.SlamT >= 0) { StepSlam(l, dt); return; }
            double spd = Math.Sqrt(l.VelX * l.VelX + l.VelY * l.VelY);
            double f = Super.ScrawlRules.RelaxFactor(spd, l.Cruise, dt);
            l.VelX *= f; l.VelY *= f;
            if (StepStage(l, dt)) return;   // caught by the pool: it moves the word this frame
        }

        var step = _engine.Step(l, dt, settings);

        if (step.Bounced && _scrawl != null)
        {
            var wall = Super.ScrawlRules.WallOf(step.BouncedX && l.VelX > 0, step.BouncedX && l.VelX < 0,
                                                step.BouncedY && l.VelY > 0, step.BouncedY && l.VelY < 0);
            var (mx, my, _) = MonitorCentreAt(l.PosX + l.TextWidth / 2, l.PosY + l.TextHeight / 2);
            _scrawl.WallHit(l.Text, wall, l.PosX, l.PosY, l.PosX + l.TextWidth, l.PosY + l.TextHeight,
                new Super.ScrawlBounds(_minX, _minY, _maxX, _maxY), new Point(mx, my));
        }

        if (step.CornerHit && _scrawl != null)
        {
            // A gold corner blows a hole in the fog. It knocks nothing: reach 0.
            double d = _scrawlDpi;
            Super.SuperStageHost.Stage.Blast(new SuperCore.Blast(
                (l.PosX + l.TextWidth / 2) * d, (l.PosY + l.TextHeight / 2) * d, 0, 230 * d, SuperCore.BlastKind.GoldCorner));
        }

        if (step.CornerHit)
        {
            App.Logger?.Information("🎯 CORNER HIT! Position: ({X}, {Y})", l.PosX, l.PosY);
            App.Achievements?.TrackCornerHit();
            OnCornerHit?.Invoke(this, EventArgs.Empty);

            // Gentle visuals: no ring snapping in at 90% opacity.
            if (settings.BouncingTextFxCornerBurst && !Safety.GentleVisuals.On)
            {
                double cx = l.PosX + l.TextWidth / 2;
                double cy = l.PosY + l.TextHeight / 2;
                foreach (var window in _windows)
                    window.SpawnCornerBurst(cx, cy, ToMedia(step.ColorBeforeBounce));
            }
        }

        if (step.Bounced)
        {
            OnBounce?.Invoke(this, EventArgs.Empty);

            // Haptic pulse on bounce
            _ = App.Haptics?.BouncingTextBounceAsync();

            UpdateWindowsText(index);
        }
    }

    private void UpdateWindowsText(int index)
    {
        var l = _engine.Logos[index];
        foreach (var window in _windows)
        {
            window.UpdateText(index, l.Text, ToMedia(l.Color));
        }
    }

    private double GetDpiScale()
    {
        try
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    /// <summary>
    /// Refresh when settings change (speed, size, opacity, fixed color, show-over-videos)
    /// </summary>
    public void Refresh()
    {
        if (!_isRunning) return;

        var settings = App.Settings.Current;

        _engine.RefreshSpeed(settings);

        // Font size changed: the engine re-measured, push the new size into the visuals.
        if (_engine.RefreshFontSize(settings))
        {
            foreach (var window in _windows)
                window.UpdateFontSize(_engine.FontSize);
        }

        // Same shape for the font family: the engine re-measures (the glyph widths change) then we
        // push the new family into every window's visuals. No restart needed - the element type is
        // unchanged.
        if (_engine.RefreshFont(settings))
        {
            var family = Helpers.FontPickerHelper.Resolve(_engine.Font, "Segoe UI");
            foreach (var window in _windows)
                window.UpdateFontFamily(family);
        }

        // Scrawl stamps are built in the words' font and size: start the walls over when either moves
        if (_scrawl != null && (_scrawlFont != _currentFont || _scrawlSize != ScrawlStampFontSize()))
        {
            DisableScrawl();
            EnableScrawl();
        }

        // Live opacity
        foreach (var window in _windows)
            window.UpdateOpacity(settings.BouncingTextOpacity);

        // Switching to Fixed color applies immediately (Random/Rainbow apply on next bounce)
        if (settings.BouncingTextColorMode == 1)
        {
            _engine.ApplyFixedColor(settings);
            for (int i = 0; i < _engine.Logos.Count; i++)
                UpdateWindowsText(i);
        }

        // Show-over-videos toggled mid-video: resume or pause the loop accordingly
        if (App.Video?.IsPlaying == true)
        {
            if (settings.BouncingTextAlwaysOnTop)
                OnVideoEndedResume(null, EventArgs.Empty);
            else
                OnVideoStartedPause(null, EventArgs.Empty);
        }
    }

    // ---- Super Scrawl on the shared stage (CCP.Core/Super): pool, fog, bubbles, pictures ----

    /// <summary>
    /// One frame of the word against the shared stage. The pool bends it, catches it near the eye,
    /// turns it and throws it back out (TextFling). Its box is swept (bubbles pop, pictures are
    /// knocked) and it cuts a trail through the fog. Words are in DIPs, the stage in device pixels.
    /// True while the pool holds the word: the caller skips its own move and wall bounces.
    /// </summary>
    private bool StepStage(Logo l, double dt)
    {
        var stage = Super.SuperStageHost.Stage;
        double d = _scrawlDpi <= 0 ? 1 : _scrawlDpi;
        double cx = (l.PosX + l.TextWidth / 2) * d, cy = (l.PosY + l.TextHeight / 2) * d;
        double vx = l.VelX * d, vy = l.VelY * d;
        SuperCore.TextFling.Step(ref l.Fling, stage, ref cx, ref cy, ref vx, ref vy, Math.Max(1, l.Cruise) * d, dt);
        l.VelX = vx / d; l.VelY = vy / d;
        bool held = l.Fling.Phase == 1;
        if (held)
        {
            l.PosX = Math.Clamp(cx / d - l.TextWidth / 2, _minX, Math.Max(_minX, _maxX - l.TextWidth));
            l.PosY = Math.Clamp(cy / d - l.TextHeight / 2, _minY, Math.Max(_minY, _maxY - l.TextHeight));
        }
        double left = l.PosX * d, top = l.PosY * d, w = l.TextWidth * d, h = l.TextHeight * d;
        // A caught word is small and in the eye: it pops and knocks nothing on the way round.
        // The player's switch, read live (the stage steps only while fog or pool is on).
        stage.MovingPops = App.Settings?.Current?.MovingEffectsPop == true;
        if (!held) stage.Sweep(left, top, left + w, top + h);
        if (stage.SteamOn)
            for (int i = -1; i <= 1; i++)
                stage.Wipe(left + w / 2 + i * w * 0.32, top + h / 2, Math.Max(h * 0.7, 26 * d), dt * 1.5, mark: false);
        return held;
    }

    // ---- Super Scrawl: wall stamps, corner stamps and the click slam (Services/Super/ScrawlRules.cs) ----

    private Models.MotionLevel _scrawlMotion = Models.MotionLevel.Full;
    private int _scrawlSlam = SuperCore.ScrawlOptions.SlamDefault;   // the option box's slam strength, read each frame
    private string _scrawlFont = "";
    private double _scrawlSize;
    private double _scrawlDpi = 1.0;

    private void OnSuperChanged(Super.SuperEffect effect)
    {
        if (effect != Super.SuperEffect.Scrawl) return;
        var disp = Application.Current?.Dispatcher;
        if (disp == null) return;
        if (!disp.CheckAccess()) { try { disp.BeginInvoke(new Action(() => OnSuperChanged(effect))); } catch { } return; }
        if (!_isRunning) return;
        bool on = Super.SuperAccess.IsOn(Super.SuperEffect.Scrawl);
        if (on && _scrawl == null) EnableScrawl();
        else if (!on && _scrawl != null) DisableScrawl();
    }

    private void EnableScrawl()
    {
        if (_scrawl != null || !_isRunning) return;
        try
        {
            var family = Helpers.FontPickerHelper.Resolve(_currentFont, "Segoe UI");
            var dpiSource = Application.Current?.MainWindow ?? (Visual?)_windows.FirstOrDefault();
            double ppd = dpiSource != null ? VisualTreeHelper.GetDpi(dpiSource).PixelsPerDip : 1.0;
            _scrawlDpi = GetDpiScale();
            _scrawlMotion = MotionFx.Level;
            _scrawlSize = ScrawlStampFontSize();
            _scrawl = new Super.ScrawlScene(family, _scrawlSize, ppd);
            _scrawlFont = _currentFont;
            foreach (var w in _windows) w.AttachScrawl(_scrawl);
            _scrawlHook = new GlobalMouseHook { LeftDown = OnScrawlDown };
            _scrawlHook.Start();
        }
        catch (Exception ex)
        {
            App.Logger?.Warning(ex, "Super Scrawl failed to start");
            DisableScrawl();
        }
    }

    private void DisableScrawl()
    {
        _scrawlHitCount = 0;
        if (_scrawlHook != null) { try { _scrawlHook.Dispose(); } catch { } _scrawlHook = null; }
        if (_scrawl == null) return;
        _scrawl.Clear();
        _scrawl = null;
        foreach (var w in _windows) { try { w.DetachScrawl(); } catch { } }
        foreach (var l in _logos)
        {
            l.SlamT = -1;
            l.SlamLanded = false;
            double spd = Math.Sqrt(l.VelX * l.VelX + l.VelY * l.VelY);
            if (spd > l.Cruise && spd > 0) { l.VelX *= l.Cruise / spd; l.VelY *= l.Cruise / spd; }
        }
    }

    /// <summary>The font size stamps are built at: the words' size times the Stamp size option.</summary>
    private double ScrawlStampFontSize()
        => _currentFontSize * SuperCore.ScrawlOptions.ClampStampSize(
            App.Settings?.Current?.ScrawlStampSize ?? SuperCore.ScrawlOptions.StampSizeDefault) / 100.0;

    private void TickScrawl(double dt)
    {
        // The Stamp size slider moved: stamps are built per size, so the walls start over.
        if (_scrawlSize != ScrawlStampFontSize())
        {
            DisableScrawl();
            EnableScrawl();
            if (_scrawl == null) return;
        }
        var scene = _scrawl!;
        _scrawlMotion = MotionFx.Level;
        // The option box (SuperCore.ScrawlOptions), read live so a slider moves the walls at once.
        var so = App.Settings?.Current;
        _scrawlSlam = SuperCore.ScrawlOptions.ClampSlam(so?.ScrawlSlam ?? SuperCore.ScrawlOptions.SlamDefault);
        scene.Configure(SuperCore.ScrawlOptions.ClampStampSeconds(so?.ScrawlStampSeconds ?? SuperCore.ScrawlOptions.StampSecondsDefault),
            SuperCore.ScrawlOptions.EffectiveCap(so?.ScrawlStampCap ?? SuperCore.ScrawlOptions.StampCapDefault,
                _scrawlSize / BouncingTextEngine.BaseFontSize * 100.0, _logos.Count),
            SuperCore.ScrawlOptions.ShakeFactor(_scrawlSlam, Safety.GentleVisuals.On),
            cornerLabel: so?.BouncingTextFxCornerBurst != false);
        scene.Tick(dt, _scrawlMotion, so?.LockdownPhotosafe == true);
        bool live = scene.IsLive;
        foreach (var w in _windows) w.UpdateScrawl(scene.ShakeX, scene.ShakeY, live);

        // Slam off: no hit boxes, so the hook never takes a click.
        if (!SuperCore.ScrawlOptions.SlamOn(_scrawlSlam)) { _scrawlHitCount = 0; return; }

        // The hit boxes the mouse hook reads, in physical px, written in place (no per-frame allocation)
        int n = Math.Min(_logos.Count, _scrawlHitPx.Length / 4);
        const double pad = 8;
        for (int i = 0; i < n; i++)
        {
            var l = _logos[i];
            _scrawlHitPx[i * 4] = (l.PosX - pad) * _scrawlDpi;
            _scrawlHitPx[i * 4 + 1] = (l.PosY - pad) * _scrawlDpi;
            _scrawlHitPx[i * 4 + 2] = (l.PosX + l.TextWidth + pad) * _scrawlDpi;
            _scrawlHitPx[i * 4 + 3] = (l.PosY + l.TextHeight + pad) * _scrawlDpi;
        }
        _scrawlHitCount = n;
    }

    /// <summary>HOOK THREAD contract: reads the snapshot only. A hit on a word swallows the click and slams it.</summary>
    private bool OnScrawlDown(Point px)
    {
        int n = _scrawlHitCount;
        var box = _scrawlHitPx;
        for (int i = n - 1; i >= 0; i--)
        {
            if (px.X < box[i * 4] || px.X > box[i * 4 + 2] || px.Y < box[i * 4 + 1] || px.Y > box[i * 4 + 3]) continue;
            // Never take a click from CCP's own UI: Lockdown, lock cards, leash windows, attention checks, the panel.
            ProbeClickTarget(px, out bool ours, out bool clickThrough);
            if (!Super.ScrawlRules.MaySwallow(true, ours, clickThrough)) return false;
            int idx = i;
            try { Application.Current?.Dispatcher.BeginInvoke(new Action(() => BeginSlam(idx))); } catch { }
            return true;
        }
        return false;
    }

    private void BeginSlam(int idx)
    {
        if (_scrawl == null || !_isRunning || idx >= _logos.Count || !SuperCore.ScrawlOptions.SlamOn(_scrawlSlam)) return;
        var l = _logos[idx];
        if (l.SlamT >= 0) return;
        (l.SlamX, l.SlamY, l.SlamW) = MonitorCentreAt(l.PosX + l.TextWidth / 2, l.PosY + l.TextHeight / 2);
        if (_scrawlMotion == Models.MotionLevel.Off)
        {
            // Motion off: the word stays put, only the still stamp lands at the centre and fades
            _scrawl.SlamLanded(l.Text, l.SlamX, l.SlamY, l.SlamW);
            return;
        }
        l.SlamT = 0;
        l.SlamLanded = false;
    }

    /// <summary>HOOK THREAD: who would get a click at this physical-px point: one of our windows, and is it click-through?</summary>
    private static void ProbeClickTarget(Point px, out bool ours, out bool clickThrough)
    {
        ours = false; clickThrough = false;
        try
        {
            var root = GetAncestor(WindowFromPoint(new PointInt { X = (int)px.X, Y = (int)px.Y }), 2 /* GA_ROOT */);
            if (root == IntPtr.Zero) return;
            GetWindowThreadProcessId(root, out uint pid);
            ours = pid == (uint)Environment.ProcessId;
            clickThrough = (GetWindowLong(root, -20 /* GWL_EXSTYLE */) & 0x20 /* WS_EX_TRANSPARENT */) != 0;
        }
        catch { ours = true; clickThrough = false; } // unsure: let the click through
    }

    private struct PointInt { public int X, Y; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(PointInt p);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);

    /// <summary>One frame of a slam: pull to the centre over 0.2 s, land (stamp, shake, knock the others), hold, let go.</summary>
    private void StepSlam(Logo l, double dt)
    {
        l.SlamT += dt;
        double tx = l.SlamX - l.TextWidth / 2, ty = l.SlamY - l.TextHeight / 2;
        if (l.SlamT <= Super.ScrawlRules.SlamTravel && _scrawlMotion != Models.MotionLevel.Off)
        {
            double f = Math.Min(1, dt * Super.ScrawlRules.SlamPull);
            l.PosX += (tx - l.PosX) * f;
            l.PosY += (ty - l.PosY) * f;
            return;
        }
        l.PosX = tx;
        l.PosY = ty;
        if (!l.SlamLanded)
        {
            l.SlamLanded = true;
            _scrawl?.SlamLanded(l.Text, l.SlamX, l.SlamY, l.SlamW);
            foreach (var o in _logos)
            {
                if (o == l || o.SlamT >= 0) continue;
                var (dvx, dvy) = Super.ScrawlRules.Knock(o.PosX + o.TextWidth / 2, o.PosY + o.TextHeight / 2,
                    l.SlamX, l.SlamY, o.Cruise, _scrawlMotion);
                double knock = SuperCore.ScrawlOptions.KnockFactor(_scrawlSlam);
                o.VelX += dvx * knock;
                o.VelY += dvy * knock;
            }
            // Leave on a fresh diagonal at its own cruise speed, DVD style
            double d = l.Cruise / Math.Sqrt(2);
            l.VelX = _random.Next(2) == 0 ? d : -d;
            l.VelY = _random.Next(2) == 0 ? d : -d;
        }
        if (l.SlamT >= Super.ScrawlRules.SlamHold)
        {
            l.SlamT = -1;
            l.SlamLanded = false;
        }
    }

    /// <summary>Centre and width (DIP) of the monitor under a virtual-desktop DIP point; the whole field if none.</summary>
    private (double X, double Y, double W) MonitorCentreAt(double x, double y)
    {
        double px = x * _scrawlDpi, py = y * _scrawlDpi;
        try
        {
            foreach (var s in App.GetGlobalScreens())
            {
                var b = s.Bounds;
                if (px >= b.Left && px < b.Right && py >= b.Top && py < b.Bottom)
                    return ((b.Left + b.Width / 2.0) / _scrawlDpi, (b.Top + b.Height / 2.0) / _scrawlDpi, b.Width / _scrawlDpi);
            }
        }
        catch { }
        return ((_minX + _maxX) / 2, (_minY + _maxY) / 2, _maxX - _minX);
    }

    public void Dispose()
    {
        Stop();
    }
}

/// <summary>
/// Transparent window that displays the bouncing text (one visual per logo)
/// </summary>
internal class BouncingTextWindow : Window
{
    /// <summary>One rendered logo: either a TextBlock (drop-shadow style) or an
    /// OutlinedText (crisp stroke style), plus its per-frame effect transforms.</summary>
    private sealed class LogoVisual
    {
        public FrameworkElement Element = null!;
        public TextBlock? Tb;
        public OutlinedText? Ot;
        public ScaleTransform Scale = null!;
        public RotateTransform Rotate = null!;
    }

    private readonly List<LogoVisual> _visuals = new();
    private readonly Canvas _canvas;
    private readonly System.Windows.Forms.Screen _screen;
    private readonly double _dpiScale;
    private readonly bool _outline;
    private IntPtr _hwnd;

    public BouncingTextWindow(System.Windows.Forms.Screen screen, int fontSize = 48, int opacity = 100, int logoCount = 1, bool outline = false)
    {
        _screen = screen;
        _dpiScale = GetDpiScale();
        _outline = outline;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        IsHitTestVisible = false;
        ResizeMode = ResizeMode.NoResize;

        // Cover the entire screen
        Left = screen.Bounds.X / _dpiScale;
        Top = screen.Bounds.Y / _dpiScale;
        Width = screen.Bounds.Width / _dpiScale;
        Height = screen.Bounds.Height / _dpiScale;

        // Canvas for positioning
        _canvas = new Canvas();
        Content = _canvas;

        for (int i = 0; i < logoCount; i++)
            _visuals.Add(CreateLogoVisual(fontSize, opacity));

        // Make click-through and force Win32 TOPMOST (more reliable than WPF Topmost property)
        SourceInitialized += (s, e) =>
        {
            MakeClickThrough();
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        };
    }

    private LogoVisual CreateLogoVisual(int fontSize, int opacity)
    {
        // The user's pick (any installed family, or the bundled Fredoka sentinel). Resolved per
        // visual rather than cached on the window so a mid-run Refresh sees the current setting.
        var family = Helpers.FontPickerHelper.Resolve(App.Settings?.Current?.BouncingTextFont, "Segoe UI");
        var v = new LogoVisual
        {
            Scale = new ScaleTransform(1, 1),
            Rotate = new RotateTransform(0),
        };
        var transform = new TransformGroup();
        transform.Children.Add(v.Scale);
        transform.Children.Add(v.Rotate);

        if (_outline)
        {
            v.Ot = new OutlinedText
            {
                Family = family,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Fill = Brushes.HotPink,
                Stroke = Brushes.Black,
                StrokeThickness = Math.Max(2.0, fontSize / 22.0),
                Opacity = opacity / 100.0,
                RenderTransform = transform,
                RenderTransformOrigin = new Point(0.5, 0.5),
                IsHitTestVisible = false,
            };
            v.Element = v.Ot;
        }
        else
        {
            v.Tb = new TextBlock
            {
                FontFamily = family,
                FontSize = fontSize,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.HotPink,
                Opacity = opacity / 100.0,
                RenderTransform = transform,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 10,
                    ShadowDepth = 3
                }
            };
            v.Element = v.Tb;
        }

        _canvas.Children.Add(v.Element);
        return v;
    }

    public void UpdateText(int index, string text, Color color)
    {
        if (index < 0 || index >= _visuals.Count) return;
        var v = _visuals[index];
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        if (v.Tb != null)
        {
            v.Tb.Text = text;
            v.Tb.Foreground = brush;
        }
        else if (v.Ot != null)
        {
            v.Ot.Text = text;
            v.Ot.Fill = brush;
            v.Ot.Build();
        }
    }

    public void UpdateFontSize(int fontSize)
    {
        foreach (var v in _visuals)
        {
            if (v.Tb != null)
            {
                v.Tb.FontSize = fontSize;
            }
            else if (v.Ot != null)
            {
                v.Ot.FontSize = fontSize;
                v.Ot.StrokeThickness = Math.Max(2.0, fontSize / 22.0);
                v.Ot.Build();
            }
        }
    }

    /// <summary>
    /// Live font-family swap, mirroring <see cref="UpdateFontSize"/>. OutlinedText caches its
    /// geometry on a key that includes the family, so it needs an explicit rebuild; a TextBlock
    /// re-measures itself.
    /// </summary>
    public void UpdateFontFamily(FontFamily family)
    {
        if (family == null) return;
        foreach (var v in _visuals)
        {
            if (v.Tb != null)
            {
                v.Tb.FontFamily = family;
            }
            else if (v.Ot != null)
            {
                v.Ot.Family = family;
                v.Ot.Build();
            }
        }
    }

    public void UpdateOpacity(int opacity)
    {
        foreach (var v in _visuals)
            v.Element.Opacity = opacity / 100.0;
    }

    public void UpdatePosition(int index, double x, double y)
    {
        if (index < 0 || index >= _visuals.Count) return;
        var v = _visuals[index];

        // Convert global position to local screen position
        var localX = x - (_screen.Bounds.X / _dpiScale);
        var localY = y - (_screen.Bounds.Y / _dpiScale);

        // OutlinedText draws its glyphs inset by (StrokeThickness + 6) padding, so
        // shift the element so the glyphs land on the same spot the bounce math uses.
        if (v.Ot != null)
        {
            double pad = v.Ot.StrokeThickness + 6;
            localX -= pad;
            localY -= pad;
        }

        // Just position the text and let WPF clip it to the window naturally. The
        // previous "is any part visible on this screen?" check used Width/Height
        // (this window's bounds, computed from the desktop DPI scale) as the
        // boundary, which goes wrong on mixed-DPI multi-monitor setups: the text
        // would appear to "hide and come back" inside a region of the screen
        // because the visibility math thought we were off-screen when we weren't.
        // (Bug #188.) The bouncing math in BouncingTextService keeps positions
        // inside the virtual desktop bounds anyway, so any window that covers part
        // of where the text is will render it; windows that don't cover that
        // region just render the text off-canvas and WPF clips it. No visibility
        // toggle needed.
        Canvas.SetLeft(v.Element, localX);
        Canvas.SetTop(v.Element, localY);
        v.Element.Visibility = Visibility.Visible;
    }

    /// <summary>Apply the combined per-frame effect transform (mutates the existing
    /// transforms; setting an unchanged DP value is a no-op, so identity frames cost nothing).</summary>
    public void UpdateTransform(int index, double scaleX, double scaleY, double angle)
    {
        if (index < 0 || index >= _visuals.Count) return;
        var v = _visuals[index];
        v.Scale.ScaleX = scaleX;
        v.Scale.ScaleY = scaleY;
        v.Rotate.Angle = angle;
    }

    // Super Scrawl: the stamp layer sits first in the canvas (under every word); the shake moves the whole canvas.
    private Super.ScrawlLayer? _scrawlLayer;
    private TranslateTransform? _shake;
    private bool _scrawlWasLive;

    public void AttachScrawl(Super.ScrawlScene scene)
    {
        DetachScrawl();
        _scrawlLayer = new Super.ScrawlLayer(scene, _screen.Bounds.X / _dpiScale, _screen.Bounds.Y / _dpiScale);
        _canvas.Children.Insert(0, _scrawlLayer);
        _shake = new TranslateTransform();
        _canvas.RenderTransform = _shake;
    }

    public void DetachScrawl()
    {
        if (_scrawlLayer != null) { _canvas.Children.Remove(_scrawlLayer); _scrawlLayer = null; }
        if (_shake != null) { _canvas.RenderTransform = Transform.Identity; _shake = null; }
        _scrawlWasLive = false;
    }

    /// <summary>Per frame: apply the shake and redraw the stamps while anything is alive (plus one frame to clear).</summary>
    public void UpdateScrawl(double shakeX, double shakeY, bool live)
    {
        if (_shake != null) { _shake.X = shakeX; _shake.Y = shakeY; }
        if (_scrawlLayer != null && (live || _scrawlWasLive)) _scrawlLayer.InvalidateVisual();
        _scrawlWasLive = live;
    }

    /// <summary>Expanding, fading ring spawned at a corner hit (virtual-desktop DIP center).</summary>
    public void SpawnCornerBurst(double x, double y, Color color)
    {
        try
        {
            const double SIZE = 240;
            var localX = x - (_screen.Bounds.X / _dpiScale);
            var localY = y - (_screen.Bounds.Y / _dpiScale);

            var strokeBrush = new SolidColorBrush(color);
            strokeBrush.Freeze();
            var scale = new ScaleTransform(0.1, 0.1);
            var ring = new System.Windows.Shapes.Ellipse
            {
                Width = SIZE,
                Height = SIZE,
                Stroke = strokeBrush,
                StrokeThickness = 6,
                Opacity = 0.9,
                IsHitTestVisible = false,
                RenderTransform = scale,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            Canvas.SetLeft(ring, localX - SIZE / 2);
            Canvas.SetTop(ring, localY - SIZE / 2);
            _canvas.Children.Add(ring);

            var dur = TimeSpan.FromMilliseconds(550);
            var grow = new System.Windows.Media.Animation.DoubleAnimation(0.1, 1.3, dur)
            {
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase
                {
                    EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut
                }
            };
            var fade = new System.Windows.Media.Animation.DoubleAnimation(0.9, 0.0, dur);
            fade.Completed += (s, e) => { try { _canvas.Children.Remove(ring); } catch { } };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            ring.BeginAnimation(OpacityProperty, fade);
        }
        catch { /* purely cosmetic - never let a burst take down the bounce loop */ }
    }

    private void MakeClickThrough()
    {
        _hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        var extendedStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        // WS_EX_TRANSPARENT: clicks pass through
        // WS_EX_TOOLWINDOW: not shown in alt-tab
        // WS_EX_NOACTIVATE: never steals keyboard/mouse focus
        SetWindowLong(_hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    public void ReassertTopmost()
    {
        if (_hwnd != IntPtr.Zero)
            SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private double GetDpiScale()
    {
        try
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
        catch
        {
            return 1.0;
        }
    }

    #region Win32

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    #endregion
}
