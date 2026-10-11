using System.Collections.Generic;

namespace ConditioningControlPanel.Services.Video
{
    /// <summary>
    /// Which floating video window carries the sound. One rule: the most recent window has it,
    /// where "recent" means spawned or touched. So the last spawned window has the sound, a touched
    /// window takes it, and when the holder closes the most recent survivor takes it. While any
    /// window is up there is exactly one holder. Pure (FloatingAudioFocusTests).
    /// </summary>
    internal sealed class FloatingAudioFocus<T> where T : class
    {
        // Oldest first; the last entry is the holder.
        private readonly List<T> _recency = new();

        public int Count => _recency.Count;

        /// <summary>The window with the sound, or null when none is up.</summary>
        public T? Holder => _recency.Count > 0 ? _recency[^1] : null;

        /// <summary>Every window, oldest first.</summary>
        public IReadOnlyList<T> Items => _recency;

        public bool HasSound(T item) => ReferenceEquals(Holder, item);

        /// <summary>A new window: it takes the sound. Adding a known window is a touch.</summary>
        public void Add(T item)
        {
            _recency.Remove(item);
            _recency.Add(item);
        }

        /// <summary>The player pressed or flung a window. True when the holder changed.</summary>
        public bool Touch(T item)
        {
            if (!_recency.Contains(item) || ReferenceEquals(Holder, item)) return false;
            _recency.Remove(item);
            _recency.Add(item);
            return true;
        }

        /// <summary>A window closed. True when it had the sound (the next most recent has it now).</summary>
        public bool Remove(T item)
        {
            var held = ReferenceEquals(Holder, item);
            return _recency.Remove(item) && held;
        }

        public void Clear() => _recency.Clear();
    }
}
