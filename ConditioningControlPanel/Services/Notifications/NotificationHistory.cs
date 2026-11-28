using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The last in-app toasts, newest first (ccp-bugs #1688: several arrived at once and there was
    /// nowhere to read them again). Memory only: machine-local, never saved, never synced, and it
    /// never raises anything on its own.
    /// </summary>
    public sealed class NotificationHistory
    {
        public const int Capacity = 50;

        public readonly record struct Entry(DateTime At, NotificationType Type, string Text);

        private readonly LinkedList<Entry> _entries = new();
        private readonly object _gate = new();

        /// <summary>Raised after an entry is added (on the caller's thread).</summary>
        public event Action? Changed;

        public int Count { get { lock (_gate) return _entries.Count; } }

        public void Record(DateTime at, NotificationType type, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (_gate)
            {
                _entries.AddFirst(new Entry(at, type, text.Trim()));
                while (_entries.Count > Capacity) _entries.RemoveLast();
            }
            Changed?.Invoke();
        }

        /// <summary>A copy, newest first.</summary>
        public IReadOnlyList<Entry> Snapshot()
        {
            lock (_gate) return new List<Entry>(_entries);
        }
    }
}
