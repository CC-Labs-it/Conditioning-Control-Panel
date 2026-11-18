using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// Super Flicker Deck on this head (WPF FlashService.cs:2717-2965 + FlashLayer.cs:929): which
    /// flashes become cards, the press that flips or breaks one, the next picture, and one shared
    /// tick that steps every card. Panic and Stop reach it through <see cref="CloseAll"/>.
    /// The shared stage supplies the pool and receives card breaks.
    /// </summary>
    internal static partial class FlashOverlay
    {
        /// <summary>The Deck switch (tests swap it).</summary>
        internal static Func<bool> DeckOn = () => SuperAccess.IsOn(SuperEffect.FlickerDeck);

        /// <summary>The next face for a flip (tests swap it): a picture decoded at the card's width, and its path.</summary>
        internal static Func<FlashOverlayWindow, Task<(Bitmap Picture, string Path)?>> DeckPicture = LoadDeckPicture;

        /// <summary>Windows has no input shape: each tick tests the cursor against the card and toggles
        /// click-through, as the bubble field does (tests swap it). WPF's card window is the picture's
        /// own size (FlashService.cs:1770, the layer draws around it), so the grown margin never takes a click.</summary>
        internal static Func<FlashOverlayWindow, PixelRect[], bool> DeckInput = (w, r) =>
            X11Overlay.BackendOf(w.TryGetPlatformHandle()) == OverlayBackend.Win32 && X11Overlay.SetInputRects(w, r, 1);

        // WPF FlashService.cs:2720: phase variety for the idle sway. UI thread only.
        private static int _deckIndex;
        private static DispatcherTimer? _deckTimer;
        private static long _lastDeckTick;
        private static bool _deckSwitchHooked;
        private static SuperStage? _listeningStage;
        internal static Action<TimeSpan, Action> DeckKnockAfter = (delay, action) => DispatcherTimer.RunOnce(action, delay);

        private static void HookDeckStage()
        {
            var stage = Platform.Super.SuperStageHost.Stage;
            if (ReferenceEquals(_listeningStage, stage)) return;
            if (_listeningStage != null) _listeningStage.Blasted -= OnDeckBlast;
            _listeningStage = stage;
            stage.Blasted += OnDeckBlast;
        }

        // WPF FlashService.cs:2769-2782: the ring reaches each card after distance / 1.4 ms.
        private static void OnDeckBlast(Blast blast)
        {
            if (blast.ReachPx <= 0) return;
            var generation = Generation;
            foreach (var (window, rect) in Active.ToArray())
            {
                if (window.Deck is not { } deck) continue;
                double dx = rect.X + rect.Width / 2.0 - blast.X, dy = rect.Y + rect.Height / 2.0 - blast.Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance > blast.ReachPx + Math.Max(rect.Width, rect.Height) * 0.3) continue;
                var delay = TimeSpan.FromMilliseconds((int)Math.Clamp(distance / 1.4, 40, 900));
                DeckKnockAfter(delay, () =>
                {
                    if (Generation != generation || !ReferenceEquals(window.Deck, deck)
                        || !Active.Any(e => e.Window == window)) return;
                    DeckPress(window);
                });
            }
        }

        /// <summary>WPF AttachDeck (:2726): a clickable flash becomes a card when Super is on and nothing
        /// owned acts on the same click. Returns the margin (px) the window grew by, 0 when it stays plain.</summary>
        internal static int AttachDeck(FlashOverlayWindow w, PixelRect rect, double scaling, bool clickable)
        {
            var s = CoreSettings.Current;
            var owned = PrizeOwnership.OwnsFlashV2();
            if (!FlickerDeck.Applies(DeckOn(), usesLayer: true, clickable, previewV2: false, w.IsRemix, isNatasha: false,
                    dragOwnedOn: s.FlashDraggable && owned, shatterOwnedOn: s.FlashShatterEnabled && owned))
                return 0;
            var (min, max) = ConditioningControlPanel.SuperCore.DeckOptions.BreakRange(s.DeckFlips);
            // Room for the lift (x1.1 and 10% up), the wobble, the peek, the shadow and the puff.
            var pad = (int)Math.Ceiling(Math.Max(rect.Width, rect.Height) * 0.25) + 32;
            w.AttachDeck(FlickerDeck.Create(_deckIndex++ % 8, Rng, min, max), pad, scaling);
            var glow = w.GlowPadPx - pad;   // the picture and its glow keep the mouse, the grown margin does not
            w.DeckInputPx = new[] { new PixelRect(pad, pad, rect.Width + 2 * glow, rect.Height + 2 * glow) };
            w.PointerEntered += (_, _) => DeckHover(w, true);
            w.PointerExited += (_, _) => DeckHover(w, false);
            HookDeckSwitch();
            HookDeckStage();
            EnsureDeckTick();
            return pad;
        }

        private static void EnsureDeckTick()
        {
            if (_deckTimer != null) return;
            _lastDeckTick = Clock.GetTimestamp();
            _deckTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => DeckTick());
            _deckTimer.Start();
        }

        internal static bool DeckTickRunning => _deckTimer != null;

        private static void StopDeckTick()
        {
            _deckTimer?.Stop(); _deckTimer = null;
            if (_listeningStage != null) _listeningStage.Blasted -= OnDeckBlast;
            _listeningStage = null;
        }

        /// <summary>One frame for every card on real elapsed time (WPF FlashLayer tick :410); stops itself
        /// when no card is left. A card that asked to be raised goes on top, list and screen.</summary>
        internal static void DeckTick()
        {
            var now = Clock.GetTimestamp();
            var dt = Clock.GetElapsedTime(_lastDeckTick, now).TotalSeconds;
            _lastDeckTick = now;
            var level = FlashOverlayWindow.DeckLevel;
            var any = false;
            List<FlashOverlayWindow>? raise = null;
            foreach (var (w, rect) in Active.ToArray())
            {
                any = true;
                if (StepPool(w, rect, dt)) continue;
                if (w.DeckInputPx is { } input) DeckInput(w, input);   // still a card's size after the switch goes off
                if (w.Deck != null && w.StepDeck(dt, level)) (raise ??= new()).Add(w);
            }
            foreach (var w in Exiting)
                if (w.Deck != null) { any = true; w.StepDeck(dt, level); }
            if (raise != null) foreach (var w in raise) RaiseDeck(w);
            // WPF UpdateDeckHover runs every tick: a raise or a new flash moves no still pointer, so no crossing event.
            foreach (var (w, _) in Active) if (w.Deck != null && w.IsPointerOver) DeckHover(w, true);
            if (!any) StopDeckTick();
        }

        /// <summary>WPF FlashLayer.StepPool: still pictures follow the shared pool in desktop pixels.</summary>
        internal static bool StepPool(FlashOverlayWindow w, PixelRect rect, double dt)
        {
            if (dt <= 0 || w.IsFadingOut || w.Exit != null || w.Shatter != null || w.DeckShatter != null) return false;
            if (w.MotionStyle != FlashMotionStyle.Still) { w.ResetPool(); return false; }
            var stage = Platform.Super.SuperStageHost.Stage;
            var pull = CoreSettings.Current.WhirlpoolDragsPictures
                ? stage.Pull(rect.X + rect.Width / 2.0, rect.Y + rect.Height / 2.0, 1.4, ConditioningControlPanel.SuperCore.PullKind.Picture)
                : default;
            if (!pull.Inside && w.PoolVx == 0 && w.PoolVy == 0 && w.PoolScale >= 0.999) return false;
            w.PoolVx = (w.PoolVx + pull.Ax * dt) * Math.Max(0, 1 - 2.4 * dt);
            w.PoolVy = (w.PoolVy + pull.Ay * dt) * Math.Max(0, 1 - 2.4 * dt);
            if (!pull.Inside && Math.Abs(w.PoolVx) < 0.5 && Math.Abs(w.PoolVy) < 0.5) w.PoolVx = w.PoolVy = 0;
            w.PoolX += w.PoolVx * dt;
            w.PoolY += w.PoolVy * dt;
            int dx = (int)Math.Round(w.PoolX), dy = (int)Math.Round(w.PoolY);
            w.PoolX -= dx; w.PoolY -= dy;
            var moved = new PixelRect(rect.X + dx, rect.Y + dy, rect.Width, rect.Height);
            var i = Active.FindIndex(e => e.Window == w);
            if (i < 0) return false;
            Active[i] = (w, moved);
            w.Position = new PixelPoint(w.Position.X + dx, w.Position.Y + dy);
            w.SetPoolScale(w.PoolScale + ((pull.Inside ? pull.Scale : 1) - w.PoolScale) * (1 - Math.Exp(-8 * dt)));
            if (!pull.Eaten) return false;
            // WPF PoolAte (FlashService.cs:2817-2825): no click reward, haptics or hydra, then the normal dismissal.
            Active.RemoveAt(i);
            if (w.Deck != null) w.DeckBreak = true;
            Dismiss(w, moved, CoreSettings.Current);
            return true;
        }

        /// <summary>WPF RaiseDeckWindow (:2938) + FlashLayer.BringToFront: in the air, it is the top card.</summary>
        private static void RaiseDeck(FlashOverlayWindow w)
        {
            var i = Active.FindIndex(e => e.Window == w);
            if (i < 0) return;
            var entry = Active[i];
            Active.RemoveAt(i);
            Active.Add(entry);
            X11Overlay.Raise(w);
        }

        /// <summary>WPF FlashLayer.UpdateDeckHover (:839): only a buried card peeks, away from the middle
        /// of the cluster it sits in. Later in <see cref="Active"/> is higher, as WPF's draw order.</summary>
        private static void DeckHover(FlashOverlayWindow w, bool over)
        {
            if (w.Deck is not { } deck) return;
            var at = Active.FindIndex(e => e.Window == w);
            if (!over || at < 0) { deck.Hover = false; return; }
            var r = Active[at].Rect;
            double sx = 0, sy = 0;
            var n = 0;
            var buried = false;
            for (var j = 0; j < Active.Count; j++)
            {
                var o = Active[j];
                if (o.Window.Exit != null || o.Window.DeckShatter != null || o.Window.Shatter != null) continue;
                if (j != at && !o.Rect.Intersects(r)) continue;
                if (j > at) buried = true;
                sx += o.Rect.X + o.Rect.Width / 2.0;
                sy += o.Rect.Y + o.Rect.Height / 2.0;
                n++;
            }
            deck.Hover = buried && n > 1;
            if (deck.Hover)
                (w.DeckPeekDx, w.DeckPeekDy) = FlickerDeck.PeekDirection(r.X + r.Width / 2.0, r.Y + r.Height / 2.0, sx / n, sy / n);
        }

        /// <summary>WPF DeckPress (:2829): a left click on a card flips it, breaks it, or does nothing while it is busy.</summary>
        internal static void DeckPress(FlashOverlayWindow w)
        {
            if (w.Deck is not { } deck) { Pop(w); return; }
            if (w.IsFadingOut || w.Exit != null || w.DeckShatter != null) return;
            switch (FlickerDeck.Press(deck, FlashOverlayWindow.DeckLevel))
            {
                case FlickerPress.Shatter:
                    w.DeckBreak = true;
                    Pop(w);
                    break;
                case FlickerPress.Flip:
                    w.BoostLifetime((int)((FlickerDeck.LiftSpanSec + FlickerDeck.MaxHoldSec) * 1000) + 250);
                    _ = FetchDeckPicture(w, deck);
                    break;
            }
        }

        /// <summary>WPF LoadDeckPictureAsync (:2853): the frames wait on the card until its flip reaches the
        /// edge. No picture: the flip opens on the old face at once.</summary>
        private static async Task FetchDeckPicture(FlashOverlayWindow w, FlickerDeckState deck)
        {
            (Bitmap Picture, string Path)? next = null;
            try { next = await DeckPicture(w); }
            catch (Exception ex) { Log.Debug("Flicker Deck picture failed: {E}", ex.Message); }
            if (next is not { } got)
            {
                if (ReferenceEquals(w.Deck, deck)) deck.NoPicture = true;
                return;
            }
            // WPF ApplyDeckSwap (FlashService.cs:2918-2930): an animated face plays at the GIF speed slider.
            GifFrames.TryGetValue(got.Picture, out var gif);
            if (!w.OfferDeckPicture(deck, got.Picture, got.Path, gif?.Frames,
                    gif is null ? default : GifFrameDelay(gif.Delay, CoreSettings.Current.FlashGifSpeedMultiplier, gentle: false)))
                Drop(got.Picture);
            else GifFrames.Remove(got.Picture);
        }

        /// <summary>Up to four draws from the flash pool, preferring a picture not up anywhere, then
        /// anything but the card's own face (WPF :2866-2880), decoded off the UI thread at the card's width.</summary>
        internal static Task<(Bitmap Picture, string Path)?> LoadDeckPicture(FlashOverlayWindow w)
        {
            // shortcut: compares raw paths, not WPF's SourceIdentityLocked pack identity; a pack picture may repeat on a small pool.
            var up = new HashSet<string>(Active.Select(e => e.Window.SourcePath).OfType<string>(), StringComparer.OrdinalIgnoreCase);
            var own = w.SourcePath;
            var rect = Active.Find(e => e.Window == w).Rect;
            var s = CoreSettings.Current;
            return Task.Run<(Bitmap Picture, string Path)?>(() =>
            {
                var files = FlashFiles(s);
                var draws = new List<string>();
                for (var i = 0; i < 4 && NextFlashPath(files) is { } p; i++) draws.Add(p);
                var order = draws.Where(p => !up.Contains(p))
                    .Concat(draws.Where(p => up.Contains(p) && !string.Equals(p, own, StringComparison.OrdinalIgnoreCase)));
                foreach (var path in order)
                {
                    try
                    {
                        // WPF LoadImageAsync: a GIF or WebP keeps its frames (decoded as a flash is).
                        using (var codec = SkiaSharp.SKCodec.Create(path))
                            if (codec is { FrameCount: > 1 } && DecodeAnimated(path, rect) is { } first) return (first, path);
                        using var stream = File.OpenRead(path);
                        return (Bitmap.DecodeToWidth(stream, Math.Max(1, rect.Width)), path);
                    }
                    catch (Exception ex) { Log.Debug("Flicker Deck: could not decode {Path}: {E}", path, ex.Message); }
                }
                return null;
            });
        }

        /// <summary>WPF OnSuperChanged (:2948): the switch went off, every card is a plain flash again.</summary>
        private static void HookDeckSwitch()
        {
            if (_deckSwitchHooked) return;
            _deckSwitchHooked = true;
            SuperAccess.Changed += effect =>
            {
                if (effect != SuperEffect.FlickerDeck) return;
                Dispatcher.UIThread.Post(() =>
                {
                    if (DeckOn()) return;
                    foreach (var (w, _) in Active) w.ClearDeck();
                });
            };
        }
    }
}
