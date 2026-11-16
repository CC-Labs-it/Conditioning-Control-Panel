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
    /// tick that steps every card. Panic and Stop reach it through <see cref="CloseAll"/>.
    /// The shared stage (break blasts, the Whirlpool's pull) and the click lifetime boost are s73-deck-b's.
    /// </summary>
    internal static partial class FlashOverlay
    {
        /// <summary>The Deck switch (tests swap it).</summary>
        internal static Func<bool> DeckOn = () => SuperAccess.IsOn(SuperEffect.FlickerDeck);

        /// <summary>The next face for a flip (tests swap it): a picture decoded at the card's width, and its path.</summary>
        internal static Func<FlashOverlayWindow, Task<(Bitmap Picture, string Path)?>> DeckPicture = LoadDeckPicture;

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
            var (min, max) = ConditioningControlPanel.SuperCore.DeckOptions.BreakRange(s.DeckFlips);
            // Room for the lift (x1.1 and 10% up), the wobble, the peek, the shadow and the puff.
            var pad = (int)Math.Ceiling(Math.Max(rect.Width, rect.Height) * 0.25) + 32;
            w.AttachDeck(FlickerDeck.Create(_deckIndex++ % 8, Rng, min, max), pad, scaling);
            w.PointerEntered += (_, _) => DeckHover(w, true);
            w.PointerExited += (_, _) => DeckHover(w, false);
            HookDeckSwitch();
            if (_deckTimer == null)
            {
                _lastDeckTick = Clock.GetTimestamp();
                _deckTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => DeckTick());
                _deckTimer.Start();
            }
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
                if (w.Deck != null)
                {
                    any = true;
                    if (w.StepDeck(dt, level)) (raise ??= new()).Add(w);
                }
            foreach (var w in Exiting)
                if (w.Deck != null) { any = true; w.StepDeck(dt, level); }
            if (raise != null) foreach (var w in raise) RaiseDeck(w);
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
            if (!w.OfferDeckPicture(deck, got.Picture, got.Path)) got.Picture.Dispose();
        }

        /// <summary>Up to four draws from the flash pool, preferring a picture not up anywhere, then
        /// anything but the card's own face (WPF :2866-2880), decoded off the UI thread at the card's width.</summary>
        private static Task<(Bitmap Picture, string Path)?> LoadDeckPicture(FlashOverlayWindow w)
        {
            // shortcut: compares raw paths, not WPF's SourceIdentityLocked pack identity; a pack picture may repeat on a small pool.
            var up = new HashSet<string>(Active.Select(e => e.Window.SourcePath).OfType<string>(), StringComparer.OrdinalIgnoreCase);
            var own = w.SourcePath;
            var width = Math.Max(1, Active.Find(e => e.Window == w).Rect.Width);
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
                        using var stream = File.OpenRead(path);
                        return (Bitmap.DecodeToWidth(stream, width), path);
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
