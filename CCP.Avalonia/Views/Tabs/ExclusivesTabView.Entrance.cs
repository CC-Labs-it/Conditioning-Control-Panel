using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ExclusivesTabView
{
    private BeatLoop? _entrance;
    private int _entranceGeneration;

    // WPF MainWindow.Exclusives.cs:280 and MainWindow.Exclusives.Flair.cs:198.
    internal void PlayVaultEntrance()
    {
        if (_plansMode) return;
        _entrance?.Stop();
        int generation = ++_entranceGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _entranceGeneration || !IsEffectivelyVisible || _plansMode) return;
            UpdateLayout();
            var shelf = this.FindControl<ItemsControl>("ExclusivesShelf")!;
            var children = shelf.GetVisualDescendants().OfType<Border>()
                .Where(b => (b.Name == "ExCard" || b.Name == "GroupHead") && b.IsVisible).ToArray();
            var slides = children.Select(child =>
            {
                var slide = new TranslateTransform();
                if (child.Name == "ExCard")
                {
                    var scale = child.RenderTransform as ScaleTransform ??
                        (child.RenderTransform as TransformGroup)?.Children.OfType<ScaleTransform>().FirstOrDefault() ?? new ScaleTransform();
                    child.RenderTransformOrigin = RelativePoint.Center;
                    child.RenderTransform = new TransformGroup { Children = new Transforms { scale, slide } };
                }
                else child.RenderTransform = slide;
                return slide;
            }).ToArray();
            var level = AmbientFxCanvas.Env.Level;
            bool full = level == MotionLevel.Full;
            void Frame(double t)
            {
                for (int i = 0; i < children.Length; i++)
                {
                    double delay = (full ? .038 : .020) * Math.Min(i, full ? 16 : 8);
                    double p = Math.Clamp((t - delay) / (full ? .320 : .200), 0, 1);
                    children[i].Opacity = level == MotionLevel.Off ? 1 : 1 - (1 - p) * (1 - p);
                    double x = 1 - Math.Clamp((t - delay) / .420, 0, 1);
                    slides[i].Y = full ? 18 * (x * x * x - .35 * x * Math.Sin(Math.PI * x)) : 0;
                }
            }
            if (level == MotionLevel.Off) { Frame(0); return; }
            double end = (full ? .038 : .020) * Math.Min(Math.Max(0, children.Length - 1), full ? 16 : 8) + (full ? .420 : .200);
            // The finite shelf entrance is separate from the view's ambient motion loop.
            _entrance = new BeatLoop(shelf, t =>
            {
                Frame(t);
                if (t >= end) { _entrance?.Stop(); _entrance = null; }
            });
            _entrance.OnParked = () => { Frame(end); _entrance?.Stop(); _entrance = null; };
            _entrance.Start();
        }, DispatcherPriority.Loaded);
    }
}
