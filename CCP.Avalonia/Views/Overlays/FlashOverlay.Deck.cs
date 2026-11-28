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
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// Super Flicker Deck on this head (WPF FlashService.cs:2717-2965 + FlashLayer.cs:929): which
    /// flashes become cards, the press that flips or breaks one, the next picture, and one shared
    /// tick that steps every card. Panic and Stop reach it through <see cref="CloseAll"/>. The next
    /// picture is got ready while a card rests (WPF dfc1565889, Core <see cref="ConditioningControlPanel.Services.DeckPrefetch"/>); the
    /// shared stage (break blasts, knocks, the Whirlpool) is FlashOverlay.Stage.cs.
    /// </summary>
    internal static partial class FlashOverlay
    {
        /// <summary>The Deck switch (tests swap it).</summary>
        internal static Func<bool> DeckOn = () => SuperAccess.IsOn(SuperEffect.FlickerDeck);

        /// <summary>The next face for a card (tests swap it): a picture decoded at the card's width, and its
        /// source path; the flag asks for local pictures only (a card's first prefetch).</summary>
        internal static Func<FlashOverlayWindow, bool, Task<(Bitmap Picture, string Path)?>> DeckPicture = LoadDeckPicture;

        /// <summary>Decodes a face kept by its path alone (an animated one) on the click (tests swap it).</summary>
        internal static Func<string, PixelRect, Task<Bitmap?>> DeckDecode = (path, rect) => Task.Run(() => DeckDecodeNow(path, rect));

        internal static Bitmap? DeckDecodeNow(string path, PixelRect rect)
        {
            try { return DecodeDeckFace(path, rect); }
            catch (Exception ex) { Log.Debug("Flicker Deck: could not decode {Path}: {E}", path, ex.Message); return null; }
        }

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

        /// <summary>WPF AttachDeck (:2726): a clickable flash becomes a card when Super is on and nothing
        /// owned acts on the same click. Returns the margin (px) the window grew by, 0 when it stays plain.</summary>
        internal static int AttachDeck(FlashOverlayWindow w, PixelRect rect, double scaling, bool clickable)
        {
            var s = CoreSettings.Current;
            var owned = PrizeOwnership.OwnsFlashV2();
            if (!FlickerDeck.Applies(DeckOn(), usesLayer: true, clickable, previewV2: false, w.IsRemix, isNatasha: false,
                    dragOwnedOn: s.FlashDraggable && owned, shatterOwnedOn: s.FlashShatterEnabled && owned))
                return 0;
            // Fewest / Most flips (ccp-bugs #1636): unset ends follow the older single slider (WPF :2744).
            var (min, max) = ConditioningControlPanel.SuperCore.DeckOptions.BreakRange(s.DeckFlips, s.DeckFlipsFewest, s.DeckFlipsMost);
            // Room for the lift (x1.1 and 10% up), the wobble, the peek, the shadow and the puff.
            var pad = (int)Math.Ceiling(Math.Max(rect.Width, rect.Height) * 0.25) + 32;
            w.AttachDeck(FlickerDeck.Create(_deckIndex++ % 8, Rng, min, max), pad, scaling);
            var glow = w.GlowPadPx - pad;   // the picture and its glow keep the mouse, the grown margin does not
            w.DeckInputPx = new[] { new PixelRect(pad, pad, rect.Width + 2 * glow, rect.Height + 2 * glow) };
            w.DeckRect = rect;
            w.PointerEntered += (_, _) => DeckHover(w, true);
            w.PointerExited += (_, _) => DeckHover(w, false);
            // WPF ApplyDeckSwap (:3007): a new face deserves a full look, and the one after it is got ready.
            w.DeckSwapped = () =>
            {
                w.BoostLifetime((int)w.Lifetime.TotalMilliseconds);
                _ = PrefetchDeck(w, localOnly: false);
            };
            HookDeckSwitch();
            HookDeckStage();
            if (_deckTimer == null)
            {
                _lastDeckTick = Clock.GetTimestamp();
                _deckTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => DeckTick());
                _deckTimer.Start();
            }
            // The first flip's picture, now: local only, so a card nobody clicks never spends the Scrolller share.
            _ = PrefetchDeck(w, localOnly: true);
            return pad;
        }

        internal static bool DeckTickRunning => _deckTimer != null;

        private static void StopDeckTick() { _deckTimer?.Stop(); _deckTimer = null; }

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
            foreach (var (w, _) in Active)
            {
                if (w.DeckInputPx is { } input) { any = true; DeckInput(w, input); }   // still a card's size after the switch goes off
                if (w.Deck != null && w.StepDeck(dt, level)) (raise ??= new()).Add(w);
            }
            foreach (var w in Exiting)
                if (w.Deck != null) { any = true; w.StepDeck(dt, level); }
            if (raise != null) foreach (var w in raise) RaiseDeck(w);
            // WPF UpdateDeckHover runs every tick: a raise or a new flash moves no still pointer, so no crossing event.
            foreach (var (w, _) in Active) if (w.Deck != null && w.IsPointerOver) DeckHover(w, true);
            if (!any) StopDeckTick();
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
                    // WPF :2853: keep the card alive through its lift and the longest edge-on wait.
                    w.BoostLifetime((int)((FlickerDeck.LiftSpanSec + FlickerDeck.MaxHoldSec) * 1000) + 250);
                    _ = FetchDeckPicture(w, deck);
                    break;
            }
        }

        /// <summary>WPF LoadDeckPictureAsync (:2959): the picture the card got ready while it rested, else a
        /// fresh draw; the frames wait on the card until its flip reaches the edge. No picture: the flip opens
        /// on the old face at once. One that lands after the flip gave up is kept for the next click.</summary>
        private static async Task FetchDeckPicture(FlashOverlayWindow w, FlickerDeckState deck)
        {
            (Bitmap Picture, string Path)? next = null;
            try { next = await TakeDeckReady(w) ?? await DeckPicture(w, false); }
            catch (Exception ex) { Log.Debug("Flicker Deck picture failed: {E}", ex.Message); }
            if (next is not { } got)
            {
                if (ReferenceEquals(w.Deck, deck)) deck.NoPicture = true;
                return;
            }
            // WPF ApplyDeckSwap (FlashService.cs:2918-2930): an animated face plays at the GIF speed slider.
            GifFrames.TryGetValue(got.Picture, out var gif);
            if (w.OfferDeckPicture(deck, got.Picture, got.Path, gif?.Frames,
                    gif is null ? default : GifFrameDelay(gif.Delay, CoreSettings.Current.FlashGifSpeedMultiplier, gentle: false)))
                GifFrames.Remove(got.Picture);
            else if (ReferenceEquals(w.Deck, deck) && !w.IsClosed && !w.IsFadingOut && !w.HasDeckReady && !w.DeckPrefetching)
                StashDeck(w, got);   // too late for this flip: kept for the next one
            else Drop(got.Picture);
        }

        /// <summary>WPF PrefetchDeckPictureAsync (:2900): get the card's NEXT picture ready while it rests, one
        /// at a time per card. UI thread in, UI thread out.</summary>
        internal static async Task PrefetchDeck(FlashOverlayWindow w, bool localOnly)
        {
            if (w.DeckPrefetching || w.HasDeckReady || w.Deck is not { } deck) return;
            w.DeckPrefetching = true;
            try
            {
                if (await DeckPicture(w, localOnly) is not { } got) return;
                if (ReferenceEquals(w.Deck, deck) && !w.IsClosed && !w.IsFadingOut && !w.HasDeckReady) StashDeck(w, got);
                else Drop(got.Picture);
            }
            catch (Exception ex) { Log.Debug("Flicker Deck prefetch failed: {E}", ex.Message); }
            finally { w.DeckPrefetching = false; }
        }

        /// <summary>WPF StashDeckPicture (:2921): a still is kept decoded, an animated face by its path only,
        /// so up to 30 MB of frames never wait in memory (<see cref="ConditioningControlPanel.Services.DeckPrefetch.KeepDecoded"/>).</summary>
        private static void StashDeck(FlashOverlayWindow w, (Bitmap Picture, string Path) got)
        {
            GifFrames.TryGetValue(got.Picture, out var gif);
            var key = SourceIdentity(got.Path);
            if (ConditioningControlPanel.Services.DeckPrefetch.KeepDecoded(gif?.Frames.Count ?? 1, hasClip: false)) w.StashDeckReady(got.Picture, got.Path, key);
            else
            {
                Drop(got.Picture);
                w.StashDeckReady(null, got.Path, key);
            }
        }

        /// <summary>WPF TakeDeckReadyAsync (:2939): the waiting picture, decoded, or decoded again from its path.</summary>
        private static async Task<(Bitmap Picture, string Path)?> TakeDeckReady(FlashOverlayWindow w)
        {
            var (still, path) = w.TakeDeckReady();
            if (path == null) return null;
            if (still != null) return (still, path);
            return await DeckDecode(path, w.DeckRect) is { } decoded ? (decoded, path) : null;
        }

        /// <summary>WPF SourceIdentityLocked: a pack decrypt is a fresh temp file on every draw, so a pack
        /// picture is known by its source key; anything else by its whole path (or url).</summary>
        internal static string SourceIdentity(string? path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            if (path.Contains("://", StringComparison.Ordinal)) return path;
            try { return ConditioningControlPanel.Services.ContentPackStore.Current is { } store && store.TryGetSourceKey(path, out var key) ? key : path; }
            catch (Exception) { return path; }
        }

        /// <summary>WPF PickDeckPictureAsync (:2865): up to four draws from the flash pool (a remote one only
        /// after the first flip, under the flash consent rule), preferring a picture not up on any card nor
        /// waiting on one, then anything but the card's own face, decoded off the UI thread.</summary>
        internal static Task<(Bitmap Picture, string Path)?> LoadDeckPicture(FlashOverlayWindow w, bool localOnly)
        {
            var up = new HashSet<string>(Active.SelectMany(e => new[] { SourceIdentity(e.Window.SourcePath), e.Window.DeckReadyKey })
                .OfType<string>().Where(k => k.Length > 0), StringComparer.OrdinalIgnoreCase);
            var own = SourceIdentity(w.SourcePath);
            var rect = w.DeckRect;
            var s = CoreSettings.Current;
            return Task.Run<(Bitmap Picture, string Path)?>(() =>
            {
                var files = FlashFiles(s);
                var draws = new List<(string File, string Source, string Key)>();
                for (var i = 0; i < 4; i++)
                {
                    var remote = !localOnly && FlashSourceRules.ShouldDrawRemote(s, HasLocalImages(files.Count), Rng)
                        ? RemoteFlashSource.TryTake(Rng) : null;
                    if (remote is { PosterPath: { Length: > 0 } poster, Url: { Length: > 0 } url }) { draws.Add((poster, url, url)); continue; }
                    if (NextFlashPath(files) is not { } p) break;
                    draws.Add((p, p, SourceIdentity(p)));
                }
                var order = draws.Where(d => !up.Contains(d.Key))
                    .Concat(draws.Where(d => up.Contains(d.Key) && !string.Equals(d.Key, own, StringComparison.OrdinalIgnoreCase)));
                foreach (var d in order)
                {
                    try { if (DecodeDeckFace(d.File, rect) is { } face) return (face, d.Source); }
                    catch (Exception ex) { Log.Debug("Flicker Deck: could not decode {Path}: {E}", d.File, ex.Message); }
                }
                return null;
            });
        }

        /// <summary>WPF LoadImageAsync: a GIF or WebP keeps its frames (decoded as a flash is), a still decodes at the card's width.</summary>
        private static Bitmap? DecodeDeckFace(string file, PixelRect rect)
        {
            using (var codec = SkiaSharp.SKCodec.Create(file))
                if (codec is { FrameCount: > 1 } && DecodeAnimated(file, rect) is { } first) return first;
            using var stream = File.OpenRead(file);
            return Bitmap.DecodeToWidth(stream, Math.Max(1, rect.Width));
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
