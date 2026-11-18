using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Compositor twin of the flash image popups. Unlike the Avalonia port's FlashLayer (which
/// owns fade + GIF advance), this layer is a pure DRAW LIST: FlashService's existing heartbeat
/// keeps driving every item's opacity, frame index and gaze-dwell scale through the FlashWindow
/// state bag - exactly the split solid mode already established - so fade speed, lifetime,
/// hydra, gaze and XP behavior are identical by construction. Flag the split when coordinating
/// with the Avalonia head.
///
/// Threading: every member is UI-thread only (spawn/remove happen on the dispatcher, and the
/// engine ticks Update/Render there too), so items need no locking. Items OWN their SKImage
/// frames; <see cref="Remove"/>/<see cref="Clear"/> dispose them deterministically - never
/// dispose an item's frames from outside the layer.
///
/// Renders on the MAIN surface (flashes stay visible in recordings, like every non-braindrain
/// effect). Mouse clicks can't reach the click-through host: FlashService runs a global-hook
/// hit-test over these items (like the shared-host bubbles) for clickable flashes.
///
/// Flashes v2: an item may carry a <see cref="FlashMotionState"/>. The layer steps it in Update
/// (the one place with delta time) and keeps the item's X/Y/W/H at the media's current
/// axis-aligned bounds, so the click snapshot and the heartbeat's state-bag mirror read the live
/// rect. A moving item dirties the layer only when the maths actually moved it.
/// </summary>
public sealed class FlashLayer : BaseLayer
{
    /// <summary>One live flash. FlashService's heartbeat writes the mutable fields each tick.</summary>
    public sealed class FlashItem
    {
        /// <summary>Decoded frames, owned by the layer. Null after removal.</summary>
        internal SKImage[]? Frames;

        // Bookkeeping rect in world (virtual-desktop) px - INCLUDES the glow padding,
        // mirroring host mode's expanded rect (gaze + overlap read the same values in DIP
        // from the FlashWindow state bag).
        public float X, Y, W, H;
        /// <summary>Glow inset: the image draws PaddingPx inside the rect (legacy Border.Padding).</summary>
        public float PaddingPx;
        public float CornerRadiusPx;

        /// <summary>Fade alpha 0..1 - written by FlashService's heartbeat (VisualOpacity).</summary>
        public double Opacity;
        /// <summary>Current GIF frame - written by FlashService's heartbeat.</summary>
        public int FrameIndex;
        /// <summary>Gaze-dwell inflate about center, 1.0..1.1 (SetGazeDwellProgress parity).</summary>
        public double DwellScale = 1.0;
        internal System.Windows.Point? BubbleOriginPx;
        internal double BubbleDiameterPx;
        internal MotionLevel EntranceMotion;
        internal System.Windows.Rect? EntranceTarget;
        internal double EntranceAlpha = 1;
        internal double EntranceProgress = 1;

        /// <summary>Flashes v2 motion, null or Still for a classic held flash. Stepped by Update.</summary>
        public FlashMotionState? Motion;

        /// <summary>
        /// Flashes v2 wave 2: non-null once a hand has dismissed this flash and it is breaking
        /// apart. FlashService is already finished with the item by then (the window is out of the
        /// active list, XP and hydra have run), so the layer owns the rest of its life: Update
        /// steps the shards and drops the item the moment they are done.
        /// </summary>
        public FlashShatterState? Shatter;

        /// <summary>
        /// Non-null once a pop hands this flash to its leave animation (FlashExit). Like
        /// <see cref="Shatter"/>, the layer owns the rest of its life and drops it when done.
        /// </summary>
        public FlashExitState? Exit;

        /// <summary>Super Flicker Deck: non-null while this flash is a flippable card.</summary>
        public FlickerDeckState? Deck;
        /// <summary>Super Flicker Deck: the final click's break. Owns the item until done, like Shatter.</summary>
        public FlickerShatterState? DeckShatter;
        /// <summary>The next picture, converted and waiting for the flip's edge. Owned by the layer.</summary>
        internal SKImage[]? DeckPendingFrames;
        /// <summary>Raised in the air (bring the window forward too), swapped, or gave up waiting.</summary>
        internal Action<FlashItem>? OnDeckRaise, OnDeckSwap, OnDeckGaveUp;
        /// <summary>Unit direction a buried card peeks along, set while hovered.</summary>
        internal float DeckPeekDx, DeckPeekDy;
        internal SKMaskFilter? DeckShadowCache;
        /// <summary>The white puff at a flip's edge as the new picture lands. Null once it has died.</summary>
        internal FlickerSpark[]? DeckSparks;
        internal float DeckShadowSigma = -1f;
        /// <summary>The monitor this card lives on, world px (empty = unknown): a reshaped card stays on it.</summary>
        internal SKRect DeckBounds;
        /// <summary>The card's box before the last new face (CCP.Core DeckCardFit): the draw eases from it to X..H.</summary>
        internal CardBox? DeckShapeFrom;
        internal double DeckShapeSpan;
        /// <summary>Motion Off: the old picture, fading out over the new face (DeckTurn.FadeOut). Owned by the layer.</summary>
        internal SKImage[]? DeckFadeFrames;
        /// <summary>Motion Off: the old face fading out was the spiral back.</summary>
        internal bool DeckFadeFromBack;
        /// <summary>Motion Off: the picture (still in Frames) is fading out over the spiral back.</summary>
        internal bool DeckFadeFromFront;
        /// <summary>Shop look "behind" (CCP.Core DeckSpiralBack): this flip turns to the spiral back, not a new picture.</summary>
        internal bool DeckBackNext;
        /// <summary>The card shows its spiral back (the picture stays in Frames for the GaveUp case).</summary>
        internal bool DeckOnBack;
        /// <summary>The back's spiral picture (Bgra8888, rolled once per flip to the back), its ground and its turn.</summary>
        internal SKBitmap? DeckSpiral;
        /// <summary>The back drawn at half size and stretched once (SpiralBackCache: about 3x cheaper than at full size).</summary>
        internal Super.Draw.SpiralBackCache? DeckBackCache;
        internal uint DeckSpiralGround;
        internal double DeckSpiralTurn;
        internal LoomSpec? DeckSpiralSpec;
        /// <summary>The back's phrase, broken into lines, and its tint.</summary>
        internal string? WhisperText;
        internal string[]? WhisperLines;
        internal float WhisperHue = 320;

        // Super, the Whirlpool: its hold on this flash. The pool moves the rect itself (X, Y), so
        // clicks and gaze keep landing where the picture is; PoolScale is draw-only.
        internal double PoolVx, PoolVy;
        public double PoolScale = 1.0;
        internal bool PoolEaten;
        /// <summary>The flash reached the eye. Raised once; FlashService takes it off the screen (no pay).</summary>
        internal Action<FlashItem>? OnPoolEaten;

        // Glow (lucky / sparkle-boost tiers). Sigma is the WPF DropShadow blur radius / 3
        // (same conversion as the brain-drain layer). LuckyPulse replicates the 400ms
        // auto-reverse radius x1.6 / opacity 0.7->1.0 forever-animation.
        internal bool HasGlow;
        internal SKColor GlowColor;
        internal float GlowSigmaPx;
        internal double GlowOpacity;
        /// <summary>Natasha's favourite: a short red blink across the picture now and then.</summary>
        internal bool NatashaCue;
        /// <summary>Natasha's dodge: Environment.TickCount64 when the ring empties (0 = no ring).</summary>
        internal long DodgeUntilMs;
        internal bool LuckyPulse;
        /// <summary>A Scrolller picture: "via Scrolller" sits in its bottom-right corner.</summary>
        internal bool Credit;

        internal double ElapsedSec;          // pulse clock, advanced by Update
        internal SKMaskFilter? BlurCache;    // rebuilt only when the quantized sigma changes
        internal float BlurCacheSigma = -1f;

        // #853 dirty tracking: the values this item was last DRAWN from. A flash holding at full
        // opacity on a still image has none of them rewritten by the heartbeat, so the layer can
        // report clean and stop re-rastering the whole shared surface behind it.
        internal double LastOpacity = double.NaN;
        internal int LastFrameIndex = -1;
        internal double LastDwellScale = double.NaN;

        internal void SetClipFrame(System.Windows.Media.Imaging.BitmapSource source)
        {
            if (Shatter != null || DeckShatter != null || Frames == null) return;
            var next = SkiaWpfInterop.ToSKImage(source);
            foreach (var old in Frames) old.Dispose();
            Frames = new[] { next };
            FrameIndex = 0;
            LastFrameIndex = -1;
        }

        internal void ReleaseFrames()
        {
            var frames = Frames;
            Frames = null;
            if (frames == null) return;
            foreach (var f in frames)
            {
                try { f.Dispose(); } catch { }
            }
            BlurCache?.Dispose();
            BlurCache = null;
            DropDeckPending();
            DropDeckFade();
            DeckBackCache?.Dispose();
            DeckBackCache = null;
            DeckSpiral?.Dispose();
            DeckSpiral = null;
            DeckShadowCache?.Dispose();
            DeckShadowCache = null;
        }

