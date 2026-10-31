using System;
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>A floating LibVLC window has no pixels, input region or sound until a frame is copied.</summary>
internal sealed class FloatingVideoReveal
{
    private readonly Window _window;
    private readonly Action<bool>? _holdAudio;
    private bool _presented, _closed;

    public FloatingVideoReveal(Window window, Action<bool>? holdAudio = null)
    {
        _window = window;
        _holdAudio = holdAudio;
        _holdAudio?.Invoke(true);
    }

    public void Present()
    {
        if (_closed || _presented) return;
        _presented = true;
        _window.Show();
        if (!_closed) _holdAudio?.Invoke(false);
    }

    public void Close()
    {
        _closed = true;
        _holdAudio?.Invoke(true);
        _window.Close();
    }
}
