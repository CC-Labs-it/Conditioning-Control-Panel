using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>Social > Leash. The own card survives poll updates; detached pages release subscriptions.</summary>
public partial class LeashTabView : UserControl
{
    private ILeashService? _service;
    private LeashSelfCard? _self;
    public LeashTabView()
    {
        InitializeComponent();
        EmptyButton.Click += (_, _) => (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab("friends");
        AttachedToVisualTree += (_, _) => Bind();
        DetachedFromVisualTree += (_, _) => Unbind();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible) Bind();
        };
    }

    private void Unbind()
    {
        if (_service != null) _service.SnapshotChanged -= OnSnapshot;
        _service = null;
    }

    internal void Bind()
    {
        Unbind();
        _service = LeashHead.Service;
        if (_service != null) _service.SnapshotChanged += OnSnapshot;
        Refresh();
    }

    private void OnSnapshot(LeashSnapshot _) => Refresh();
    private void Refresh()
    {
        var me = _service?.Available == true ? _service.Snapshot.Me : null;
        SectionHost.Children.Clear();
        if (me != null)
        {
            if (_self == null) _self = new LeashSelfCard(me, () => _service);
            else _self.Update(me);
            SectionHost.Children.Add(_self);
            if (IsVisible && VisualRoot != null && me.Assignment is { } a) _service?.NoteShown(a.Aid);
        }
        else _self = null;
        EmptyPanel.IsVisible = me == null;
    }

    internal bool ShowingEmpty => EmptyPanel.IsVisible;
}