        internal void DropDeckFade()
        {
            var fading = DeckFadeFrames;
            DeckFadeFrames = null;
            DeckFadeFromBack = false;
            DeckFadeFromFront = false;
            if (fading == null) return;
            foreach (var f in fading)
            {
                try { f.Dispose(); } catch { }
            }
        }

        internal void DropDeckPending()
        {
            var pending = DeckPendingFrames;
            DeckPendingFrames = null;
            if (pending == null) return;
            foreach (var f in pending)
            {
                try { f.Dispose(); } catch { }
            }
        }
    }

    private readonly List<FlashItem> _items = new();
    // Reused paints (no per-frame allocations).
    private readonly SKPaint _imagePaint = new() { FilterQuality = SKFilterQuality.Low };
    private readonly SKPaint _fillPaint = new();
    private readonly SKPaint _ringPaint = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
    private readonly SKPaint _creditPaint = new()
    {
        IsAntialias = true,
        TextAlign = SKTextAlign.Right,
        Typeface = SKTypeface.FromFamilyName("Segoe UI") ?? SKTypeface.Default,
    };
    // Super Flicker Deck: one reused path for cracks and shard clips, one rng for the breaks.
    private readonly SKPath _deckPath = new();
    private readonly Random _deckRng = new();
    // Shop looks worn by the Deck (frames, the spiral back, the turn shade): rules in CCP.Core DeckLooks / DeckSpiralBack.
    private readonly Super.Draw.DeckLookDraw _deckLook = new();

    public FlashLayer(CompositorEngine engine) : base(engine.Wake) { }

    public override int ZIndex => CompositorLayers.Flash;

    public override bool WorldSpacePx => true;

    /// <summary>
    /// Add a flash. The layer takes ownership of <paramref name="frames"/>. Geometry is world
    /// px; the image draws <paramref name="paddingPx"/> inside the rect (glow inset, 0 without
    /// glow). <paramref name="glowSigmaPx"/> is the DropShadow radius/3 in px; 0 = no glow.
    /// </summary>
    public FlashItem Spawn(SKImage[] frames, float x, float y, float w, float h,
        float paddingPx, float cornerRadiusPx,
        SKColor glowColor, float glowSigmaPx, double glowOpacity, bool luckyPulse,
        FlashMotionState? motion = null)
    {
        var item = new FlashItem
        {
            Frames = frames,
            X = x, Y = y, W = w, H = h,
            Motion = motion,
            PaddingPx = paddingPx,
            CornerRadiusPx = cornerRadiusPx,
            HasGlow = glowSigmaPx > 0,
            GlowColor = glowColor,
            GlowSigmaPx = glowSigmaPx,
            GlowOpacity = glowOpacity,
            LuckyPulse = luckyPulse
        };
        // A pendulum re-homes the picture under its pivot at spawn: start from the motion's rect.
        if (motion != null && motion.Style != FlashMotionStyle.Still)
            SyncRect(item, motion);
        _items.Add(item);
        _dirty = true;
        SetActive(true);
        return item;
    }

    /// <summary>
    /// Flashes v2 wave 2: hand an item over to the shatter instead of removing it. The layer keeps
    /// the frames alive for the length of the break and disposes them when the last shard is gone,
    /// so FlashService can tear its window down at the dismiss exactly as it always did. A state
    /// with no shards (MotionLevel.Off) removes the item on the spot: that is the plain cut.
    /// </summary>
    public void BeginShatter(FlashItem item, FlashShatterState shatter)
    {
        if (item.Frames is not { Length: > 0 } || shatter.Done || shatter.Shards.Length == 0)
        {
            Remove(item);
            return;
        }
        item.Shatter = shatter;
        // The shards fall from where the picture is actually DRAWN, not from where it spawned and
        // not from the axis-aligned box the click reads. A pendulum's box is the bounds of the
        // ROTATED media, some 15-18% larger than the picture and never tilted, so cutting from it
        // would make the flash jump bigger and snap level the instant it broke; the pivot-space
        // rect plus the frozen angle is the same geometry the pendulum draw itself uses.
        if (item.Motion is { Style: FlashMotionStyle.Pendulum } pend)
            FlashShatter.TakeOverHangingRect(shatter, pend.PivotX, pend.PivotY, pend.Rope,
                pend.AngleRad, pend.MediaW, pend.MediaH);
        else
            FlashShatter.TakeOverDrawnRect(shatter, item.X, item.Y, item.W, item.H);
        _dirty = true;
        SetActive(true);
    }

    /// <summary>
    /// Hand a popped item to its leave animation instead of removing it. Same ownership contract
    /// as <see cref="BeginShatter"/>: frames live until the exit ends, then the layer drops them.
    /// </summary>
    public void BeginExit(FlashItem item, FlashExitState exit)
    {
        if (item.Frames is not { Length: > 0 } || item.Opacity <= 0)
        {
            Remove(item);
            return;
        }
        item.Exit = exit;
        // A picture popped mid-entrance leaves from where it is, fully formed.
        item.BubbleOriginPx = null;
        item.EntranceProgress = 1;
        item.EntranceAlpha = 1;
        _dirty = true;
        SetActive(true);
    }

    /// <summary>
    /// Super Flicker Deck: draw this item above every other flash. Called while the card is
    /// already in the air, never on the click frame.
    /// </summary>
    public void BringToFront(FlashItem item)
    {
        var at = _items.IndexOf(item);
        if (at < 0 || at == _items.Count - 1) return;
        _items.RemoveAt(at);
        _items.Add(item);
        _dirty = true;
    }

    /// <summary>
    /// Super Flicker Deck: the final click. Breaks the picture box as it is drawn right now into
    /// triangles (FlickerShatter) and keeps the frames until the last shard is gone. Off motion
    /// makes no shards and removes the item: the plain cut.
    /// </summary>
    public void BeginDeckShatter(FlashItem item, MotionLevel level)
    {
        var frames = item.Frames;
        if (frames is not { Length: > 0 }) { Remove(item); return; }
        var image = frames[Math.Clamp(item.FrameIndex, 0, frames.Length - 1)];
        var rect = new SKRect(item.X, item.Y, item.X + item.W, item.Y + item.H);
        var inner = new SKRect(rect.Left + item.PaddingPx, rect.Top + item.PaddingPx,
            rect.Right - item.PaddingPx, rect.Bottom - item.PaddingPx);
        var fit = UniformFit(image.Width, image.Height, inner);
        var rot = item.Deck != null ? FlickerDeck.Sample(item.Deck, level).WobbleRad : 0;
        var state = FlickerShatter.Create(fit.MidX, fit.MidY, fit.Width, fit.Height, rot, level, _deckRng);
        if (state.Done) { Remove(item); return; }
        item.Deck = null;
        item.DropDeckPending();
        item.DeckShatter = state;
        _dirty = true;
        SetActive(true);
    }

