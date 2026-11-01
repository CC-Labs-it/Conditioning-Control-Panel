// WPF FriendsDrawer.cs:394-395 and LeashDrawerSection.cs:49-71,91-103,113-130.
using System;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    private ILeashService? _leash;
    private LeashSelfCard? _leashSelf;

    private void AddLeashSelf()
    {
        var next = LeashHead.Service;
        if (!ReferenceEquals(next, _leash))
        {
            CloseLeash();
            _leash = next;
            if (_leash != null) _leash.SnapshotChanged += OnLeashSnapshot;
        }

        MyLeash? me = null;
        try { if (_leash?.Available == true) me = _leash.Snapshot.Me; } catch { }
        if (me == null) { _leashSelf = null; return; }
        if (_leashSelf == null) _leashSelf = new LeashSelfCard(me, () => _leash);
        else _leashSelf.Update(me);
        _list.Children.Add(_leashSelf);
        try { if (me.Assignment is { } a) _leash?.NoteShown(a.Aid); } catch { }
    }

    private void OnLeashSnapshot(LeashSnapshot _) { if (_isOpen) Render(); }

    private void CloseLeash()
    {
        if (_leash != null) _leash.SnapshotChanged -= OnLeashSnapshot;
        _leash = null;
    }
}
