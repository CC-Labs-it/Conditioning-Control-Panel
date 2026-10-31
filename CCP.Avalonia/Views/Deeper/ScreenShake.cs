// Adapted from parity/trunk b8576c3fade91143293d04e29abb8a30e1702d57; WPF ScreenShakeService wins on timing/offsets.
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
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Deeper
{
    /// <summary>
    /// Deeper's <c>screen_shake</c> (WPF Services/UI/ScreenShakeService): the content of every visible app
    /// window jitters together by up to 28 px x intensity on a 30 ms tick for the action's duration,
    /// then each window gets its own transform back. The timer exists only while a shake runs.
    /// A new shake replaces the running one. WebHost pages receive WPF's CSS shake too.
    /// </summary>
    internal static class ScreenShake
    {
        internal const int TickMs = 30;
        internal const double MaxOffsetPx = 28.0;

        private static readonly Random Rng = new();
        private static readonly List<(Control Root, TranslateTransform Shake, ITransform? Prior)> Targets = new();
        private static DispatcherTimer? _timer;
        private static long _start;
        internal static Func<long> Now = () => Environment.TickCount64;
        private static readonly List<Controls.WebHost> WebTargets = new();
        private static int _durationMs;
        private static double _amplitude;

        internal static bool IsRunning => _timer != null;

        /// <summary>The windows to shake (tests swap it).</summary>
        internal static Func<IEnumerable<Window>> WindowsProvider = () =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows ?? (IEnumerable<Window>)Array.Empty<Window>();

        internal static void Shake(double intensity, int durationMs)
        {
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Shake(intensity, durationMs)); return; }
            intensity = double.IsFinite(intensity) ? Math.Clamp(intensity, 0, 1) : 0;
            if (intensity <= 0 || durationMs <= 0) return;
            try
            {
                Stop();   // restores the last shake's transforms before the windows are read again
                foreach (var win in WindowsProvider())
                {
                    if (!win.IsVisible || win.Content is not Control root) continue;
                    var shake = new TranslateTransform();
                    Targets.Add((root, shake, root.RenderTransform));
                    root.RenderTransform = shake;
                }
                foreach (var win in WindowsProvider().Where(w => w.IsVisible))
                    foreach (var web in win.GetVisualDescendants().OfType<Controls.WebHost>())
                    {
                        WebTargets.Add(web);
                        _ = web.InvokeScriptAsync(WebScript(MaxOffsetPx * intensity, durationMs));
                    }
                if (Targets.Count == 0) return;
                _amplitude = MaxOffsetPx * intensity;
                _durationMs = durationMs;
                _start = Now();
                _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(TickMs) };
                _timer.Tick += (_, _) => Tick();
                _timer.Start();
            }
            catch (Exception ex) { Log.Debug("Deeper screen_shake: {E}", ex.Message); Stop(); }
        }

        // WPF's native web views do not inherit the root transform. The page timer self-expires;
        // Stop also clears it immediately so a panic cannot leave a CSS shake running.
        internal static string WebScript(double amplitude, int durationMs) =>
            "(function(amp,durationMs){" + WebStopScript +
            "var t=document.querySelector('video')||document.fullscreenElement||document.documentElement;if(!t)return;" +
            "window._ccpShakeTarget=t;var endsAt=Date.now()+durationMs;" +
            "window._ccpShakeTimer=setInterval(function(){" +
            "if(Date.now()>=endsAt){" + WebStopScript + "return;}" +
            "var dx=(Math.random()*2-1)*amp,dy=(Math.random()*2-1)*amp;" +
            "t.style.setProperty('transform','translate('+dx.toFixed(1)+'px,'+dy.toFixed(1)+'px)','important');" +
            "},30);})(" + amplitude.ToString("0.0", CultureInfo.InvariantCulture) + "," + durationMs.ToString(CultureInfo.InvariantCulture) + ");";

        private const string WebStopScript = "try{clearInterval(window._ccpShakeTimer);window._ccpShakeTimer=null;" +
            "var p=window._ccpShakeTarget;if(p&&p.style)p.style.removeProperty('transform');}catch(e){}";

        internal static void Tick()
        {
            if (Now() - _start >= _durationMs) { Stop(); return; }
            // WPF: one offset for all windows, full amplitude until the duration ends.
            var dx = (Rng.NextDouble() * 2 - 1) * _amplitude;
            var dy = (Rng.NextDouble() * 2 - 1) * _amplitude;
            foreach (var t in Targets) { t.Shake.X = dx; t.Shake.Y = dy; }
        }

        /// <summary>At rest now, every window's own transform back (the end of a shake, engine stop, panic).</summary>
        internal static void Stop()
        {
            try { _timer?.Stop(); } catch { }
            _timer = null;
            foreach (var t in Targets)
            {
                try { if (ReferenceEquals(t.Root.RenderTransform, t.Shake)) t.Root.RenderTransform = t.Prior; }
                catch (Exception ex) { Log.Debug("Deeper screen_shake restore: {E}", ex.Message); }
            }
            Targets.Clear();
            foreach (var web in WebTargets) _ = web.InvokeScriptAsync(WebStopScript);
            WebTargets.Clear();
        }
    }
}