    /// <summary>
    /// Super Flicker Deck: drop every break still falling. Panic and Stop call this after they
    /// have closed the windows, because a break has no window left to close.
    /// </summary>
    public void ClearDeckBreaks()
    {
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            if (item.DeckShatter == null) continue;
            item.DeckShatter = null;
            item.ReleaseFrames();
            _items.RemoveAt(i);
            _dirty = true;
        }
        if (_items.Count == 0) SetActive(false);
    }

    /// <summary>Super Flicker Deck switched off: every card goes back to a plain flash where it is.</summary>
    public void ClearDecks()
    {
        foreach (var item in _items)
        {
            if (item.Deck == null) continue;
            item.Deck = null;
            item.DropDeckPending();
            item.DropDeckFade();
            item.DeckOnBack = item.DeckBackNext = false;
            item.DeckShapeFrom = null;
            _dirty = true;
        }
    }

    /// <summary>Remove an item and dispose its frames. Idempotent.</summary>
    public void Remove(FlashItem item)
    {
        item.ReleaseFrames();
        _items.Remove(item);
        _dirty = true;      // the survivors must be repainted without this one
        if (_items.Count == 0) SetActive(false);
    }

    public void Clear()
    {
        foreach (var item in _items) item.ReleaseFrames();
        _items.Clear();
        _dirty = true;
        SetActive(false);
    }

    // #853: honest dirt. FlashService's heartbeat only WRITES these fields while a flash is fading
    // or stepping GIF frames - a still image holding at full opacity writes nothing, yet the layer
    // used to force a full re-raster of the shared surface every frame it was up.
    // UI thread only, like every other member (see the class Threading note).
    private bool _dirty = true;

    public override bool Dirty => _dirty;
    public override void ClearDirty() => _dirty = false;

    public override void Update(TimeSpan delta)
    {
        UpdateDeckHover();
        // Backwards: a finished shatter drops its item right here, on the existing flash tick, so
        // the break needs no timer of its own.
        for (int i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            item.ElapsedSec += delta.TotalSeconds;
            if (item.Shatter == null && item.BubbleOriginPx is { } origin)
            {
                item.EntranceTarget ??= new System.Windows.Rect(item.X, item.Y, item.W, item.H);
                var sample = FlashDelivery.Sample(origin, item.BubbleDiameterPx,
                    item.EntranceTarget.Value, item.ElapsedSec, MotionFx.Level == MotionLevel.Off
                        ? MotionLevel.Off : item.EntranceMotion);
                item.X = (float)sample.Rect.X; item.Y = (float)sample.Rect.Y;
                item.W = (float)sample.Rect.Width; item.H = (float)sample.Rect.Height;
                item.EntranceAlpha = sample.Alpha;
                item.EntranceProgress = sample.Progress;
                _dirty = true;
                if (sample.Progress >= 1 && sample.Alpha >= 1) item.BubbleOriginPx = null;
            }

            if (item.Shatter is { } shatter)
            {
                if (FlashShatter.Step(shatter, delta.TotalSeconds)) _dirty = true;
                if (shatter.Done)
                {
                    item.Shatter = null;
                    item.ReleaseFrames();
                    _items.RemoveAt(i);
                    _dirty = true;
                    if (_items.Count == 0) SetActive(false);
                }
                // A breaking flash answers to the shards and to nothing else: no drift, no fade
                // ramp, no GIF advance (its FlashWindow is already gone and writes nothing).
                continue;
            }

            if (item.DeckShatter is { } deckBreak)
            {
                if (FlickerShatter.Step(deckBreak, delta.TotalSeconds)) _dirty = true;
                if (deckBreak.Done)
                {
                    item.DeckShatter = null;
                    item.ReleaseFrames();
                    _items.RemoveAt(i);
                    _dirty = true;
                    if (_items.Count == 0) SetActive(false);
                }
                continue;
            }

            if (item.Exit == null && item.BubbleOriginPx == null) StepPool(item, delta.TotalSeconds);
            if (item.Deck is { } deck && item.Exit == null) StepDeck(item, deck, delta.TotalSeconds);
            else if (item.DeckSparks is { } leftover)
            {
                // An exiting card's puff and dust still drift out instead of freezing in place.
                if (!FlickerShatter.StepSparks(leftover, delta.TotalSeconds)) item.DeckSparks = null;
                _dirty = true;
            }

            if (item.Exit is { } exit)
            {
                if (FlashExit.Step(exit, delta.TotalSeconds)) _dirty = true;
                if (exit.Done)
                {
                    item.Exit = null;
                    item.ReleaseFrames();
                    _items.RemoveAt(i);
                    _dirty = true;
                    if (_items.Count == 0) SetActive(false);
                }
                // Frozen where it was popped: no drift or pendulum swing while it leaves.
                continue;
            }

            // Flashes v2: step the motion here (the one place with delta time). Step answers
            // false for a Still item and for a zero delta, so a held flash stays clean.
            if (item.Motion != null && FlashMotion.Step(item.Motion, delta.TotalSeconds))
            {
                SyncRect(item, item.Motion);
                _dirty = true;
            }

            // Compare against what was last drawn instead of having FlashService announce its
            // writes: a missed call site there would be a STUCK-CLEAN (visually frozen) flash,
            // whereas a state compare self-heals on the next tick. A lucky pulse animates its
            // glow off ElapsedSec every frame, so it is legitimately dirty throughout.
            if ((item.HasGlow && item.LuckyPulse)
                || (item.DodgeUntilMs > 0 && Environment.TickCount64 <= item.DodgeUntilMs + 50)
                || item.Opacity != item.LastOpacity
                || item.FrameIndex != item.LastFrameIndex
                || item.DwellScale != item.LastDwellScale)
            {
                item.LastOpacity = item.Opacity;
                item.LastFrameIndex = item.FrameIndex;
                item.LastDwellScale = item.DwellScale;
                _dirty = true;
            }
        }
    }

    public override void Render(SKCanvas canvas, SKRectI boundsPx, double dpiScale, TimeSpan elapsed)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var frames = item.Frames;
            if (frames == null || frames.Length == 0 || item.Opacity <= 0) continue;

            var rect = new SKRect(item.X, item.Y, item.X + item.W, item.Y + item.H);

            // Flashes v2 wave 2: a dismissed flash draws as falling pieces of its LAST frame and
            // nothing else - no glow card, no dwell inflate, no pendulum rotation. The shards
            // leave the item's own rect, so this runs before the AABB cull and culls per shard.
            if (item.Shatter is { } shatter)
            {
                DrawShards(canvas, item, shatter, frames[Math.Clamp(item.FrameIndex, 0, frames.Length - 1)],
                    boundsPx);
                continue;
            }

            if (item.DeckShatter is { } deckBreak)
            {
                DrawDeckShards(canvas, item, deckBreak, frames[Math.Clamp(item.FrameIndex, 0, frames.Length - 1)]);
                continue;
            }

            // An exit can grow past its box (a swell, a melt), so it skips the AABB cull.
            if (item.Exit == null && !rect.IntersectsWith(boundsPx)) continue;   // cull to this monitor (the AABB)

            FlashExitSample? exit = item.Exit is { } exitState ? FlashExit.Sample(exitState) : null;
            var exitAlpha = exit?.Alpha ?? 1.0;
            var alpha = (byte)Math.Clamp(item.Opacity * item.EntranceAlpha * exitAlpha * 255, 0, 255);
            if (alpha == 0 && item.Exit != null) { DrawSparks(canvas, item); continue; }
            var image = frames[Math.Clamp(item.FrameIndex, 0, frames.Length - 1)];

            int saves = canvas.Save();
            // Pendulum: rotate the canvas about the pivot and draw the media hanging straight
            // down the rope in that frame. item.X..H stays the axis-aligned bounds of the rotated
            // picture (for gaze/click); the draw rect is the unrotated media in pivot space.
            if (item.Motion is { Style: FlashMotionStyle.Pendulum } pend)
            {
                canvas.RotateDegrees((float)(pend.AngleRad * 180.0 / Math.PI), (float)pend.PivotX, (float)pend.PivotY);
                var cx = (float)pend.PivotX;
                var cy = (float)(pend.PivotY + pend.Rope);
                var hw = (float)(pend.MediaW / 2.0);
                var hh = (float)(pend.MediaH / 2.0);
                rect = new SKRect(cx - hw, cy - hh, cx + hw, cy + hh);
            }
            // A Deck card that just took a new face eases into the new shape (CCP.Core DeckCardFit):
            // the size and the centre both move, so the card turns into its shape instead of popping.
            if (item.Deck is { } shaping && item.DeckShapeFrom is { } from)
            {
                var k = DeckCardFit.Progress(shaping.SinceSwap, item.DeckShapeSpan);
                if (k < 1)
                {
                    var now = new CardBox(rect.Left, rect.Top, rect.Width, rect.Height);
                    // A moving card keeps its own centre (the path owns it); a still one eases there too.
                    if (item.Motion is { Style: not FlashMotionStyle.Still })
                        from = new CardBox(now.Cx - from.W / 2, now.Cy - from.H / 2, from.W, from.H);
                    var b = DeckCardFit.Between(from, now, k);
                    rect = new SKRect((float)b.X, (float)b.Y, (float)(b.X + b.W), (float)(b.Y + b.H));
                }
            }
            // Gaze-dwell inflate about the rect center (RenderTransform ScaleTransform parity),
            // times how small the Whirlpool has drawn it in.
            if (item.DwellScale > 1.001 || item.PoolScale < 0.999)
            {
                var s = (float)(item.DwellScale * item.PoolScale);
                canvas.Translate(rect.MidX, rect.MidY);
                canvas.Scale(s, s);
                canvas.Translate(-rect.MidX, -rect.MidY);
            }
            // Super Flicker Deck: peek, lift, wobble and flip about the card's own centre.
            FlickerPose? pose = null;
            if (item.Deck is { } deck)
            {
                var p = FlickerDeck.Sample(deck, MotionFx.Level);
                // Juice: an exiting card eases out of its pose rather than snapping to rest.
                if (item.Exit is { } leaving) p = FlickerDeck.Settle(p, leaving.ElapsedSec);
                pose = p;
                var dx = (float)(item.DeckPeekDx * p.PeekFrac * rect.Width);
                var dy = (float)(item.DeckPeekDy * p.PeekFrac * rect.Width + p.RiseFrac * rect.Height);
                canvas.Translate(rect.MidX + dx, rect.MidY + dy);
                if (p.WobbleRad != 0) canvas.RotateRadians((float)p.WobbleRad);
                canvas.Scale((float)p.Scale, (float)(p.Scale * p.SquashY));
                // The turn (CCP.Core DeckTurn): width = cos of the angle, and a light perspective so the
                // far edge is shorter than the near one. One matrix, no allocation.
                var turnM = SKMatrix.CreateIdentity();
                turnM.ScaleX = (float)p.ScaleX;
                turnM.Persp0 = (float)DeckTurn.PerspectiveTerm(p.TurnRad, rect.Width / 2.0, p.Persp);
                canvas.Concat(ref turnM);
                canvas.Translate(-rect.MidX, -rect.MidY);
            }

            // Leave animation: scale / spin / slide about the pivot the style asks for.
            if (exit is { } ex)
            {
                var px = rect.MidX;
                var py = rect.Top + (float)(rect.Height * ex.PivotY);
                canvas.Translate(px, py + (float)(ex.OffsetY * rect.Height));
                if (ex.RotationDeg != 0) canvas.RotateDegrees((float)ex.RotationDeg);
                canvas.Scale((float)Math.Max(0.0001, ex.ScaleX), (float)Math.Max(0.0001, ex.ScaleY));
                canvas.Translate(-px, -py);
            }

            // The image sits PaddingPx inside the bookkeeping rect (glow inset), letterboxed
            // uniform like Stretch.Uniform - geometry preserves aspect, so fit is a no-op in
            // practice, but the 50px minimum clamp can distort slightly on tiny images.
            var padding = item.PaddingPx * (float)item.EntranceProgress;
            var inner = new SKRect(rect.Left + padding, rect.Top + padding,
                rect.Right - padding, rect.Bottom - padding);
            // A Deck card IS its picture's shape (DeckCardFit), so its face fills the card edge to edge;
            // while the card eases from one shape to the next the picture crops a little, never bars.
            var fit = item.Deck != null ? inner : UniformFit(image.Width, image.Height, inner);
            if (item.EntranceProgress < 1)
            {
                // Start with the same circle and center crop used by the bubble face.
                var radius = (float)(Math.Min(inner.Width, inner.Height) * .5 * (1 - item.EntranceProgress)
                    + item.CornerRadiusPx * item.EntranceProgress);
                canvas.ClipRoundRect(new SKRoundRect(inner, radius), antialias: true);
                var scale = Math.Max(inner.Width / image.Width, inner.Height / image.Height);
                var fw = image.Width * scale; var fh = image.Height * scale;
                fit = new SKRect(inner.MidX - fw / 2, inner.MidY - fh / 2,
                    inner.MidX + fw / 2, inner.MidY + fh / 2);
                _imagePaint.Color = new SKColor(255, 255, 255, alpha);
                canvas.DrawImage(image, fit, _imagePaint);
                // Shop look "card": a worn frame grows in with the card (it follows the morphing corner).
                if (item.Deck != null && DeckLookChoice.Back is var entering && DeckLooks.FrameShows(entering))
                    _deckLook.DrawFrame(canvas, inner, radius, entering, (byte)(alpha * item.EntranceProgress));
                canvas.RestoreToCount(saves);
                continue;
            }

            if (pose is { } shadowPose) DrawDeckShadow(canvas, item, fit, shadowPose.Lift, alpha);

            if (item.HasGlow)
            {
                // Halo: blurred round-rect behind the image (DropShadow depth-0 equivalent).
                var sigma = item.GlowSigmaPx;
                var glowAlpha = item.GlowOpacity;
                if (item.LuckyPulse)
                {
                    // 400ms auto-reverse: radius base->x1.6, opacity 0.7->1.0 (legacy anim).
                    var tri = Math.Abs(item.ElapsedSec % 0.8 / 0.4 - 1.0);   // 1..0..1 triangle
                    tri = 1.0 - tri;                                          // 0..1..0
                    sigma *= (float)(1.0 + 0.6 * tri);
                    glowAlpha = 0.7 + 0.3 * tri;
                }
                // Rebuild the mask filter only when the quantized sigma moves (blur filters
                // are expensive to churn per frame; lucky pulses quantize to 0.5px steps).
                var q = MathF.Round(sigma * 2f) / 2f;
                if (item.BlurCache == null || Math.Abs(q - item.BlurCacheSigma) > 0.01f)
                {
                    item.BlurCache?.Dispose();
                    item.BlurCache = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.5f, q));
                    item.BlurCacheSigma = q;
                }
                _fillPaint.MaskFilter = item.BlurCache;
                _fillPaint.Color = item.GlowColor.WithAlpha(
                    (byte)Math.Clamp(glowAlpha * item.Opacity * item.EntranceAlpha * 255, 0, 255));
                canvas.DrawRoundRect(new SKRoundRect(fit, item.CornerRadiusPx), _fillPaint);
                _fillPaint.MaskFilter = null;

                // Rounded clip so the image corners match the glow card (legacy clip Border).
                canvas.ClipRoundRect(new SKRoundRect(fit, item.CornerRadiusPx), antialias: true);
            }
            else
            {
                // Legacy non-glow content: black backing behind the (letterboxed) image.
                // Wave 2: with rounded corners on, the backing rounds too and the image is clipped
                // to the same round-rect, so no square black shoulder survives behind a corner.
                _fillPaint.Color = new SKColor(0, 0, 0, alpha);
                if (item.CornerRadiusPx > 0)
                {
                    canvas.DrawRoundRect(new SKRoundRect(inner, item.CornerRadiusPx), _fillPaint);
                    canvas.ClipRoundRect(new SKRoundRect(fit, item.CornerRadiusPx), antialias: true);
                }
                else
                {
                    canvas.DrawRect(inner, _fillPaint);
                    if (item.Deck != null) canvas.ClipRect(inner, antialias: true);   // the face may crop while it reshapes
                }
            }

            _imagePaint.Color = new SKColor(255, 255, 255, alpha);
            var back = DeckLookChoice.Back;
            // Shop look "behind" (DeckSpiralBack): the card's back is a spiral with a phrase coming at you.
            var onBack = item.Deck != null && item.DeckOnBack && item.DeckSpiral != null && DeckLookChoice.Whisper;
            if (exit is { Glitch: > 0 } && item.Exit is { } glitchState)
                DrawGlitch(canvas, image, fit, alpha, glitchState, exit.Value.Glitch);
            else if (onBack)
                DrawBackFace(canvas, item, fit, alpha);
            else if (item.Deck != null)
                DrawCover(canvas, image, fit, _imagePaint);
            else
                canvas.DrawImage(image, fit, _imagePaint);
            // Motion Off: no turn, the old face fades out over the new one (DeckTurn.FadeOut).
            if (item.Deck is { } fading && exit == null && (item.DeckFadeFrames != null || item.DeckFadeFromBack || item.DeckFadeFromFront))
            {
                var oa = (byte)Math.Clamp(DeckTurn.FadeOut(fading.SinceSwap, Safety.GentleVisuals.On) * alpha, 0, 255);
                if (oa > 0)
                {
                    var oldFace = item.DeckFadeFromFront && onBack ? image
                        : item.DeckFadeFrames is { Length: > 0 } old ? old[0] : null;
                    if (item.DeckFadeFromBack && item.DeckSpiral != null && !onBack) DrawBackFace(canvas, item, fit, oa, word: false);
                    else if (oldFace != null)
                    {
                        _imagePaint.Color = new SKColor(255, 255, 255, oa);
                        DrawCover(canvas, oldFace, fit, _imagePaint);
                        _imagePaint.Color = new SKColor(255, 255, 255, alpha);
                    }
                }
            }
            // The turn: the face darkens toward the edge that is going away.
            if (pose is { Shade: > 0.004 } shaded && exit == null)
                _deckLook.DrawShade(canvas, fit, shaded.Shade, shaded.TurnRad > 0, alpha);
            // Shop look "card" (sold as Card frames): the worn frame round every Deck card, the whole time
            // the picture is up. Plain draws nothing; badges below then keep inside the band.
            var badgeBox = fit;
            if (item.Deck != null && DeckLooks.FrameShows(back))
            {
                var band = _deckLook.DrawFrame(canvas, fit, item.CornerRadiusPx, back, alpha);
                if (band > 0) badgeBox = SKRect.Inflate(fit, -band, -band);
            }
            if (exit is { Whiten: > 0 } lit)
            {
                // TV off: the picture burns bright as it collapses to its line.
                _fillPaint.MaskFilter = null;
                _fillPaint.Color = new SKColor(255, 255, 255, (byte)Math.Clamp(lit.Whiten * alpha, 0, 255));
                canvas.DrawRoundRect(new SKRoundRect(fit, item.CornerRadiusPx), _fillPaint);
            }
            if (item.NatashaCue)
            {
                var wash = Chaster.NatashasFavourite.WashAlphaAt(item.ElapsedSec);
                if (wash > 0.003)
                {
                    _fillPaint.MaskFilter = null;
                    _fillPaint.Color = new SKColor(Chaster.NatashasFavourite.R, Chaster.NatashasFavourite.G, Chaster.NatashasFavourite.B,
                        (byte)Math.Clamp(wash * alpha, 0, 255));
                    canvas.DrawRoundRect(new SKRoundRect(fit, item.CornerRadiusPx), _fillPaint);
                }
            }
            if (pose is { Sheen: > 0.004 } lit2) DrawDeckSheen(canvas, fit, lit2.Sheen, lit2.SheenPos, alpha);
            if (pose is { Crack: > 0 } cracked && item.Deck is { } crackDeck)
                DrawCracks(canvas, crackDeck, fit, cracked.Crack, cracked.CrackReach, alpha);
            if (item.DodgeUntilMs > 0 && item.Exit == null) DrawDodgeRing(canvas, badgeBox, alpha, item.DodgeUntilMs);
            if (item.Credit && item.Exit == null && !onBack) DrawCredit(canvas, badgeBox, alpha, dpiScale);
            canvas.RestoreToCount(saves);
            if (item.Exit != null) DrawSparks(canvas, item);
            if (item.DeckSparks != null && item.Deck != null) DrawDeckPuff(canvas, item.DeckSparks, rect.MidX, rect.MidY, item.Opacity);
        }
    }

    /// <summary>"via Scrolller" in the picture's bottom-right corner, like the video badge: 11 DIP,
    /// white at 75% of the flash's own alpha over a soft dark shadow, so it fades with the flash.</summary>
    private void DrawCredit(SKCanvas canvas, SKRect fit, byte alpha, double dpiScale)
    {
        var scale = (float)(dpiScale > 0 ? dpiScale : 1.0);
        var size = 11f * scale;
        if (fit.Width < size * 8 || fit.Height < size * 3) return;   // too small to carry it
        _creditPaint.TextSize = size;
        var x = fit.Right - 8f * scale;
        var y = fit.Bottom - 7f * scale;
        var a = alpha * 0.75f;
        _creditPaint.Color = new SKColor(0, 0, 0, (byte)Math.Clamp(a * 0.8f, 0, 255));
        canvas.DrawText(Video.ScrolllerCredit.Mark, x + scale, y + scale, _creditPaint);
        _creditPaint.Color = new SKColor(255, 255, 255, (byte)Math.Clamp(a, 0, 255));
        canvas.DrawText(Video.ScrolllerCredit.Mark, x, y, _creditPaint);
    }

    private static readonly SKColor SparkPink = new(0xFF, 0x69, 0xB4);

    /// <summary>Natasha's dodge: a small red dial in the picture's top-right corner, draining
    /// clockwise from twelve as the ring runs out.</summary>
    private void DrawDodgeRing(SKCanvas canvas, SKRect fit, byte alpha, long untilMs)
    {
        var left = Chaster.NatashasFavourite.DodgeLeft(Chaster.NatashasFavourite.DodgeMs - (untilMs - Environment.TickCount64));
        if (left <= 0.001) return;
        float r = Math.Clamp(Math.Min(fit.Width, fit.Height) * 0.06f, 12f, 22f);
        float cx = fit.Right - r - 10f, cy = fit.Top + r + 10f;
        _fillPaint.MaskFilter = null;
        _fillPaint.Color = new SKColor(0x10, 0x06, 0x0C, (byte)(0.6 * alpha));
        canvas.DrawCircle(cx, cy, r + 3f, _fillPaint);
        _ringPaint.StrokeWidth = Math.Max(3f, r * 0.24f);
        _ringPaint.Color = new SKColor(Chaster.NatashasFavourite.R, Chaster.NatashasFavourite.G, Chaster.NatashasFavourite.B, alpha);
        canvas.DrawArc(new SKRect(cx - r, cy - r, cx + r, cy + r), -90f, 360f * (float)left, false, _ringPaint);
    }

    /// <summary>Pop's spray of pink sparks, in world space around the flash's box.</summary>
    private void DrawSparks(SKCanvas canvas, FlashItem item)
    {
        if (item.Exit is not { } exit) return;
        int n = FlashExit.SparkCount(exit);
        if (n == 0) return;
        float cx = item.X + item.W / 2, cy = item.Y + item.H / 2;
        float half = Math.Max(item.W, item.H) / 2;
        _fillPaint.MaskFilter = null;
        for (int i = 0; i < n; i++)
        {
            var sp = FlashExit.Spark(exit, i);
            if (sp.Alpha <= 0 || sp.RadiusPx <= 0.2) continue;
            _fillPaint.Color = SparkPink.WithAlpha((byte)Math.Clamp(sp.Alpha * 255, 0, 255));
            canvas.DrawCircle(cx + (float)(sp.Dx * sp.Distance * half), cy + (float)(sp.Dy * sp.Distance * half),
                (float)sp.RadiusPx, _fillPaint);
        }
    }

    /// <summary>
    /// Glitch exit: the picture in horizontal slices knocked sideways, plus a red and a cyan ghost
    /// pulled apart. Everything stays inside one frame of the image, so it costs a handful of
    /// sub-rect blits and no extra surfaces.
    /// </summary>
    private void DrawGlitch(SKCanvas canvas, SKImage image, SKRect fit, byte alpha, FlashExitState state, double amount)
    {
        const int slices = 7;
        float split = (float)(amount * 0.04 * fit.Width);
        if (split > 0.5f)
        {
            using var red = SKColorFilter.CreateBlendMode(new SKColor(255, 40, 90, (byte)(alpha / 2)), SKBlendMode.Modulate);
            using var cyan = SKColorFilter.CreateBlendMode(new SKColor(40, 230, 255, (byte)(alpha / 2)), SKBlendMode.Modulate);
            _imagePaint.ColorFilter = red;
            canvas.DrawImage(image, new SKRect(fit.Left - split, fit.Top, fit.Right - split, fit.Bottom), _imagePaint);
            _imagePaint.ColorFilter = cyan;
            canvas.DrawImage(image, new SKRect(fit.Left + split, fit.Top, fit.Right + split, fit.Bottom), _imagePaint);
            _imagePaint.ColorFilter = null;
        }
        _imagePaint.Color = new SKColor(255, 255, 255, alpha);
        for (int k = 0; k < slices; k++)
        {
            float t0 = (float)k / slices, t1 = (float)(k + 1) / slices;
            var src = new SKRect(0, image.Height * t0, image.Width, image.Height * t1);
            float dx = (float)(FlashExit.SliceOffset(state, k) * fit.Width);
            var dst = new SKRect(fit.Left + dx, fit.Top + fit.Height * t0, fit.Right + dx, fit.Top + fit.Height * t1);
            canvas.DrawImage(image, src, dst, _imagePaint);
        }
    }

    /// <summary>
    /// Flashes v2 wave 2: draw one broken flash. Every shard is a sub-rect of the same frame the
    /// flash was showing, blitted at the shard's offset, turned about its own centre and faded
    /// with the rest of them.
    ///
    /// The geometry mirrors the classic draw above it so nothing jumps at the break: a pendulum
    /// rotates the canvas about its pivot by the angle FROZEN at the dismiss and cuts from the
    /// media rect in pivot space, a gaze-dwell pop keeps the inflate it was wearing, and the cut
    /// is taken over the LETTERBOXED image box (glow padding removed). A non-glow flash keeps its
    /// black backing, now per shard, so a GIF with transparency in it does not turn to ghosts
    /// halfway down the screen.
    ///
    /// What it deliberately does NOT carry over: the glow halo (a shattered flash is no longer a
    /// card, so there is no card to light) and the corner radius (a break exposes hard edges, and
    /// rounding every shard would read as a bag of lozenges).
    /// </summary>
    private void DrawShards(SKCanvas canvas, FlashItem item, FlashShatterState shatter, SKImage image,
        SKRectI boundsPx)
    {
        var rect = new SKRect((float)shatter.RectX, (float)shatter.RectY,
            (float)(shatter.RectX + shatter.RectW), (float)(shatter.RectY + shatter.RectH));

        int saves = canvas.Save();
        // Pendulum: the same rotate-about-the-pivot the classic path does, at the frozen angle.
        // The pieces then fall down the picture's own axis rather than the screen's, which is
        // what a thing coming apart mid-swing does.
        var tilted = shatter.FrozenAngleRad != 0;
        if (tilted)
            canvas.RotateDegrees((float)(shatter.FrozenAngleRad * 180.0 / Math.PI),
                (float)shatter.PivotX, (float)shatter.PivotY);

        // Gaze-dwell inflate about the rect center, as the classic path has it: a stare-to-pop
        // must not start the break by shrinking the picture 10%.
        if (item.DwellScale > 1.001)
        {
            var ds = (float)item.DwellScale;
            canvas.Translate(rect.MidX, rect.MidY);
            canvas.Scale(ds, ds);
            canvas.Translate(-rect.MidX, -rect.MidY);
        }

        var inner = new SKRect(rect.Left + item.PaddingPx, rect.Top + item.PaddingPx,
            rect.Right - item.PaddingPx, rect.Bottom - item.PaddingPx);
        var fit = UniformFit(image.Width, image.Height, inner);
        if (fit.Width <= 0 || fit.Height <= 0) { canvas.RestoreToCount(saves); return; }

        foreach (var shard in shatter.Shards)
        {
            var a = shard.Alpha * item.Opacity;
            if (a <= 0) continue;

            var dx = (float)shard.Dx;
            var dy = (float)shard.Dy;
            var dest = new SKRect(
                fit.Left + (float)(shard.U0 * fit.Width) + dx,
                fit.Top + (float)(shard.V0 * fit.Height) + dy,
                fit.Left + (float)(shard.U1 * fit.Width) + dx,
                fit.Top + (float)(shard.V1 * fit.Height) + dy);
            if (dest.Width <= 0 || dest.Height <= 0) continue;

            // Cull generously: the tumble can push a shard's corners a little past its own box.
            // A tilted rig is not culled at all - these rects are in pivot space, so testing them
            // against a screen rect would throw away pieces that are plainly on the monitor, and
            // a pendulum break is a handful of shards for 0.7 s.
            if (!tilted)
            {
                var slack = Math.Max(dest.Width, dest.Height);
                if (!SKRect.Create(dest.Left - slack, dest.Top - slack,
                        dest.Width + 2 * slack, dest.Height + 2 * slack).IntersectsWith(boundsPx))
                    continue;
            }

            var src = new SKRect(
                (float)(shard.U0 * image.Width), (float)(shard.V0 * image.Height),
                (float)(shard.U1 * image.Width), (float)(shard.V1 * image.Height));

            var shardAlpha = (byte)Math.Clamp(a * 255, 0, 255);
            int shardSaves = canvas.Save();
            canvas.RotateDegrees((float)(shard.AngleRad * 180.0 / Math.PI), dest.MidX, dest.MidY);
            if (!item.HasGlow)
            {
                // The legacy black backing, cut up and falling with the picture it was behind.
                _fillPaint.Color = new SKColor(0, 0, 0, shardAlpha);
                canvas.DrawRect(dest, _fillPaint);
            }
            _imagePaint.Color = new SKColor(255, 255, 255, shardAlpha);
            canvas.DrawImage(image, src, dest, _imagePaint);
            canvas.RestoreToCount(shardSaves);
        }
        canvas.RestoreToCount(saves);
    }

    #region Super Flicker Deck

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct CursorPoint { public int X, Y; }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint pt);

    /// <summary>
    /// Which card the cursor is over, and whether it sits under another one. Only a buried card
    /// peeks: it slides out from the middle of the cluster it is in. One cursor read per tick, and
    /// none at all while no flash is a card.
    /// </summary>
    private void UpdateDeckHover()
    {
        var anyDeck = false;
        foreach (var it in _items) if (it.Deck != null) { anyDeck = true; break; }
        if (!anyDeck) return;

        FlashItem? top = null;
        if (GetCursorPos(out var cur))
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var it = _items[i];
                if (!Live(it) || it.Opacity <= 0) continue;
                if (cur.X >= it.X && cur.X <= it.X + it.W && cur.Y >= it.Y && cur.Y <= it.Y + it.H) { top = it; break; }
            }
        }

        foreach (var it in _items)
        {
            if (it.Deck == null) continue;
            var hover = false;
            if (ReferenceEquals(it, top))
            {
                // Buried = something drawn above it overlaps it. The cluster is every live flash
                // overlapping it, itself included.
                var at = _items.IndexOf(it);
                double sx = 0, sy = 0;
                int n = 0;
                var buried = false;
                for (int j = 0; j < _items.Count; j++)
                {
                    var o = _items[j];
                    if (!Live(o)) continue;
                    var self = ReferenceEquals(o, it);
                    if (!self && !Overlaps(o, it)) continue;
                    if (j > at && !self) buried = true;
                    sx += o.X + o.W / 2;
                    sy += o.Y + o.H / 2;
                    n++;
                }
                if (buried && n > 1)
                {
                    hover = true;
                    var (dx, dy) = FlickerDeck.PeekDirection(it.X + it.W / 2, it.Y + it.H / 2, sx / n, sy / n);
                    it.DeckPeekDx = (float)dx;
                    it.DeckPeekDy = (float)dy;
                }
            }
            if (it.Deck.Hover != hover) { it.Deck.Hover = hover; _dirty = true; }
        }
    }

    private static bool Live(FlashItem it) => it.Shatter == null && it.Exit == null && it.DeckShatter == null;

    private static bool Overlaps(FlashItem a, FlashItem b)
        => a.X < b.X + b.W && b.X < a.X + a.W && a.Y < b.Y + b.H && b.Y < a.Y + a.H;

    /// <summary>Step one card and act on what the step says: raise it, swap its picture, or give up.</summary>
    /// <summary>
    /// Super, the Whirlpool: every plain flash inside its reach is dragged in and around, drawn
    /// smaller the closer it gets, and handed to FlashService once it reaches the eye. A flash on
    /// a v2 motion path keeps that path. Costs two compares while the pool is off.
    /// </summary>
    private void StepPool(FlashItem item, double dt)
    {
        var stage = Super.SuperStageHost.Stage;
        bool moving = item.Motion is { Style: not FlashMotionStyle.Still };
        var pull = stage.PoolLive && !moving && !item.PoolEaten
            ? stage.Pull(item.X + item.W / 2, item.Y + item.H / 2, 1.4, SuperCore.PullKind.Picture)
            : default;
        double want = pull.Inside ? pull.Scale : 1;
        if (!pull.Inside && item.PoolVx == 0 && item.PoolVy == 0 && item.PoolScale >= 0.999)
        {
            item.PoolScale = 1;
            return;
        }
        item.PoolVx += pull.Ax * dt; item.PoolVy += pull.Ay * dt;
        double damp = Math.Max(0, 1 - 2.4 * dt);
        item.PoolVx *= damp; item.PoolVy *= damp;
        if (Math.Abs(item.PoolVx) < 0.5 && Math.Abs(item.PoolVy) < 0.5 && !pull.Inside) { item.PoolVx = 0; item.PoolVy = 0; }
        item.X += (float)(item.PoolVx * dt); item.Y += (float)(item.PoolVy * dt);
        item.PoolScale += (want - item.PoolScale) * (1 - Math.Exp(-8 * dt));
        _dirty = true;
        if (pull.Eaten)
        {
            item.PoolEaten = true;
            item.OnPoolEaten?.Invoke(item);
        }
    }

    private void StepDeck(FlashItem item, FlickerDeckState deck, double dt)
    {
        // A flip to the spiral back has its face ready at once (rolled at the press).
        var ev = FlickerDeck.Step(deck, dt, item.DeckPendingFrames != null || item.DeckBackNext, MotionFx.Level);
        if ((ev & FlickerEvents.Raise) != 0)
        {
            BringToFront(item);
            item.OnDeckRaise?.Invoke(item);
        }
        var off = MotionFx.Level == MotionLevel.Off;
        if ((ev & FlickerEvents.Swap) != 0 && item.DeckBackNext)
        {
            // Edge-on: the back turns into view. The picture stays in Frames for the next flip's fall-back.
            item.DropDeckFade();
            if (off) item.DeckFadeFromFront = true;   // Off: the picture (still in Frames) fades out over the back
            item.DeckBackNext = false;
            item.DeckOnBack = true;
            item.DeckShapeFrom = null;
            item.DeckSparks = SwapPuff(item, deck);
        }
        else if ((ev & FlickerEvents.Swap) != 0 && item.DeckPendingFrames is { Length: > 0 } next)
        {
            var old = item.Frames;
            var fromBack = item.DeckOnBack;
            item.DropDeckFade();
            item.Frames = next;
            item.DeckPendingFrames = null;
            item.FrameIndex = 0;
            item.LastFrameIndex = -1;
            item.DeckOnBack = false;
            if (off && fromBack) item.DeckFadeFromBack = true;
            if (off && !fromBack && old != null) item.DeckFadeFrames = old;   // fades out over the new face, then goes
            else if (old != null)
                foreach (var f in old) { try { f.Dispose(); } catch { } }
            Reshape(item, next[0], off);
            item.DeckSparks = SwapPuff(item, deck);
            item.OnDeckSwap?.Invoke(item);
        }
        // Off: the Back-to-picture and picture-to-picture fades let go of the old face once it is gone.
        if (item.DeckFadeFrames != null || item.DeckFadeFromBack || item.DeckFadeFromFront)
        {
            if (deck.SinceSwap >= DeckTurn.FadeLength(Safety.GentleVisuals.On)) item.DropDeckFade();
            _dirty = true;
        }
        if (item.DeckShapeFrom != null)
        {
            if (deck.SinceSwap >= item.DeckShapeSpan) item.DeckShapeFrom = null;
            _dirty = true;
        }
        if (item.DeckSparks is { } puff)
        {
            if (FlickerShatter.StepSparks(puff, dt)) _dirty = true;
            else { item.DeckSparks = null; _dirty = true; }
        }
        if ((ev & FlickerEvents.GaveUp) != 0)
        {
            // No new picture came: the card opens on the one it still holds, never on a stale back.
            item.DeckOnBack = false;
            item.DeckBackNext = false;
            item.OnDeckGaveUp?.Invoke(item);
        }
        if (ev != FlickerEvents.None || (MotionFx.Level != MotionLevel.Off && FlickerDeck.Animating(deck)))
            _dirty = true;
        StepBack(item, deck, dt);
    }

    /// <summary>
    /// A new face reshapes the card to its picture (CCP.Core DeckCardFit): same area, the picture's own
    /// aspect, about the same centre, kept on its monitor. The click box moves at once; the draw eases
    /// from the old shape over the opening half of the flip (Off: over the cross-fade).
    /// </summary>
    private static void Reshape(FlashItem item, SKImage picture, bool off)
    {
        if (picture.Width <= 0 || picture.Height <= 0) return;
        var from = new CardBox(item.X, item.Y, item.W, item.H);
        var bounds = new CardBox(item.DeckBounds.Left, item.DeckBounds.Top, item.DeckBounds.Width, item.DeckBounds.Height);
        var to = DeckCardFit.Refit(from, item.PaddingPx, picture.Width / (double)picture.Height, bounds);
        if (Math.Abs(to.W - from.W) < 1 && Math.Abs(to.H - from.H) < 1 && Math.Abs(to.X - from.X) < 1 && Math.Abs(to.Y - from.Y) < 1)
            return;
        item.DeckShapeFrom = from;
        item.DeckShapeSpan = off ? DeckTurn.FadeLength(Safety.GentleVisuals.On) : FlickerDeck.FlipSec / 2;
        if (item.Motion is { } m && m.Style != FlashMotionStyle.Still)
        {
            // A moving card keeps its path: its media size changes about its own centre.
            double cx = m.X + m.W / 2, cy = m.Y + m.H / 2;
            m.MediaW = to.W; m.MediaH = to.H;
            if (m.Style != FlashMotionStyle.Pendulum)
            {
                m.W = to.W; m.H = to.H; m.X = cx - to.W / 2; m.Y = cy - to.H / 2;
            }
            SyncRect(item, m);
            return;
        }
        item.X = (float)to.X; item.Y = (float)to.Y; item.W = (float)to.W; item.H = (float)to.H;
    }

    // The spiral back is worked out small and stretched (SKIA STRETCH TRAP: Bgra8888, Low): it is soft
    // curves, and it is rolled once per flip to the back, never per frame. One generator for the layer.
    // 320 like the Whirlpool's: at 192 a dense spiral stair-stepped once stretched over a big card.
    internal const int BackSpiralPx = 320;
    private SuperCore.LoomSpiral? _backLoom;
    private byte[]? _backPx;

    /// <summary>
    /// Shop look "behind": called on a press. With the look worn and the card showing a picture, this flip
    /// turns to the spiral back: a new Loom spiral and a new phrase are rolled now, so the face is ready at
    /// the edge. Returns false when the flip should fetch a picture as usual.
    /// </summary>
    public bool PrepareDeckBack(FlashItem item)
    {
        item.DeckBackNext = false;
        if (item.Deck == null || item.DeckOnBack || !DeckLookChoice.Whisper) return false;
        try { RollBack(item, App.Settings?.Current?.SubliminalPool, AccentRgb()); }
        catch (Exception ex) { App.Logger?.Debug("Deck spiral back: {E}", ex.Message); return false; }
        item.DeckBackNext = true;
        return true;
    }

    /// <summary>Roll the back's spiral into the card's own small bitmap, and its phrase. Tests call it directly.</summary>
    internal void RollBack(FlashItem item, IEnumerable<KeyValuePair<string, bool>>? pool, uint accentRgb)
    {
        var spec = DeckSpiralBack.Roll(_deckRng, accentRgb, item.DeckSpiralSpec);
        _backLoom ??= new SuperCore.LoomSpiral(BackSpiralPx);
        _backPx ??= new byte[BackSpiralPx * BackSpiralPx * 4];
        _backLoom.Clear();
        _backLoom.Add(spec, 0, 0, 1);
        uint ground = DeckSpiralBack.Ground(accentRgb);
        _backLoom.CopyToBgraOver(_backPx, ground & 0xFFFFFF);
        item.DeckSpiral ??= new SKBitmap(new SKImageInfo(BackSpiralPx, BackSpiralPx, SKColorType.Bgra8888, SKAlphaType.Premul));
        System.Runtime.InteropServices.Marshal.Copy(_backPx, 0, item.DeckSpiral.GetPixels(), _backPx.Length);
        item.DeckSpiral.NotifyPixelsChanged();
        item.DeckBackCache?.Invalidate();   // same bitmap, new pixels: the half-size back draws again
        item.DeckSpiralSpec = spec;
        item.DeckSpiralGround = ground;
        item.DeckSpiralTurn = _deckRng.NextDouble() * Math.PI * 2;
        var phrase = DeckWhisper.Pick(pool, item.WhisperText, _deckRng, DeckSpiralBack.MaxChars, lower: false);
        item.WhisperText = phrase;
        item.WhisperLines = phrase != null ? DeckSpiralBack.Lines(phrase) : null;
        item.WhisperHue = (float)(270 + _deckRng.NextDouble() * 65);   // the slot's house flavour, pink to purple
    }

    private static uint AccentRgb()
    {
        try
        {
            if (App.Mods is { } mods) { var (r, g, b) = mods.GetAccentColorRgb(); return ((uint)r << 16) | ((uint)g << 8) | b; }
        }
        catch { }
        return 0xFF69B4;
    }

    /// <summary>The back turns slowly while it shows, and keeps the frame moving while its phrase is up.</summary>
    private void StepBack(FlashItem item, FlickerDeckState deck, double dt)
    {
        if (!item.DeckOnBack && !item.DeckFadeFromBack) return;
        if (!DeckLookChoice.Whisper) { if (item.DeckOnBack) { item.DeckOnBack = false; _dirty = true; } return; }
        var level = MotionFx.Level;
        var rate = DeckSpiralBack.TurnRate(level == MotionLevel.Off, Safety.GentleVisuals.On, level == MotionLevel.Reduced ? 0.5 : 1.0);
        if (rate > 0) { item.DeckSpiralTurn += rate * dt; _dirty = true; }
        if (item.WhisperLines != null && deck.SinceSwap < DeckSpiralBack.Life(level == MotionLevel.Off, Safety.GentleVisuals.On)) _dirty = true;
    }

    /// <summary>The spiral back filling <paramref name="r"/>, and its phrase coming at the viewer.</summary>
    private void DrawBackFace(SKCanvas canvas, FlashItem item, SKRect r, byte alpha, bool word = true)
    {
        if (item.DeckSpiral == null) return;
        _deckLook.DrawSpiralBack(canvas, r, item.DeckSpiral, item.DeckSpiralGround, item.DeckSpiralTurn, alpha, item.DeckBackCache ??= new());
        if (!word || item.Deck is not { } deck || item.WhisperLines == null || item.Exit != null) return;
        var level = MotionFx.Level;
        var p = DeckSpiralBack.Word(deck.SinceSwap, level == MotionLevel.Reduced ? 0.5 : 1.0, level == MotionLevel.Off, Safety.GentleVisuals.On);
        _deckLook.DrawBackWord(canvas, r, item.WhisperLines, p, item.WhisperHue, alpha);
    }

    /// <summary>A picture filling <paramref name="dst"/> edge to edge, cropped about its centre (the canvas is clipped to the card).</summary>
    private static void DrawCover(SKCanvas canvas, SKImage image, SKRect dst, SKPaint paint)
    {
        if (image.Width <= 0 || image.Height <= 0 || dst.Width <= 0 || dst.Height <= 0) return;
        float scale = Math.Max(dst.Width / image.Width, dst.Height / image.Height);
        float sw = dst.Width / scale, sh = dst.Height / scale;
        var src = new SKRect((image.Width - sw) / 2f, (image.Height - sh) / 2f, (image.Width + sw) / 2f, (image.Height + sh) / 2f);
        canvas.DrawImage(image, src, dst, paint);
    }

    /// <summary>The card's shadow grows and drops as it lifts (mockup: blur 8 + 24 lift, offset 3 + 12 lift).</summary>
    private void DrawDeckShadow(SKCanvas canvas, FlashItem item, SKRect fit, double lift, byte alpha)
    {
        var sigma = MathF.Round((float)(4 + 12 * lift) * 2f) / 2f;
        if (item.DeckShadowCache == null || Math.Abs(sigma - item.DeckShadowSigma) > 0.01f)
        {
            item.DeckShadowCache?.Dispose();
            item.DeckShadowCache = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, Math.Max(0.5f, sigma));
            item.DeckShadowSigma = sigma;
        }
        _fillPaint.MaskFilter = item.DeckShadowCache;
        _fillPaint.Color = new SKColor(0, 0, 0, (byte)(0.65 * alpha));
        var off = (float)(3 + 12 * lift);
        // The rect overload: no native SKRoundRect allocated every frame per card.
        canvas.DrawRoundRect(new SKRect(fit.Left, fit.Top + off, fit.Right, fit.Bottom + off),
            item.CornerRadiusPx, item.CornerRadiusPx, _fillPaint);
        _fillPaint.MaskFilter = null;
    }

    /// <summary>
    /// Juice: the flip's puff, plus dust shaken out of the crack when this flip deepens it. The
    /// dust leaves from the cracks' impact point, measured on the card's padded picture box.
    /// </summary>
    private FlickerSpark[] SwapPuff(FlashItem item, FlickerDeckState deck)
    {
        var motes = FlickerDeck.CrackMotes(deck.Flips, deck.BreakAt, MotionFx.Level);
        if (motes <= 0 || deck.Cracks.Length == 0 || deck.Cracks[0].Length < 2)
            return FlickerShatter.SwapSparks(MotionFx.Level, _deckRng);
        double w = Math.Max(1, item.W - 2 * item.PaddingPx), h = Math.Max(1, item.H - 2 * item.PaddingPx);
        return FlickerShatter.SwapSparks(MotionFx.Level, _deckRng, motes,
            (deck.Cracks[0][0] - 0.5) * w, (deck.Cracks[0][1] - 0.5) * h);
    }

    // Juice: the flip's lit band, one unit-space gradient built once and mapped onto the face each frame.
    private readonly SKPaint _sheenPaint = new()
    {
        IsAntialias = true,
        Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(1, 0),
            new[] { new SKColor(255, 236, 246, 0), new SKColor(255, 236, 246, 255), new SKColor(255, 236, 246, 0) },
            new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp),
    };

    /// <summary>
    /// A thin lit band sweeping the face through the flip, slanted, tinted pale pink and low (never a
    /// white flash). Drawn in the card's own space, so it narrows with the flip like the face does.
    /// </summary>
    private void DrawDeckSheen(SKCanvas canvas, SKRect fit, double sheen, double pos, byte alpha)
    {
        var a = (byte)Math.Clamp(sheen * alpha, 0, 255);
        if (a == 0) return;
        int saves = canvas.Save();
        canvas.ClipRect(fit, antialias: true);
        const float Slant = 0.12f;   // the band leans back by this share of the height
        var bw = fit.Width * 0.22f;
        var lean = Slant * fit.Height;
        // Starts fully off the left edge and leaves fully off the right one, slant included.
        var x = fit.Left - bw + (float)pos * (fit.Width + bw + lean);
        canvas.Translate(x, fit.Top);
        canvas.Skew(-Slant, 0);
        canvas.Scale(bw, fit.Height);
        _sheenPaint.Color = new SKColor(255, 255, 255, a);
        canvas.DrawRect(0, 0, 1, 1, _sheenPaint);
        canvas.RestoreToCount(saves);
    }

    /// <summary>
    /// Cracks over the picture: a dark under-stroke and a white line, faint one flip early. Juice:
    /// <paramref name="reach"/> below 1 draws each hairline only part of its way out from the impact.
    /// </summary>
    private void DrawCracks(SKCanvas canvas, FlickerDeckState deck, SKRect fit, double amount, double reach, byte alpha)
    {
        _deckPath.Rewind();
        foreach (var line in deck.Cracks)
        {
            if (line.Length < 4) continue;
            var segs = line.Length / 2 - 1;
            var run = Math.Clamp(reach, 0, 1) * segs;
            if (run <= 0) continue;
            _deckPath.MoveTo(fit.Left + (float)(line[0] * fit.Width), fit.Top + (float)(line[1] * fit.Height));
            for (int k = 1; k <= segs && k - 1 < run; k++)
            {
                var f = Math.Min(1, run - (k - 1));
                var px = line[k * 2 - 2] + (line[k * 2] - line[k * 2 - 2]) * f;
                var py = line[k * 2 - 1] + (line[k * 2 + 1] - line[k * 2 - 1]) * f;
                _deckPath.LineTo(fit.Left + (float)(px * fit.Width), fit.Top + (float)(py * fit.Height));
            }
        }
        // Hairlines: a barely-there under-stroke and a thin pale line, so a crack reads as a flaw, not a drawing.
        var w = Math.Max(0.8f, Math.Min(fit.Width, fit.Height) * 0.0028f);
        _ringPaint.StrokeWidth = w * 1.8f;
        _ringPaint.Color = new SKColor(0, 0, 0, (byte)(0.22 * amount * alpha));
        canvas.DrawPath(_deckPath, _ringPaint);
        _ringPaint.StrokeWidth = w;
        _ringPaint.Color = new SKColor(255, 255, 255, (byte)(0.55 * amount * alpha));
        canvas.DrawPath(_deckPath, _ringPaint);
    }

    private static readonly SKColor SparkWhite = new(0xFF, 0xFF, 0xFF);
    private static readonly SKColor SparkDeckPink = new(0xFF, 0x8F, 0xD0);
    private static readonly SKColor SparkDust = new(0xE8, 0xDC, 0xE4);

    /// <summary>The flip's puff: additive white dots about the card's centre, unaffected by its pose.</summary>
    private void DrawDeckPuff(SKCanvas canvas, FlickerSpark[] sparks, float cx, float cy, double opacity)
    {
        _fillPaint.MaskFilter = null;
        _fillPaint.BlendMode = SKBlendMode.Plus;
        foreach (var sp in sparks)
        {
            var sa = sp.Alpha * opacity;
            if (sa <= 0) continue;
            if (sp.Mote)
            {
                // Crack dust: small, dim, painted over (never added), so it reads as grit, not light.
                _fillPaint.BlendMode = SKBlendMode.SrcOver;
                _fillPaint.Color = SparkDust.WithAlpha((byte)Math.Clamp(sa * 0.55 * 255, 0, 255));
                canvas.DrawCircle((float)(cx + sp.X), (float)(cy + sp.Y), (float)(0.8 + 0.7 * sp.Alpha), _fillPaint);
                _fillPaint.BlendMode = SKBlendMode.Plus;
                continue;
            }
            _fillPaint.Color = SparkWhite.WithAlpha((byte)Math.Clamp(sa * 255, 0, 255));
            canvas.DrawCircle((float)(cx + sp.X), (float)(cy + sp.Y), (float)(1 + 2.2 * sp.Alpha), _fillPaint);
        }
        _fillPaint.BlendMode = SKBlendMode.SrcOver;
    }

    /// <summary>The final break: each triangle clips the picture as it was, thrown and tumbling, plus sparks.</summary>
    private void DrawDeckShards(SKCanvas canvas, FlashItem item, FlickerShatterState s, SKImage image)
    {
        var shardAlpha = FlickerShatter.ShardAlpha(s.ElapsedSec) * item.Opacity;
        float hw = (float)(s.W / 2), hh = (float)(s.H / 2);
        if (shardAlpha > 0)
        {
            var a = (byte)Math.Clamp(shardAlpha * 255, 0, 255);
            foreach (var sh in s.Shards)
            {
                float cx = (float)sh.Cx, cy = (float)sh.Cy;
                int saves = canvas.Save();
                canvas.Translate((float)(s.CenterX + sh.Cx + sh.X), (float)(s.CenterY + sh.Cy + sh.Y));
                canvas.RotateRadians((float)(sh.AngleRad + s.Rot0));
                _deckPath.Rewind();
                _deckPath.MoveTo((float)(sh.U0 * s.W) - hw - cx, (float)(sh.V0 * s.H) - hh - cy);
                _deckPath.LineTo((float)(sh.U1 * s.W) - hw - cx, (float)(sh.V1 * s.H) - hh - cy);
                _deckPath.LineTo((float)(sh.U2 * s.W) - hw - cx, (float)(sh.V2 * s.H) - hh - cy);
                _deckPath.Close();
                canvas.ClipPath(_deckPath, antialias: true);
                var dest = new SKRect(-hw - cx, -hh - cy, hw - cx, hh - cy);
                if (!item.HasGlow)
                {
                    _fillPaint.Color = new SKColor(0, 0, 0, a);
                    canvas.DrawRect(dest, _fillPaint);
                }
                _imagePaint.Color = new SKColor(255, 255, 255, a);
                canvas.DrawImage(image, dest, _imagePaint);
                canvas.RestoreToCount(saves);
            }
        }

        _fillPaint.MaskFilter = null;
        _fillPaint.BlendMode = SKBlendMode.Plus;
        foreach (var sp in s.Sparks)
        {
            var sa = sp.Alpha * item.Opacity;
            if (sa <= 0) continue;
            _fillPaint.Color = (sp.Pink ? SparkDeckPink : SparkWhite).WithAlpha((byte)Math.Clamp(sa * 255, 0, 255));
            canvas.DrawCircle((float)(s.CenterX + sp.X), (float)(s.CenterY + sp.Y), (float)(1 + 2.2 * sp.Alpha), _fillPaint);
        }
        _fillPaint.BlendMode = SKBlendMode.SrcOver;
    }

    #endregion

    /// <summary>Copy the motion's current axis-aligned bounds onto the item's bookkeeping rect.</summary>
    private static void SyncRect(FlashItem item, FlashMotionState m)
    {
        item.X = (float)m.X; item.Y = (float)m.Y; item.W = (float)m.W; item.H = (float)m.H;
    }

    private static SKRect UniformFit(int srcW, int srcH, SKRect dest)
    {
        if (srcW <= 0 || srcH <= 0 || dest.Width <= 0 || dest.Height <= 0) return dest;
        var ratio = Math.Min(dest.Width / srcW, dest.Height / srcH);
        float w = srcW * ratio, h = srcH * ratio;
        var x = dest.Left + (dest.Width - w) / 2f;
        var y = dest.Top + (dest.Height - h) / 2f;
        return new SKRect(x, y, x + w, y + h);
    }
}
