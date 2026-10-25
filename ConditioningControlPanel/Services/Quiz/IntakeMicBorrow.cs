using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Quiz
{
    /// <summary>
    /// Graded Intake BORROWS THE MIC (owner, 2026-10-13). The mic has one owner at a time. While the
    /// "Hey Bambi" wake-word loop (or push-to-talk) is armed it holds the mic for good, so a say-it
    /// item used to be told <c>busy</c> and fell to typed input. Now the item stands the wake word
    /// down for the length of the spoken answer and re-arms it after, the way a lock card
    /// (<c>LockCardWindow.StartVoiceSolve</c>) and a Deeper speak prompt (<c>SpeakPromptSession</c>)
    /// already take it.
    ///
    /// <para>ONE PROMISE: the wake word always comes back. Every session that took the mic is a
    /// holder; the mic goes back when the LAST holder leaves, whatever the reason (matched answer,
    /// the question's timer, idle, an error, the window closing, panic). <see cref="ReturnAll"/> is
    /// the teardown path: it gives the mic back even if a session never reported in.</para>
    ///
    /// <para>Holders, not a flag: the page opens the next say-it item before the previous listen
    /// loop has finished winding down on the pool, so the old loop's return can arrive AFTER the new
    /// item took the mic. With a flag that late return would re-arm the wake word under the new
    /// item. With holders it only removes the old one.</para>
    ///
    /// <para>Giving back is "reconcile with settings" (<c>AutonomyService.RefreshVoiceInputModes</c>),
    /// never "force on": if the user switched the wake word off while the mic was on loan, it stays
    /// off. No live mic is touched here, so the rules are tested directly.</para>
    /// </summary>
    internal sealed class IntakeMicBorrow
    {
        private readonly object _gate = new();
        private readonly HashSet<int> _holders = new();
        private readonly Func<bool> _wakeArmed;
        private readonly Action _standDown;
        private readonly Action _giveBack;
        private bool _owed;   // we stood the wake word down and have not given it back yet

        /// <param name="wakeArmed">True while the wake word / push-to-talk is armed by the user.</param>
        /// <param name="standDown">Stop the wake loop and release its capture session.</param>
        /// <param name="giveBack">Re-arm the wake word per current settings.</param>
        public IntakeMicBorrow(Func<bool> wakeArmed, Action standDown, Action giveBack)
        {
            _wakeArmed = wakeArmed ?? throw new ArgumentNullException(nameof(wakeArmed));
            _standDown = standDown ?? throw new ArgumentNullException(nameof(standDown));
            _giveBack = giveBack ?? throw new ArgumentNullException(nameof(giveBack));
        }

        /// <summary>True while the wake word is stood down on our account.</summary>
        public bool IsOwed { get { lock (_gate) return _owed; } }

        /// <summary>Sessions currently holding the mic.</summary>
        public int Holders { get { lock (_gate) return _holders.Count; } }

        /// <summary>
        /// Session <paramref name="id"/> wants the mic. Stands the wake word down if it is armed.
        /// Safe to call again for the same session before each listen window: if something re-armed
        /// the wake word in between (a lock card closing hands it back), it is stood down again.
        /// Returns true when this call stood it down (the caller then waits a beat for the capture
        /// session to let go).
        /// </summary>
        public bool Take(int id)
        {
            lock (_gate)
            {
                _holders.Add(id);
                bool armed;
                try { armed = _wakeArmed(); } catch { armed = false; }
                if (!armed) return false;
                try { _standDown(); } catch { }
                _owed = true;
                return true;
            }
        }

        /// <summary>Session <paramref name="id"/> is done. The last one out gives the mic back.
        /// Idempotent; a session that never took the mic is a no-op.</summary>
        public void Return(int id)
        {
            lock (_gate)
            {
                if (!_holders.Remove(id)) return;
                if (_holders.Count > 0) return;
                GiveBackLocked();
            }
        }

        /// <summary>Teardown (window closed, panic): drop every holder and give the mic back now.
        /// A late <see cref="Return"/> from a loop still winding down is then a no-op.</summary>
        public void ReturnAll()
        {
            lock (_gate)
            {
                _holders.Clear();
                GiveBackLocked();
            }
        }

        private void GiveBackLocked()
        {
            if (!_owed) return;
            _owed = false;
            try { _giveBack(); } catch { }
        }
    }
}
