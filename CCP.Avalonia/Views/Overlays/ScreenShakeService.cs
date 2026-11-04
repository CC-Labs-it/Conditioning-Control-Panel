// WPF: ConditioningControlPanel/Services/UI/ScreenShakeService.cs:22
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

internal sealed class ScreenShakeService
{
    internal static readonly ScreenShakeService Instance = new();
    private readonly List<(Control Root, ITransform? Prior, TranslateTransform Shake)> _targets = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private DateTime _endsAt;
    private double _amplitude;
    // Headless tests supply windows; production uses the desktop lifetime.
    internal Func<IEnumerable<Window>>? WindowsForTests;

    internal ScreenShakeService() => _timer.Tick += (_, _) => Tick();

    internal void Shake(double intensity, int durationMs)
    {
        intensity = Math.Clamp(intensity, 0, 1);
        if (double.IsNaN(intensity) || intensity <= 0 || durationMs <= 0) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Shake(intensity, durationMs));
            return;
        }
        var desktop = Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var windows = WindowsForTests?.Invoke() ?? desktop?.Windows;
        if (windows != null) Start(windows, intensity, durationMs);
    }

    internal void Start(IEnumerable<Window> windows, double intensity, int durationMs)
    {
        Reset();
        _amplitude = 28 * Math.Clamp(intensity, 0, 1);
        if (double.IsNaN(_amplitude) || _amplitude <= 0 || durationMs <= 0) return;
        foreach (var window in windows)
        {
            if (!window.IsVisible || window.Content is not Control root) continue;
            var shake = new TranslateTransform();
            _targets.Add((root, root.RenderTransform, shake));
            root.RenderTransform = shake;
            var script = BrowserShakeScript(_amplitude, durationMs);
            foreach (var web in root.GetVisualDescendants().OfType<WebHost>()
                         .Prepend(root as WebHost).Where(w => w is { IsEffectivelyVisible: true, HasEngine: true }))
                _ = web!.InvokeScriptAsync(script);
        }
        _endsAt = DateTime.UtcNow.AddMilliseconds(durationMs);
        if (_targets.Count > 0) { Tick(); _timer.Start(); }
    }

    // WPF ScreenShakeService.cs:126: video first, then fullscreen or document root.
    // Restore the page's inline transform on expiry and before restarting.
    internal static string BrowserShakeScript(double amplitude, int durationMs) =>
        "(function(amp,ms){"
        + "var old=window._ccpShake;if(old){clearInterval(old.timer);old.restore();}"
        + "var t=document.querySelector('video')||document.fullscreenElement||document.documentElement;if(!t)return;"
        + "var value=t.style.getPropertyValue('transform'),priority=t.style.getPropertyPriority('transform');"
        + "var state={restore:function(){if(value)t.style.setProperty('transform',value,priority);else t.style.removeProperty('transform');}};"
        + "window._ccpShake=state;var end=Date.now()+ms;state.timer=setInterval(function(){"
        + "if(Date.now()>=end){clearInterval(state.timer);state.restore();if(window._ccpShake===state)window._ccpShake=null;return;}"
        + "var x=(Math.random()*2-1)*amp,y=(Math.random()*2-1)*amp;"
        + "t.style.setProperty('transform','translate('+x.toFixed(1)+'px,'+y.toFixed(1)+'px)','important');},30);"
        + "})(" + amplitude.ToString(CultureInfo.InvariantCulture) + "," + durationMs.ToString(CultureInfo.InvariantCulture) + ");";

    internal void Tick()
    {
        if (DateTime.UtcNow >= _endsAt) { Reset(); return; }
        var x = (Random.Shared.NextDouble() * 2 - 1) * _amplitude;
        var y = (Random.Shared.NextDouble() * 2 - 1) * _amplitude;
        foreach (var target in _targets) { target.Shake.X = x; target.Shake.Y = y; }
    }

    internal void Reset()
    {
        _timer.Stop();
        foreach (var target in _targets)
            if (ReferenceEquals(target.Root.RenderTransform, target.Shake))
                target.Root.RenderTransform = target.Prior;
        _targets.Clear();
    }
}
