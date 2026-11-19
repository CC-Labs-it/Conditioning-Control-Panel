using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services;

/// <summary>
/// Super Froth glue (the star on the dashboard Bubbles; the enum name is still InnerBloom).
/// Ambient bubbles on the compositor path merge when they touch, turn ripe when big, take their
/// neighbours with them when they pop, are pulled in by the Whirlpool and pushed by the cursor.
/// Every rule and number is <see cref="FrothRules"/> (CCP.Core/Super); this file only walks the
/// field and talks to the shared stage.
///
/// UNITS. The stage (<see cref="SuperStageHost.Stage"/>) speaks DEVICE PIXELS of the virtual
/// desktop. A bubble lives in DIPs of its own monitor with a per-bubble dpi scale. Everything
/// that crosses that line is converted here, at the call: px = dip * bubble.DpiScale.
///
/// PAY (owner rule, 2026-11-15): a merged bubble pays exactly the sum of its parts, never a
/// bonus. The carrier pays itself through the ordinary pop path; <see cref="PayFrothParts"/>
/// then credits each bubble it took in once. Nothing else here pays.
/// </summary>
public partial class BubbleService
{
    private bool _frothHooked;
    private bool _frothStar;      // the last answer of SuperAccess.IsOn, refreshed on Changed and once a second
    private bool _frothWasOn;
    private long _frothLastMs, _frothAskedMs = -100000;
    private readonly List<Bubble> _frothBuf = new();
    /// <summary>True while a step walks <see cref="_frothBuf"/>. A pop inside the walk can pump the
    /// dispatcher (a render tick lands mid-pop) and start a second step, which cleared the buffer under
    /// the first one: "Collection was modified" (owner crash, 2026-11-17). The inner step stands down.</summary>
    private bool _frothStepping;

    /// <summary>The star is on AND this is the ambient field on the compositor path. Never during
    /// a chaos run, never on the legacy per-window path.</summary>
    private bool FrothOn => _frothStar && _ambientHost && !_chaosActive && Bubble.UseCompositor;

    private void HookFroth()
    {
        if (_frothHooked) return;
        _frothHooked = true;
        SuperAccess.Changed += OnFrothStarChanged;
        SuperStageHost.Stage.Blasted += OnStageBlasted;
        SuperStageHost.Stage.Swept += OnStageSwept;
    }

    private void UnhookFroth()
    {
        if (!_frothHooked) return;
        _frothHooked = false;
        SuperAccess.Changed -= OnFrothStarChanged;
        SuperStageHost.Stage.Blasted -= OnStageBlasted;
        SuperStageHost.Stage.Swept -= OnStageSwept;
    }

    /// <summary>The switch flipped, the tier lapsed or the Bubbles went off: re-ask, and if Froth
    /// is over put the field back the way the base keeps it, at once.</summary>
    private void OnFrothStarChanged(SuperEffect effect)
    {
        if (effect != SuperEffect.InnerBloom) return;
        DispatcherHelper.RunOnUI(() =>
        {
            _frothStar = SuperAccess.IsOn(SuperEffect.InnerBloom);
            if (!FrothOn && _frothWasOn) { _frothWasOn = false; EndFroth(); }
        });
    }

