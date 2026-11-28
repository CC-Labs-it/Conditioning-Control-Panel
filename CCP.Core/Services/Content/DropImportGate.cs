using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// One dropped pack imports at a time (ccp-bugs #1706). Each run gets a number; a progress report is shown only
    /// while its own run is going, so a report that lands after the import ended can never put the card back up
    /// and leave every later drop refused. A refused drop says why at most once every few seconds.
    /// </summary>
    public sealed class DropImportGate
    {
        public static readonly TimeSpan BusyNoticeGap = TimeSpan.FromSeconds(4);

        private int _run;
        private DateTime _lastBusyNotice = DateTime.MinValue;

        public bool Busy { get; private set; }

        public int Begin() { Busy = true; return ++_run; }

        public bool Accepts(int run) => Busy && run == _run;

        public void End(int run) { if (run == _run) Busy = false; }

        /// <summary>True when a refused drop should show the "still importing" line now.</summary>
        public bool ShouldSayBusy(DateTime nowUtc)
        {
            if (!Busy || nowUtc - _lastBusyNotice < BusyNoticeGap) return false;
            _lastBusyNotice = nowUtc;
            return true;
        }
    }
}
