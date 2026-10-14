using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class PresetsTabView
{
    private CardSheenAdorner? _cardSheen;
    private Control? _cardSheenHost;
    private IDisposable? _sheenVisibility;
    private Window? _sheenWindow;
    internal CardSheenAdorner? SelectedCardSheen => _cardSheen;
    internal Control? SelectedSheenHost => _cardSheenHost;

    private void InitializeCardSheen()
    {
        AttachedToVisualTree += (_, _) =>
        {
            _sheenVisibility = EffectiveVisibility.Watch(this, RefreshCardSheen);
            _sheenWindow = TopLevel.GetTopLevel(this) as Window;
            if (_sheenWindow != null) _sheenWindow.PropertyChanged += SheenWindowChanged;
            AmbientFxCanvas.Env.MotionGateChanged += RefreshCardSheen;
            CoreMods.ModChanged += SheenModChanged;
            Dispatcher.UIThread.Post(RefreshCardSheen, DispatcherPriority.Loaded);
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _sheenVisibility?.Dispose(); _sheenVisibility = null;
            if (_sheenWindow != null) _sheenWindow.PropertyChanged -= SheenWindowChanged;
            _sheenWindow = null;
            AmbientFxCanvas.Env.MotionGateChanged -= RefreshCardSheen;
            CoreMods.ModChanged -= SheenModChanged;
            DetachCardSheen();
        };
    }

    private void SheenWindowChanged(object? sender, global::Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.IsActiveProperty || e.Property == Window.WindowStateProperty) RefreshCardSheen();
    }

    private void SheenModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(() =>
    {
        // WPF TabFx_ModChanged detaches first, so Start re-reads the new glow colour.
        DetachCardSheen(); RefreshCardSheen();
    });

    internal void RefreshCardSheen()
    {
        Control? target = null;
        if (IsEffectivelyVisible && _sheenWindow is { IsActive: true, WindowState: not WindowState.Minimized }
            && AmbientFxCanvas.Env.AllowAmbientLoops)
        {
            target = _selectedPreset != null
                ? PresetCardsPanel.Children.OfType<Border>().FirstOrDefault(c => c.Tag as string == _selectedPreset.Id)
                : SessionRackPanel.Children.OfType<Border>().FirstOrDefault(c => c.Tag is Session s && s.Id == _selectedSession?.Id);
        }
        if (target == _cardSheenHost && _cardSheen != null) return;
        DetachCardSheen();
        if (target?.IsEffectivelyVisible != true) return;
        _cardSheen = CardSheenAdorner.Attach(target, _selectedPreset != null ? 17 : 10);
        if (_cardSheen != null) _cardSheenHost = target;
    }

    private void DetachCardSheen()
    {
        CardSheenAdorner.Detach(_cardSheen);
        _cardSheen = null; _cardSheenHost = null;
    }
}
