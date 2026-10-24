using System;
using System.ComponentModel;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Audio
{
    /// <summary>
    /// One place that says "the master volume just moved" (ccp-bugs #1432).
    ///
    /// <para>The settings slider used to push the new level by hand to the two players it knew
    /// about (video, Brain Drain). Every other way the level changes - a preset, a session, the
    /// launcher dial, the volume hotkeys, a remote controller - writes
    /// <see cref="AppSettings.MasterVolume"/> without running that handler, so a player that was
    /// already sounding kept its old level. This watches the setting itself, so it does not matter
    /// who moved it.</para>
    ///
    /// <para>Handlers run on whatever thread wrote the setting and must stay cheap (set a gain on
    /// a reader, nothing else).</para>
    /// </summary>
    public sealed class CoreMasterVolumeWatch
    {
        /// <summary>The app-wide watch. Players subscribe here; it hooks the live settings object
        /// the first time anyone asks and follows it when the settings object is swapped.</summary>
        public static CoreMasterVolumeWatch Shared { get; } = new CoreMasterVolumeWatch();

        private readonly object _gate = new();
        private AppSettings? _hooked;
        private bool _hookedReplace;

        /// <summary>Raised with the new level (0-100) after the setting changes.</summary>
        public event Action<int>? Changed;

        /// <summary>Hooks the app's current settings object. Safe to call often: it is a no-op
        /// once hooked to the live instance, and it quietly does nothing before settings exist.</summary>
        public void EnsureHooked()
        {
            try
            {
                var service = CoreSettings.Service;
                if (service == null) { Hook(CoreSettings.Current); return; }
                lock (_gate)
                {
                    if (!_hookedReplace)
                    {
                        _hookedReplace = true;
                        service.CurrentReplaced += OnCurrentReplaced;
                    }
                }
                Hook(service.Current);
            }
            catch { /* test host or pre-settings startup: nothing to watch yet */ }
        }

        /// <summary>Follows <paramref name="settings"/> and drops the object followed before.</summary>
        internal void Hook(AppSettings? settings)
        {
            lock (_gate)
            {
                if (ReferenceEquals(_hooked, settings)) return;
                if (_hooked != null) _hooked.PropertyChanged -= OnSettingChanged;
                _hooked = settings;
                if (settings != null) settings.PropertyChanged += OnSettingChanged;
            }
        }

        private void OnCurrentReplaced()
        {
            try
            {
                var current = CoreSettings.Service?.Current;
                Hook(current);
                // A swapped settings object can carry a different level without any change event.
                if (current != null) Raise(current.MasterVolume);
            }
            catch { }
        }

        private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AppSettings.MasterVolume)) return;
            if (sender is AppSettings s) Raise(s.MasterVolume);
        }

        private void Raise(int level)
        {
            var handlers = Changed;
            if (handlers == null) return;
            foreach (Action<int> handler in handlers.GetInvocationList())
            {
                // One player's failure must never stop the others following the dial.
                try { handler(level); } catch { }
            }
        }
    }
}