    /// <summary>Froth is over while bubbles are still up: a merged bubble leaves quietly (like one
    /// that floated off: no pop, no pay), every other bubble drops what Froth did to it.</summary>
    private void EndFroth()
    {
        foreach (var b in _bubbles.ToArray())
        {
            try
            {
                if (b.FrothPartCount > 1) b.ForceDestroy();
                else b.FrothReset();
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }
    }

    /// <summary>Glass radius of a fresh ambient bubble in DIPs: the middle of the spawn band at
    /// the player's own size setting. "Ripe" and "chains" are multiples of this.</summary>
    private static double FrothFreshRadiusDip()
        => BubbleSizing.Scale((BubbleSizing.BaseMinDip + BubbleSizing.BaseMaxDip) / 2,
               App.Settings?.Current?.BubblesSize ?? BubbleSizing.UserPercentDefault,
               App.Mods?.ActiveMod?.Manifest?.BubbleScale) * FrothRules.GlassOfSprite;

    /// <summary>One Froth step, from the shared bubble tick before the bubbles animate. With the
    /// star off this is one comparison and the base field runs exactly as it always has.</summary>
    private void StepFroth()
    {
        HookFroth();
        long now = _stepClock.ElapsedMilliseconds;
        if (now - _frothAskedMs >= 1000)
        {
            _frothAskedMs = now;
            _frothStar = SuperAccess.IsOn(SuperEffect.InnerBloom);
        }
        if (!FrothOn)
        {
            if (_frothWasOn) { _frothWasOn = false; EndFroth(); }
            return;
        }
        if (!_frothWasOn) { _frothWasOn = true; _frothLastMs = now; return; }
        double dt = Math.Clamp((now - _frothLastMs) / 1000.0, 0, 0.1);
        _frothLastMs = now;
        if (dt <= 0 || _bubbles.Count == 0) return;

        var stage = SuperStageHost.Stage;
        bool poolLive = stage.PoolLive && stage.Drags(PullKind.Bubble);
        bool gentle = Safety.GentleVisuals.On;
        double freshDip = FrothFreshRadiusDip();
        // The option box, read live (FrothOptions): the size cap and the cursor push. Neither touches pay.
        var fs = App.Settings?.Current;
        double maxGrow = FrothOptions.ClampMaxSize(fs?.FrothMaxSize ?? FrothOptions.MaxSizeDefault);
        double pushGain = FrothOptions.PushFactor(fs?.FrothCursorPush ?? FrothOptions.PushDefault);
        // The nudge reads the cursor itself (device px, same as the stage): SuperStageHost.Cursor
        // only moves while Steam or the Whirlpool steps the stage. Read only, nothing is hooked.
        bool haveCursor = TryCursorPx(out var cursorPx);

        if (_frothStepping) return;
        _frothStepping = true;
        try
        {
        _frothBuf.Clear();
        foreach (var b in _bubbles) if (b.IsFrothLive) _frothBuf.Add(b);

        foreach (var b in _frothBuf)
        {
            if (!b.IsFrothLive) continue;
            // A ring reached it a moment ago: it goes off when the ring gets there.
            if (b.FrothFuse >= 0)
            {
                b.FrothFuse -= dt;
                if (b.FrothFuse <= 0) { b.FrothFuse = -1; b.Pop(); }
                continue;
            }

            double dpi = b.DpiScale, rDip = b.FrothRadiusDip;
            var cPx = b.CenterPx;                                    // DIP -> device px
            var pull = stage.Pull(cPx.X, cPx.Y, FrothRules.Mass(rDip, freshDip), PullKind.Bubble);
            if (pull.Eaten) { b.Pop(); continue; }                   // owner rule: the pool's bubble pays normally
            double ax = pull.Ax / dpi, ay = pull.Ay / dpi;           // px/s2 -> DIP/s2

            // The cursor herds bubbles while the pool is not using it.
            if (!poolLive && haveCursor && pushGain > 0)
            {
                double dx = (cPx.X - cursorPx.X) / dpi, dy = (cPx.Y - cursorPx.Y) / dpi;   // px -> DIP
                double dd = Math.Sqrt(dx * dx + dy * dy);
                double push = FrothRules.NudgeAccel(dd - rDip, rDip, freshDip) * pushGain;
                if (push > 0 && dd > 1e-6) { ax += dx / dd * push; ay += dy / dd * push; }
            }
            b.FrothStep(dt, ax, ay, pull.Scale, pull.Inside, freshDip, gentle);
        }

        // Merge: two that touch become one. The smaller one is taken away without a pop (no sound,
        // nothing paid yet); the carrier remembers it and pays for it when it pops.
        for (int i = 0; i < _frothBuf.Count; i++)
        {
            var a = _frothBuf[i];
            if (!a.IsFrothLive || a.FrothFuse >= 0) continue;
            for (int j = i + 1; j < _frothBuf.Count; j++)
            {
                var b = _frothBuf[j];
                if (!b.IsFrothLive || b.FrothFuse >= 0) continue;
                if (a.DpiScale != b.DpiScale) continue;              // same monitor scale: one DIP space
                var ca = a.CenterDip; var cb = b.CenterDip;
                if (!FrothRules.Touching(ca.X, ca.Y, a.FrothRadiusDip, cb.X, cb.Y, b.FrothRadiusDip)) continue;
                bool keepA = a.FrothRadiusDip >= b.FrothRadiusDip;
                var keep = keepA ? a : b; var gone = keepA ? b : a;
                try
                {
                    keep.FrothAbsorb(gone, freshDip, maxGrow);
                    gone.ForceDestroy();                             // no pop: no sound, no award
                }
                catch (Exception ex) { App.Logger?.Debug("Froth merge: {E}", ex.Message); }
                if (!keepA) break;                                   // a is gone
            }
        }
        _frothBuf.Clear();
        }
        finally { _frothStepping = false; }
    }

    /// <summary>Every ambient pop comes through here after the ordinary award: pay the parts a
    /// merged bubble took in, then tell the stage it went off (the hole in the fog, the ring).</summary>
    private void OnFrothPopped(Bubble b)
    {
        PayFrothParts(b);
        if (!FrothOn || !b.IsFrothKind) return;
        try
        {
            var c = b.CenterPx;                                       // DIP -> device px
            double rPx = b.FrothRadiusPx, freshPx = FrothFreshRadiusDip() * b.DpiScale;
            double reach = FrothRules.Chains(rPx, freshPx) ? FrothRules.Reach(rPx) : 0;   // a small one reaches nothing
            reach *= FrothOptions.ReachFactor(App.Settings?.Current?.FrothPopReach ?? FrothOptions.ReachDefault);
            var kind = FrothRules.Ripe(rPx, freshPx) ? BlastKind.RipePop : BlastKind.BubblePop;
            SuperStageHost.Stage.Blast(new Blast(c.X, c.Y, reach, rPx * FrothRules.HoleRadii, kind));
        }
        catch (Exception ex) { App.Logger?.Debug("Froth blast: {E}", ex.Message); }
    }

    /// <summary>
    /// THE SUM. The carrier has just been paid as one ordinary pop (AwardAmbientPop). Each bubble
    /// it took in is now credited once, exactly as its own pop would have been: its own lucky
    /// roll, 5 XP from the same daily bucket, one pop for the leash and companion counters
    /// (OnBubblePopped) and one for the achievements. N parts count as N pops; nothing is added.
    /// Only the sound, the haptic tap and the burst stay single: there is one pop to hear.
    /// A part that carried a picture also fires its own flash, up to the pictures shown inside.
    /// </summary>
    private void PayFrothParts(Bubble b)
    {
        var parts = b.FrothParts;
        if (parts == null) return;
        int flashes = b.Spec?.Payload != null ? 1 : 0;
        foreach (var part in parts)
        {
            CreditFrothPart(part.XpMultiplier);
            if (part.Payload == null || flashes >= FrothRules.MaxPictures) continue;
            flashes++;
            try
            {
                if (part.Payload is FlashPayload flash && part.FacePath != null)
                {
                    flash.ImagePath = part.FacePath;
                    flash.OriginPx = b.CenterPx;
                    flash.FaceDiameterPx = b.FrothRadiusPx;
                }
                FireAmbientPayload(part.Payload);
            }
            catch (Exception ex) { App.Logger?.Debug("Froth part payload: {E}", ex.Message); }
        }
    }

    /// <summary>What one absorbed bubble's pop pays and counts. Keep in step with the paying and
    /// counting lines of <see cref="AwardAmbientPop"/>; the test FrothWiringTests pins both.</summary>
    private void CreditFrothPart(int xpMultiplier)
    {
        var multiplier = App.SkillTree?.RollLuckyBubble() ?? 1;
        var paidXp = TakeFromAmbientBubbleBucket(5 * multiplier * xpMultiplier);
        if (paidXp > 0) App.Progression?.AddXP(paidXp, XPSource.Bubble);
        AmbientXpBudgetChanged?.Invoke();
        OnBubblePopped?.Invoke();
        App.Achievements?.TrackBubblePopped();
    }

    /// <summary>Something went off somewhere (a bubble, a broken picture, a gold corner). Every
    /// Froth bubble inside its reach goes off when the ring gets there, once. Device px in, and
    /// the bubbles answer in device px.</summary>
    private void OnStageBlasted(Blast blast)
    {
        if (blast.ReachPx <= 0 || !FrothOn) return;
        try
        {
            double freshDip = FrothFreshRadiusDip();
            foreach (var b in _bubbles)
            {
                if (!b.IsFrothLive || b.FrothFuse >= 0) continue;   // the one that caused it is already popping
                var c = b.CenterPx;
                double dx = c.X - blast.X, dy = c.Y - blast.Y, d = Math.Sqrt(dx * dx + dy * dy);
                if (FrothRules.InReach(d, blast.ReachPx, b.FrothRadiusPx))
                    b.FrothFuse = FrothRules.ChainDelay(d, freshDip * b.DpiScale);
            }
        }
        catch (Exception ex) { App.Logger?.Debug("Froth chain: {E}", ex.Message); }
    }

    /// <summary>The bouncing word covers this box (device px): the Froth bubbles under it pop.</summary>
    private void OnStageSwept(double left, double top, double right, double bottom)
    {
        if (!FrothOn) return;
        try
        {
            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                if (i >= _bubbles.Count) continue;
                var b = _bubbles[i];
                if (!b.IsFrothLive) continue;
                var c = b.CenterPx;
                if (FrothRules.UnderBox(c.X, c.Y, b.FrothRadiusPx, left, top, right, bottom)) b.Pop();
            }
        }
        catch (Exception ex) { App.Logger?.Debug("Froth sweep: {E}", ex.Message); }
    }
}

