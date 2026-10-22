// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Chaster.cs (InitializeChasterFlash,
// OnChasterBooked, ShowBookedFlash, OnChasterCapRefused, ShowCapNotice): a booked price pops as
// ChasterBookedPop where it was earned (cause, else cursor), coalesced by Core BookedFlashPlan. Where
// the platform cannot show that window (Wayland, headless) it floats off the rail padlock, WPF's own
// fallback. The chip ring pulses in the figure's colour, as WPF. A full tab pops a muted tag.
using System;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private BookedFlashPlan.Figure? _chasterFigure;
        private ChasterBookedFlash? _chasterFlash;
        private ChasterBookedPop? _chasterPop;
        private long? _chasterCapShownMs;

        /// <summary>Clock for the coalescing window and the cap throttle; tests step it (P08).</summary>
        internal static TimeProvider ChasterClock = TimeProvider.System;

        private static long ChasterNowMs() => ChasterClock.GetElapsedTime(0).Ticks / TimeSpan.TicksPerMillisecond;

        /// <summary>Wire the figure to the service App built (WPF calls this from the ctor).</summary>
        internal void InitializeChasterFlash(ChasterService chaster)
        {
            chaster.BookedAt += OnChasterBooked;
            chaster.CapRefused += OnChasterCapRefused;
            Closed += (_, _) =>
            {
                chaster.BookedAt -= OnChasterBooked;
                chaster.CapRefused -= OnChasterCapRefused;
                ChasterBookedPop.CloseAll();   // an open pop is an unowned window: it must not hold the app
            };
            InitializeCirceLines(chaster); // MainShellWindow.CirceLines.cs
        }

        /// <summary>Raised on whatever thread booked it, so marshalled first.</summary>
        private void OnChasterBooked(string eventId, TabBooking booking, ScreenPoint? originPx)
        {
            if (booking.AppliedSeconds == 0) return;
            Dispatcher.UIThread.Post(() => ShowBookedFlash(eventId, booking.AppliedSeconds, originPx));
        }

        private void ShowBookedFlash(string eventId, int seconds, ScreenPoint? causePx)
        {
            try
            {
                var (figure, isNew) = BookedFlashPlan.Merge(_chasterFigure, seconds, eventId, ChasterNowMs());
                _chasterFigure = figure;
                var level = AmbientFxCanvas.Env.Level;
                var look = BookedFlashPlan.For(figure, level);
                if (!isNew && (_chasterPop is not null || _chasterFlash is not null))
                {
                    // Same figure, bigger number; a price and its credit in the same breath net to nothing.
                    if (look is { } merged)
                    {
                        _chasterPop?.Retitle(merged.Text, merged.Colour, merged.Source);
                        _chasterFlash?.Retitle(merged.Text, merged.Colour);
                    }
                    else
                    {
                        _chasterPop?.Dismiss(); _chasterPop = null;
                        _chasterFlash?.Dismiss(); _chasterFlash = null;
                    }
                }
                else if (look is { } fresh)
                {
                    // WPF :106-121: at the cause, else the cursor, in its own topmost window; earlier
                    // pops stay up and stack. The rail is the last fallback.
                    _chasterFlash?.Dismiss(); _chasterFlash = null;
                    _chasterPop = BookedPopLayout.ResolveOrigin(Valid(causePx), ChasterBookedPop.Cursor()) is { } at
                        ? ChasterBookedPop.Show(this, at, fresh, BookedPopLayout.For(level))
                        : null;
                    if (_chasterPop is null && IsVisible) _chasterFlash = ChasterBookedFlash.Show(Named<ChasterRailChip>("ChasterRail"), fresh);
                }
                // The ring takes the figure's colour for a beat; a net-zero merge has no pulse.
                if (IsVisible && look is { } pulse) Named<ChasterRailChip>("ChasterRail")?.Pulse(global::Avalonia.Media.Color.FromUInt32(pulse.Colour));
            }
            catch (Exception ex) { Serilog.Log.Debug("[Chaster] booked flash: {E}", ex.Message); }
        }

        /// <summary>A red bubble the player popped found the tab full: a short muted tag where it popped (WPF :136-158).</summary>
        private void OnChasterCapRefused(string eventId, TabRefusal refusal, ScreenPoint? originPx) =>
            Dispatcher.UIThread.Post(() => ShowCapNotice(refusal, originPx));

        private void ShowCapNotice(TabRefusal refusal, ScreenPoint? causePx)
        {
            try
            {
                var now = ChasterNowMs();
                if (CapNotice.Throttled(_chasterCapShownMs, now)) return;
                if (BookedPopLayout.ResolveOrigin(Valid(causePx), ChasterBookedPop.Cursor()) is not { } at) return;
                var motion = AmbientFxCanvas.Env.Level;
                var look = CapNotice.Look(Loc.Get(CapNotice.TextKey(refusal)), motion);
                if (ChasterBookedPop.Show(this, at, look, CapNotice.Pop(motion)) is not null) _chasterCapShownMs = now;
            }
            catch (Exception ex) { Serilog.Log.Debug("[Chaster] cap tag: {E}", ex.Message); }
        }

        private static ScreenPoint? Valid(ScreenPoint? p) =>
            p is { } v && double.IsFinite(v.X) && double.IsFinite(v.Y) ? v : null;
    }
}
