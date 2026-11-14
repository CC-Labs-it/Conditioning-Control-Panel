using System;
using System.Runtime.CompilerServices;
using ConditioningControlPanel.Services.Audio;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>Records WebView audio starts, once per event and once per attached view.</summary>
internal sealed class WebAudioTracker(RecentSounds ring, Func<bool> engineRunning)
{
    private readonly ConditionalWeakTable<object, object> _hooked = new();

    internal void Attach(object view, Action<Action> subscribe, Func<(bool Playing, string? Source, bool Muted)> read)
    {
        try
        {
            if (_hooked.TryGetValue(view, out _)) return;
            _hooked.Add(view, new object());
            subscribe(() =>
            {
                try
                {
                    var (playing, source, muted) = read();
                    if (!playing) return;
                    var (player, file) = WebPageSound.Describe(source, muted);
                    ring.Note(player, file, engineRunning(), false, DateTime.Now);
                }
                catch { }
            });
        }
        catch { }
    }
}
