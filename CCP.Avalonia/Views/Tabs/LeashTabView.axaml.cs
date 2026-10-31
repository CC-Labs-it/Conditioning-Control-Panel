using System;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Leash;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Leash;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>Social &gt; Leash. WPF page repaint and the leashed account's own card.</summary>
public partial class LeashTabView : UserControl
{
    private readonly Func<ILeashService?> _resolve;
    private ILeashService? _service;
    private LeashSelfCard? _self;

    public LeashTabView() : this(() => LeashHead.Service) { }
    internal LeashTabView(Func<ILeashService?> service)
    {
        _resolve = service;
        InitializeComponent();
        EmptyButton.Click += (_, _) => (TopLevel.GetTopLevel(this) as MainShellWindow)?.ShowTab("friends");
        DetachedFromVisualTree += (_, _) => Unsubscribe();
        PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty && !IsVisible) Unsubscribe(); };
    }

    internal bool ShowingEmpty => EmptyPanel.IsVisible;
    internal LeashSelfCard? SelfCard => _self;

    internal void OnShown()
    {
        Unsubscribe();
        LeashHead.ServiceChanged += OnServiceChanged;
        try { _service = _resolve(); } catch { _service = null; }
        if (_service != null) _service.SnapshotChanged += OnSnapshot;
        Render();
    }

    private void Unsubscribe()
    {
        LeashHead.ServiceChanged -= OnServiceChanged;
        if (_service != null) _service.SnapshotChanged -= OnSnapshot;
    }
    // WPF LeashDrawerSection.cs:37-39,48-60 rebinds on load. Also handle late service startup here.
    private void OnServiceChanged()
    {
        if (global::Avalonia.Threading.Dispatcher.UIThread.CheckAccess()) OnShown();
        else global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (IsEffectivelyVisible) OnShown(); });
    }
    private void OnSnapshot(LeashSnapshot _) => Render();

    private void Render()
    {
        MyLeash? me = null;
        try { if (_service?.Available == true) me = _service.Snapshot.Me; } catch { }
        SectionHost.Children.Clear();
        if (me != null)
        {
            if (_self == null) _self = new LeashSelfCard(me, () => _service);
            else _self.Update(me);
            SectionHost.Children.Add(_self);
            // WPF NoteOnScreen: assignments are seen here; punishments are seen on the gate.
            if (IsEffectivelyVisible && me.Assignment is { } a) _service?.NoteShown(a.Aid);
        }
        else _self = null;
        EmptyPanel.IsVisible = me == null;
    }
}
