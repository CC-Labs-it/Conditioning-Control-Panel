using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Descent;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class SpiralTabView
{
    private DescentService? _wiredDescent;

    // WPF SpiralTabView.xaml.cs:249,268: subscribe once and release the same service.
    private void WireDescent()
    {
        if (ReferenceEquals(_wiredDescent, App.Descent)) return;
        UnwireDescent();
        _wiredDescent = App.Descent;
        if (_wiredDescent != null) _wiredDescent.BlockChanged += OnBlockChanged;
    }

    private void UnwireDescent()
    {
        if (_wiredDescent != null) _wiredDescent.BlockChanged -= OnBlockChanged;
        _wiredDescent = null;
    }

    // WPF SpiralTabView.xaml.cs:309-333: update an existing canvas before repainting.
    private void OnBlockChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        // Ignore events queued before a tab exit, detach or service replacement.
        if (!IsVisible || !ReferenceEquals(sender, _wiredDescent)) return;
        var block = _wiredDescent?.Current;
        if (block != null) _embed?.PostState(block);
        Refresh();
    });
}