/// <summary>Super Froth state on one bubble. Everything here is neutral (x 1.0, + 0) until
/// BubbleService.StepFroth touches the bubble, so the base bubble is unchanged with the star off.</summary>
internal partial class Bubble
{
    /// <summary>One bubble a merged bubble took in: what its own pop would have fired and paid.</summary>
    internal sealed record FrothPart(EffectPayload? Payload, string? FacePath, int XpMultiplier);

    private bool _frothTouched;
    private double _frothGrow = 1.0;    // size multiple the merges ask for
    private double _frothShown = 1.0;   // the size multiple drawn and hit-tested (eases to _frothGrow)
    private double _frothPool = 1.0;    // the Whirlpool draws it this small
    private double _frothDrift = 1.0;   // share of its own drift left (the pool damps it)
    private double _frothVx, _frothVy;  // pushed velocity, DIP/s
    private double _frothWob, _frothT, _frothRim;
    private double _frothEdgePad;       // how far the top and bottom exits were moved out for its size
    private bool _frothCredit;          // it carries a Scrolller picture taken in by a merge
    private List<FrothPart>? _frothParts;

    /// <summary>Seconds until a ring that reached it pops it; below 0 = none.</summary>
    internal double FrothFuse = -1;

    /// <summary>Froth only touches the ambient field on the compositor layer: plain bubbles and
    /// the picture ("flash") trigger bubbles. Never a chaos bubble, a magnet, the red one, another
    /// trigger kind (a chained pop must not open a video) or anything of the parked Inner Bloom.</summary>
    internal bool IsFrothKind => _useLayer && !_isMagnetBubble && !_isNatasha && _bloom == null && BloomRoot == 0
        && (_spec == null || (_isAmbientTrigger && _spec.VariantId == "flash"));

