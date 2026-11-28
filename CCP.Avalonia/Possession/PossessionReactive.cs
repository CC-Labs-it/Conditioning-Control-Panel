// WPF Services/Possession/PossessionEvents.cs:106-168.
using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class PossessionReactive : IDisposable
{
    private PossessionDirector? _director;
    private readonly PossessionPointer _pointer;
    private readonly INotifyPropertyChanged? _settings;

    internal PossessionReactive(PossessionDirector director, PossessionPointer pointer, INotifyPropertyChanged? settings)
    {
        _director = director;
        _pointer = pointer;
        _settings = settings;
        _pointer.HoverChanged += OnHover;
        if (_settings != null) _settings.PropertyChanged += OnSettingChanged;
    }

    private void OnHover(Control? control)
    {
        var director = _director;
        if (director == null || control is not Button || control is ToggleButton) return;
        try
        {
            if (!PossessionReactiveRules.LooksLikeStartStop(control.Name, (control as ContentControl)?.Content as string, PossessionTree.GetName(control))) return;
            if (director.TargetFor(control, PossessionRole.Button) is { } target)
                director.RequestReactive("dodge", target, PossessionRung.Drift);
        }
        catch (Exception ex) { Serilog.Log.Debug("Possession reactive hover failed: {Error}", ex.Message); }
    }

    private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_director?.IsHaunting != true || PossessionReactiveRules.IsConsentSetting(e.PropertyName)) return;
        void React()
        {
            try
            {
                var director = _director;
                if (director?.IsHaunting != true) return;
                if (director.NearestTarget(PossessionRole.Label) is { } target)
                    director.RequestReactive("typo", target, PossessionRung.Drift);
            }
            catch (Exception ex) { Serilog.Log.Debug("Possession reactive setting failed: {Error}", ex.Message); }
        }
        if (Dispatcher.UIThread.CheckAccess()) React(); else Dispatcher.UIThread.Post(React);
    }

    public void Dispose()
    {
        _director = null;
        _pointer.HoverChanged -= OnHover;
        if (_settings != null) _settings.PropertyChanged -= OnSettingChanged;
    }
}
