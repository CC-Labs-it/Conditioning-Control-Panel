using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ExclusivesTabView
    {
        private readonly Dictionary<Grid, (Rectangle Band, LinearGradientBrush Brush, int Index)> _signBands = new();
        private BeatLoop? _standaloneSignMotion;
        private long _signStart;

        // WPF MainWindow.Exclusives.Flair.cs:67-98: the mask uses the same sign art.
        private void Sign_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is not Grid host || host.DataContext is not ExclusiveGroupRow row || row.Sign == null || _signBands.ContainsKey(host)) return;
            var brush = new LinearGradientBrush
            {
                GradientStops = new GradientStops
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), .30),
                    new GradientStop(Color.FromArgb(0xD8, 255, 255, 255), .47),
                    new GradientStop(Color.FromArgb(0xF0, 255, 255, 255), .50),
                    new GradientStop(Color.FromArgb(0xD8, 255, 255, 255), .53),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), .70),
                }
            };
            var band = new Rectangle { Name = "SignSheen", Fill = brush, IsHitTestVisible = false,
                OpacityMask = new ImageBrush { Source = row.Sign, Stretch = Stretch.Uniform } };
            host.Children.Add(band);
            _signBands.Add(host, (band, brush, row.Group == PremiumGroup.Basic ? 0 : 1));
            PaintSign(brush, -1.2);
            if (_motion != null || _standaloneSignMotion != null)
                SignFrame(FxAdorner.Time.GetElapsedTime(_signStart).TotalSeconds);
        }

        private void Sign_Unloaded(object? sender, RoutedEventArgs e)
        {
            if (sender is Grid host && _signBands.Remove(host, out var part)) host.Children.Remove(part.Band);
        }

        private void UpdateSignMotion()
        {
            bool visible = IsVisible && _plansActive && !_plansMode && this.IsAttachedToVisualTree();
            // WPF starts sign sheens independently of the ambient performance gate.
            bool standalone = visible && AmbientFxCanvas.Env.Level != MotionLevel.Off && _motion == null;
            if (standalone && _standaloneSignMotion == null)
            {
                _signStart = FxAdorner.Time.GetTimestamp();
                _standaloneSignMotion = new BeatLoop(this, _ => SignTick());
                _standaloneSignMotion.Start();
            }
            else if (!standalone && _standaloneSignMotion != null)
            {
                _standaloneSignMotion.Stop();
                _standaloneSignMotion = null;
            }
            if (_motion != null && visible) _signStart = _motionStart;
            if (!visible || (_motion == null && _standaloneSignMotion == null))
                foreach (var part in _signBands.Values) PaintSign(part.Brush, -1.2);
        }

        internal bool SignMotionRunning => _motion != null || _standaloneSignMotion != null;

        internal void SignTick() => SignFrame(FxAdorner.Time.GetElapsedTime(_signStart).TotalSeconds);

        internal void SignFrame(double t)
        {
            bool full = AmbientFxCanvas.Env.Level == MotionLevel.Full;
            double crossing = full ? 1.1 : 2.2, period = full ? 4.2 : 9.0;
            foreach (var part in _signBands.Values)
            {
                double phase = t - (0.5 + 0.7 * part.Index);
                double x = -1.2;
                if (phase >= 0)
                {
                    double u = Math.Min(1, phase % period / crossing);
                    x += 2.4 * (1 - Math.Cos(Math.PI * u)) / 2;
                }
                PaintSign(part.Brush, x);
            }
        }

        // Translating the relative endpoints matches WPF's relative brush transform.
        private static void PaintSign(LinearGradientBrush brush, double x)
        {
            brush.StartPoint = new RelativePoint(x, 0, RelativeUnit.Relative);
            brush.EndPoint = new RelativePoint(1 + x, .35, RelativeUnit.Relative);
        }
    }
}