    /// <summary>A Froth bubble that can still merge, be pushed and be popped by a ring.</summary>
    internal bool IsFrothLive => IsFrothKind && _isAlive && !_isDestroyed && !_isPopping
        && !_claimedByAvatar && !_isResisting;

    /// <summary>Glass radius in DIPs (the sprite has a soft margin), merges included.</summary>
    internal double FrothRadiusDip => _size * FrothRules.GlassOfSprite * _frothGrow;
    /// <summary>The same in device px.</summary>
    internal double FrothRadiusPx => FrothRadiusDip * _dpiScale;

    internal IReadOnlyList<FrothPart>? FrothParts => _frothParts;
    /// <summary>How many ordinary pops this bubble stands for (1 unless it merged).</summary>
    internal int FrothPartCount => 1 + (_frothParts?.Count ?? 0);

    /// <summary>Move the bubble and everything its motion row is anchored to (DIPs).</summary>
    private void FrothShift(double dx, double dy)
    {
        _posX += dx; _startX += dx; _spiralCx += dx;
        _posY += dy; _startY += dy; _spiralCy += dy;
    }

    /// <summary>One step: the pool's pull and the cursor's push (DIP/s2), the pool's shrink, the
    /// merge wobble, the size easing and the gold rim.</summary>
    internal void FrothStep(double dt, double axDip, double ayDip, double poolScale, bool insidePool,
                            double freshRadiusDip, bool gentle)
    {
        _frothTouched = true;
        _frothT += dt;
        _frothVx += axDip * dt;
        _frothVy += ayDip * dt;
        double drag = Math.Max(0, 1 - dt * (insidePool ? FrothRules.PoolDrag : FrothRules.Drag));
        _frothVx *= drag;
        _frothVy *= drag;
        // Inside the pool its own drift dies away too, so it spirals in instead of orbiting.
        _frothDrift = insidePool
            ? _frothDrift * Math.Max(0, 1 - dt * FrothRules.PoolDrag)
            : Math.Min(1.0, _frothDrift + dt * FrothRules.DriftBack);

        double dx = _frothVx * dt, dy = _frothVy * dt;
        // Never pushed off the side of its own screen (the base rows only leave by their own edge).
        if ((_posX + dx < _screenLeft && dx < 0) || (_posX + dx > _screenRight && dx > 0)) { dx = 0; _frothVx = 0; }
        if (dx != 0 || dy != 0) FrothShift(dx, dy);

        _frothPool = Math.Clamp(poolScale, 0.05, 1.0);
        _frothShown = FrothRules.EaseSize(_frothShown, _frothGrow, dt, gentle);
        if (Math.Abs(_frothGrow - _frothShown) < 0.002) _frothShown = _frothGrow;
        if (_frothWob > 0) _frothWob = Math.Max(0, _frothWob - dt / FrothRules.WobbleSeconds);
        // The gold rim eases in and out over 400 ms: it never snaps on.
        double rim = FrothRules.Ripe(FrothRadiusDip, freshRadiusDip) ? 1 : 0;
        _frothRim += Math.Clamp(rim - _frothRim, -dt / 0.4, dt / 0.4);
    }

