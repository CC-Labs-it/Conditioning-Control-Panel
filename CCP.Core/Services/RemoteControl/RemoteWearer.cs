using System;

namespace ConditioningControlPanel.Services.Remote
{
    /// <summary>
    /// The Avalonia head's side of <see cref="RemoteHoldings"/> (owner, 2026-10-22; main 4adc4c476 +
    /// 2a712d9da): WPF RemoteControlService.V2 TakeWearerSnapshot / NoteRemoteWrite / HandBackStrictLock /
    /// HandBackWearerVisuals / DropWearerSnapshot and RemoteControlService.ReleaseRemoteSafetyState, for Core
    /// <see cref="RemoteRelay"/> and <see cref="RemoteCommands"/>. One relay per app, so one session here.
    ///
    /// <para>Every end path (the controller leaves, the session ends, a leash cut, a controller's panic)
    /// hands the wearer's own strict lock back: one the controller switched on goes off, one the wearer set
    /// stays, one a running Lockdown forced stays on and Lockdown's record is corrected instead. The panic
    /// key comes back on, except while a running Lockdown holds it off (its own end restores it). Never
    /// a blanket "strict off" any more. On disk while a session runs, so a crash heals at the next launch
    /// (<see cref="HealOnLaunch"/>, after Lockdown's own recovery).</para>
    /// </summary>
    public static class RemoteWearer
    {
        private static readonly object Gate = new();
        private static RemoteHoldings? _holdings;

        /// <summary>Where remote_restore.json lives. Tests swap it.</summary>
        internal static Func<string> Dir = () => CorePaths.UserData;

        private static LockdownService? Lockdown => LockdownService.Current;
        private static bool LockdownActive => Lockdown?.IsActive == true;

        /// <summary>True while a session's snapshot is held.</summary>
        public static bool Holding { get { lock (Gate) return _holdings != null; } }

        private static int StrictBefore(Models.AppSettings s) =>
            RemoteHoldings.StrictBefore(s.StrictLockEnabled, LockdownActive, Lockdown?.RecordedStrictLock == true);

        /// <summary>Session start. While a Lockdown runs, its recorded strict lock is the wearer's own.</summary>
        public static void Take()
        {
            var s = CoreSettings.Current;
            lock (Gate)
            {
                _holdings = RemoteHoldings.Take(f => f == RemoteHeld.StrictLock ? StrictBefore(s) : RemoteHoldings.Read(s, f),
                    DateTime.UtcNow.Ticks);
                RemoteHoldings.Save(Dir(), _holdings);
            }
        }

        /// <summary>Every controller write to a held setting comes here first, before the value lands.</summary>
        public static void NoteWrite(RemoteHeld field, int after)
        {
            var s = CoreSettings.Current;
            lock (Gate)
            {
                if (_holdings == null) return;
                var before = field == RemoteHeld.StrictLock ? StrictBefore(s) : RemoteHoldings.Read(s, field);
                _holdings.NoteWrite(field, before, after);
                RemoteHoldings.Save(Dir(), _holdings);
            }
        }

        /// <summary>WPF ReleaseRemoteSafetyState: the strict lock goes back to the wearer's own (a running
        /// Lockdown keeps the one it forced), the panic key comes back on unless a running Lockdown holds it
        /// off. Saves and resyncs the panic UI when anything moved. Returns true when it did.</summary>
        public static bool ReleaseSafetyState()
        {
            var s = CoreSettings.Current;
            var changed = false;
            lock (Gate)
            {
                if (_holdings != null && _holdings.Holds(RemoteHeld.StrictLock))
                {
                    var ld = Lockdown;
                    var back = _holdings.HandBackStrict(s.StrictLockEnabled, LockdownActive,
                        ld?.ForcedStrictLock == true, ld?.RecordedStrictLock == true);
                    RemoteHoldings.Save(Dir(), _holdings);
                    if (back.LockdownRecorded is bool recorded) ld?.CorrectRecordedStrictLock(recorded);
                    if (back.Live is bool live) { s.StrictLockEnabled = live; changed = true; }
                }
            }
            // A controller can never switch the panic key off on this head (RemoteCommandGate), so the only
            // question is whether a running Lockdown holds it off.
            if (RemoteHoldings.TurnsPanicBackOn(s.PanicKeyEnabled, remoteDisabledPanic: false,
                    LockdownActive, Lockdown?.DisabledPanicKey == true))
            {
                s.PanicKeyEnabled = true;
                changed = true;
            }
            if (!changed) return false;
            CoreSettings.Save();
            try { LockdownService.PanicKeyUiSync?.Invoke(); } catch (Exception ex) { Serilog.Log.Debug(ex, "[RemoteControl] panic UI sync"); }
            Serilog.Log.Information("[RemoteControl] Remote end: the wearer's own strict lock back, panic key per the Lockdown rule");
            return true;
        }

        /// <summary>WPF HandBackWearerVisuals: spiral, pink, their opacities and Melt go back to the wearer's
        /// own where they still hold what the controller wrote. Saves when anything moved.</summary>
        public static int HandBackAllButStrict()
        {
            var s = CoreSettings.Current;
            int n;
            lock (Gate)
            {
                if (_holdings == null) return 0;
                n = _holdings.HandBackAllButStrict(s);
                RemoteHoldings.Save(Dir(), _holdings);
            }
            if (n > 0)
            {
                CoreSettings.Save();
                Serilog.Log.Information("[RemoteControl] Handed back {Count} of the wearer's own settings", n);
            }
            return n;
        }

        /// <summary>The session is over: nothing is held any more and the recovery file goes.</summary>
        public static void Drop()
        {
            lock (Gate)
            {
                _holdings = null;
                RemoteHoldings.Clear(Dir());
            }
        }

        /// <summary>Launch, after <see cref="LockdownService.RecoverIfNeeded"/>: the app died with a
        /// controller's values in the settings. Saves when anything moved; returns the count.</summary>
        public static int HealOnLaunch()
        {
            var n = RemoteHoldings.HealOnLaunch(Dir(), CoreSettings.Current);
            if (n > 0) CoreSettings.Save();
            return n;
        }
    }
}
