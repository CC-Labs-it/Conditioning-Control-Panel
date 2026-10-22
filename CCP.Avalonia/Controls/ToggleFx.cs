using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media.Transformation;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// ToggleStyle's direction-dependent motion (WPF Controls.xaml:236-281), set by the theme:
    ///  * the knob: ON 340 ms BackEase(0.6), OFF 300 ms BackEase(0.35) (ToggleKnobEnter/ExitTransitions);
    ///  * the spark's opacity: ON 0 -> 0.9 at 60 ms -> 0 at 450 ms, OFF fade from wherever it is to 0 in 80 ms.
    /// Code rather than :checked styles, both found by the stepped-clock test: when :checked stops
    /// matching, the knob's position reverts before its Transitions do, so the exit set never runs;
    /// and a keyframe animation with no 0% frame starts from the BASE value, so no style can fade
    /// "from wherever it is". Here the Transitions are set before the position, and the running flash
    /// is cancelled before the fade replaces it, as WPF's SnapshotAndReplace does.
    /// </summary>
    public static class ToggleFx
    {
        public static readonly AttachedProperty<bool> IsEnabledProperty =
            AvaloniaProperty.RegisterAttached<ToggleButton, bool>("IsEnabled", typeof(ToggleFx));

        public static bool GetIsEnabled(ToggleButton t) => t.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(ToggleButton t, bool v) => t.SetValue(IsEnabledProperty, v);

        private static readonly ConditionalWeakTable<ToggleButton, CancellationTokenSource> Runs = new();

        static ToggleFx() => IsEnabledProperty.Changed.AddClassHandler<ToggleButton>((t, e) =>
        {
            t.PropertyChanged -= OnProperty;
            t.TemplateApplied -= OnTemplate;
            if (e.NewValue is not true) return;
            t.PropertyChanged += OnProperty;
            t.TemplateApplied += OnTemplate;
        });

        // WPF Trigger EnterActions also run when the template first applies with IsChecked already
        // true: the spark flashes; the knob just sits where it belongs.
        private static void OnTemplate(object? s, TemplateAppliedEventArgs e)
        {
            if (s is not ToggleButton t) return;
            bool on = t.IsChecked == true;
            Knob(t, on, animate: false);
            if (on) Spark(t, on: true);
        }

        private static void OnProperty(object? s, AvaloniaPropertyChangedEventArgs e)
        {
            if (s is not ToggleButton t || e.Property != ToggleButton.IsCheckedProperty) return;
            bool on = t.IsChecked == true;
            Knob(t, on, animate: true);
            Spark(t, on);
        }

        private static T? Part<T>(ToggleButton t, string name) where T : Control =>
            t.GetVisualDescendants().OfType<T>().FirstOrDefault(x => x.Name == name && x.TemplatedParent == t);

        private static void Knob(ToggleButton t, bool on, bool animate)
        {
            if (Part<Border>(t, "thumb") is not { } thumb) return;
            // Transitions first: Animatable records the base value when a set is attached, and the
            // position change after it is what starts the tween.
            thumb.Transitions = animate && t.TryFindResource(on ? "ToggleKnobEnterTransitions" : "ToggleKnobExitTransitions", out var set)
                ? set as Transitions : null;
            thumb.RenderTransform = TransformOperations.Parse(on ? "translateX(22px)" : "translateX(0px)");
        }

        private static void Spark(ToggleButton t, bool on)
        {
            if (Part<Ellipse>(t, "spark") is not { } spark) return;
            double from = spark.Opacity;   // before the cancel drops the running flash's value
            if (Runs.TryGetValue(t, out var old)) { old.Cancel(); Runs.Remove(t); }
            var anim = new Animation { Duration = TimeSpan.FromMilliseconds(on ? 450 : 80) };
            if (on)
            {
                anim.Children.Add(Frame(0, 0));
                anim.Children.Add(Frame(60.0 / 450, 0.9));
            }
            else anim.Children.Add(Frame(0, from));
            anim.Children.Add(Frame(1, 0));
            var cts = new CancellationTokenSource();
            Runs.AddOrUpdate(t, cts);
            _ = anim.RunAsync(spark, cts.Token);
        }

        private static KeyFrame Frame(double cue, double opacity) =>
            new() { Cue = new Cue(cue), Setters = { new Setter(Visual.OpacityProperty, opacity) } };
    }
}