    /// <summary>Take <paramref name="other"/> in: areas add, the centre is the weighted middle,
    /// its pictures float inside, and its pay is remembered for the pop. The caller removes it.
    /// Both bubbles are on the same dpi scale, so one DIP space.</summary>
    internal void FrothAbsorb(Bubble other, double freshRadiusDip, double maxGrow = FrothRules.MaxGrow)
    {
        _frothTouched = true;
        double ar = FrothRadiusDip, br = other.FrothRadiusDip;
        var a = CenterDip; var b = other.CenterDip;
        var c = FrothRules.MergedCentre(a.X, a.Y, ar, b.X, b.Y, br);
        FrothShift(c.X - a.X, c.Y - a.Y);
        double wa = ar * ar, wb = br * br, w = wa + wb;
        if (w > 0)
        {
            _frothVx = (_frothVx * wa + other._frothVx * wb) / w;
            _frothVy = (_frothVy * wa + other._frothVy * wb) / w;
        }
        _frothGrow = Math.Max(_frothGrow, FrothRules.MergedRadius(ar, br, freshRadiusDip, maxGrow) / (_size * FrothRules.GlassOfSprite));
        _frothWob = 1;
        // A grown bubble leaves by the top or bottom only once the whole of it is past the edge.
        double pad = _size * (_frothGrow - 1) / 2.0;
        _screenTop -= pad - _frothEdgePad;
        _screenBottom += pad - _frothEdgePad;
        _frothEdgePad = pad;

        // The sum: one entry per bubble taken in, the other's own parts included.
        _frothParts ??= new List<FrothPart>();
        _frothParts.Add(new FrothPart(other._spec?.Payload, other._flashFacePath, other.AmbientXpMultiplier));
        if (other._frothParts != null) _frothParts.AddRange(other._frothParts);

        // Its pictures move in: its own face as a still, then what it was carrying.
        var item = _layerItem;
        if (item != null)
        {
            int room = FrothRules.MaxPictures - (item.HasFace ? 1 : 0) - (item.FrothPictures?.Count ?? 0);
            if (room > 0 && other._bubbleFaceImg?.Source is BitmapSource face)
            {
                try
                {
                    (item.FrothPictures ??= new List<SKImage>()).Add(SkiaWpfInterop.ToSKImage(face));
                    room--;
                }
                catch (Exception ex) { Diag.Swallowed(ex); }
            }
            var theirs = other._layerItem?.FrothPictures;
            if (theirs != null)
            {
                // Ownership moves with the picture; what does not fit stays with the other bubble
                // and is freed when its item leaves the layer.
                while (room > 0 && theirs.Count > 0)
                {
                    (item.FrothPictures ??= new List<SKImage>()).Add(theirs[0]);
                    theirs.RemoveAt(0);
                    room--;
                }
            }
        }
        // "via Scrolller" stays up while a Scrolller picture is inside (released in Destroy).
        if (other._frothCredit || Video.ScrolllerCredit.IsScrolllerMedia(other._flashFacePath))
        {
            _frothCredit = true;
            Controls.ScrolllerCreditChip.Hold(this);
        }
    }

    /// <summary>The star went off: back to a plain bubble, where it is.</summary>
    internal void FrothReset()
    {
        if (!_frothTouched) return;
        _frothTouched = false;
        _frothGrow = _frothShown = _frothPool = _frothDrift = 1.0;
        _frothVx = _frothVy = _frothWob = _frothRim = 0;
        FrothFuse = -1;
        var it = _layerItem;
        if (it == null) return;
        it.FrothScale = 1f;
        it.FrothSquash = 1f;
        it.FrothRim = 0f;
        s_layer?.MarkDirty();
    }

    /// <summary>Copy this step's Froth look onto the draw item (called from SyncLayerItem).</summary>
    private void SyncFrothItem(Compositor.BubbleLayer.BubbleItem it)
    {
        it.FrothScale = (float)(_frothShown * _frothPool);
        it.FrothSquash = (float)FrothRules.Squash(_frothWob, _frothT, Safety.GentleVisuals.On);
        it.FrothRim = (float)_frothRim;
        it.FrothTime = _frothT;
    }
}
