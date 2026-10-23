// The kept Escape (owner, 2026-10-12: Escape only pauses a game, it never closes its window). WPF
// MainWindow.TryRacePauseOnEscape asks each host IsInFront / IsReady and hands the kept press over with
// PostKeptEscape (CaucusHostService, PieceByPieceHostService, BackRoomHostService, DtrhHostService); each page
// plays it once through its own host-escape.js. Here one GameWindow hosts every game, so the same three
// answers live on the window, keyed by the game it runs. A second Escape inside 2 s stays a full panic
// (PanicPolicy.GameClaimsEscapeAsPause), which closes it through PanicSurfaces "games".
// Not on this head (no host yet): the full Breakout, the Arcademy, the Back Room room and Goon. FYP and
// Just Drop are not Escape-pause games on WPF either: a first Escape closes them.
using System;
using System.Linq;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        /// <summary>Set by the shell's "exit" frame: the page is on its way out (WPF ArmExitWatchdog).</summary>
        private bool _shellExiting;

        /// <summary>Which kept-Escape page this window runs (Core PanicPolicy.KeptEscapePage), None for a
        /// game that does not pause on Escape.</summary>
        internal PanicPolicy.KeptEscapePage KeptEscapePage => Spec.Id.ToLowerInvariant() switch
        {
            "race" => PanicPolicy.KeptEscapePage.Race,
            PbpId => PanicPolicy.KeptEscapePage.Board,
            "breakoutdemo" or "breakout" => PanicPolicy.KeptEscapePage.Breakout,
            DtrhId => PanicPolicy.KeptEscapePage.Dtrh,
            _ => PanicPolicy.KeptEscapePage.None,
        };

        /// <summary>WPF IsReady per host: the page said ready and is not on its way out. A booting or
        /// reloading page cannot pause, so it never keeps a press (251b60807, frontGameReady).</summary>
        internal bool KeptEscapeReady => IsReady && !_closing && !_shellExiting && !_raceExiting && !_dtrhExiting;

        /// <summary>WPF IsInFront: the open Escape-pause game window that is the active window, else null.</summary>
        internal static GameWindow? EscapePauseInFront()
        {
            lock (Open) return Open.FirstOrDefault(w => w.IsActive && w.KeptEscapePage != PanicPolicy.KeptEscapePage.None);
        }

        /// <summary>WPF PostKeptEscape: hand the page the Escape the panel kept as its pause (its host-escape.js
        /// plays it once unless the real key reached it too) and give it the keyboard back. The board's frame is
        /// <c>pbp:escape</c> (WPF PieceByPieceHostService.KeptEscapeType); every other page reads
        /// <c>kept-escape</c>.</summary>
        internal void PostKeptEscape()
        {
            try
            {
                Post(new { type = KeptEscapePage == PanicPolicy.KeptEscapePage.Board ? "pbp:escape" : "kept-escape" });
                Web.Focus();
            }
            catch (Exception ex) { Log.Debug("[Game] {Id}: kept Escape post failed: {E}", Spec.Id, ex.Message); }
        }
    }
}
