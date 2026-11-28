using System;
using System.Collections.Generic;

namespace ConditioningControlPanel.Services
{
    /// <summary>In-memory history of the last displayed notifications, newest first.</summary>
    public sealed class NotificationHistoryBuffer<TType>
    {
        public const int Capacity = 50;
        public readonly record struct Entry(DateTime At, TType Type, string Text);

        private readonly LinkedList<Entry> _entries = new();
        private readonly object _gate = new();

        public event Action? Changed;
        public int Count { get { lock (_gate) return _entries.Count; } }

        public void Record(DateTime at, TType type, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            lock (_gate)
            {
                _entries.AddFirst(new Entry(at, type, text.Trim()));
                while (_entries.Count > Capacity) _entries.RemoveLast();
            }
            Changed?.Invoke();
        }

        public IReadOnlyList<Entry> Snapshot()
        {
            lock (_gate) return new List<Entry>(_entries);
        }
    }
}
