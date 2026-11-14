// Receipt print and chevron: WPF ChasterTabView.Fx.cs:810-897.
using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        internal int BillGeneration { get; private set; }
        private CancellationTokenSource? _billAnimation;

        private static KeyFrame BillKey(double cue, AvaloniaProperty property, double value) =>
            new() { Cue = new Cue(cue), Setters = { new Setter(property, value) } };

        private static Animation BillTween(AvaloniaProperty property, double from, double to, int ms, Easing easing)
        {
            var animation = new Animation { Duration = TimeSpan.FromMilliseconds(ms), Easing = easing, FillMode = FillMode.Forward };
            animation.Children.Add(BillKey(0, property, from));
            animation.Children.Add(BillKey(1, property, to));
            return animation;
        }

        private async void FxBill(bool open)
        {
            var generation = ++BillGeneration;
            var from = ReceiptHost.IsVisible ? ReceiptHost.Bounds.Height : 0;
            var opacity = ReceiptHost.IsVisible ? ReceiptHost.Opacity : 0;
            _billAnimation?.Cancel();
            _billAnimation?.Dispose();
            _billAnimation = new CancellationTokenSource();
            var token = _billAnimation.Token;
            var turn = TxtRunChevron.RenderTransform as RotateTransform;
            if (turn == null) TxtRunChevron.RenderTransform = turn = new RotateTransform();
            var angle = turn.Angle;
            var targetAngle = open ? 180 : 0;
            var settle = ReceiptHost.RenderTransform as TranslateTransform;
            if (settle == null) ReceiptHost.RenderTransform = settle = new TranslateTransform();

            if (!AmbientFxCanvas.Env.AllowTransitions)
            {
                ReceiptHost.Height = double.NaN;
                ReceiptHost.Opacity = 1;
                ReceiptHost.IsVisible = open;
                settle.Y = 0;
                turn.Angle = targetAngle;
                return;
            }

            var ms = open ? 420 : 260;
            _ = BillTween(RotateTransform.AngleProperty, angle, targetAngle, ms, new CubicEaseOut()).RunAsync(TxtRunChevron, token);
            turn.Angle = targetAngle;
            if (open)
            {
                ReceiptHost.Height = double.NaN;
                ReceiptHost.IsVisible = true;
                var width = (ReceiptHost.Parent as Control)?.Bounds.Width ?? 0;
                ReceiptHost.Measure(new Size(width > 0 ? width : double.PositiveInfinity, double.PositiveInfinity));
            }
            var to = open ? Math.Max(0, ReceiptHost.DesiredSize.Height - ReceiptHost.Margin.Top - ReceiptHost.Margin.Bottom) : 0;
            if (!open && from <= 0)
            {
                ReceiptHost.IsVisible = false;
                return;
            }
            ReceiptHost.Height = from;
            // Gentle visuals: the print's fade-in never rises faster than the 300 ms floor.
            _ = BillTween(OpacityProperty, opacity, open ? 1 : 0, open ? ConditioningControlPanel.Services.Safety.GentleVisuals.RiseMs(231) : ms, new LinearEasing()).RunAsync(ReceiptHost, token);
            if (open)
                _ = BillTween(TranslateTransform.YProperty, -10, 0, ms, new BackEaseOut()).RunAsync(ReceiptHost, token);
            try
            {
                await BillTween(HeightProperty, from, to, ms, open ? new CubicEaseOut() : new CubicEaseIn()).RunAsync(ReceiptHost, token);
            }
            catch (OperationCanceledException) { }
            FinishBill(generation, open);
        }

        internal void FinishBill(int generation, bool open)
        {
            if (generation != BillGeneration) return;
            var settle = (TranslateTransform)ReceiptHost.RenderTransform!;
            ReceiptHost.Height = double.NaN;
            ReceiptHost.Opacity = 1;
            settle.Y = 0;
            ReceiptHost.IsVisible = open;
        }
    }
}
