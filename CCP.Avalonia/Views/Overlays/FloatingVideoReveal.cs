using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>A floating LibVLC window has no pixels or input region until a frame is copied.
/// Showing only then also avoids an empty black window while its decoder starts.</summary>
internal sealed class FloatingVideoReveal(Window window)
{
    private bool _presented, _closed;

    public void Present()
    {
        if (_closed || _presented) return;
        _presented = true;
        window.Show();
    }

    public void Close()
    {
        _closed = true;
        window.Close();
    }
}
