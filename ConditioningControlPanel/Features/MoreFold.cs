using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// Folds a panel's power-user rows behind one "More options" link (house rule: lead with the
    /// capability, keep the knobs one click away). The rows are this panel's children in XAML.
    /// Code-only on purpose: a UserControl with its own XAML is a namescope, and WPF then refuses
    /// x:Name on the host's rows inside it. Closed by default; the host sets <see cref="IsOpen"/>
    /// when a row inside is off its default, so a changed setting is never hidden.
    /// A click on the link plays an IN (fade + short rise) and an OUT (fade, then fold); Motion
    /// Off snaps, Gentle visuals lengthens the fade. A host setting IsOpen in code always snaps.
    /// </summary>
    public class MoreFold : StackPanel
    {
        public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
            nameof(IsOpen), typeof(bool), typeof(MoreFold),
            new PropertyMetadata(false, (d, _) => ((MoreFold)d).Apply()));

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        private readonly TextBlock _toggle;
        private readonly Run _label = new();

        public MoreFold()
        {
            var link = new Hyperlink(_label) { TextDecorations = null };
            link.SetResourceReference(TextElement.ForegroundProperty, "TextMutedBrush");
            link.Click += (_, _) =>
            {
                _animateNext = true;
                try { IsOpen = !IsOpen; }
                finally { _animateNext = false; }
            };
            link.MouseEnter += (_, _) => link.TextDecorations = TextDecorations.Underline;
            link.MouseLeave += (_, _) => link.TextDecorations = null;

            _toggle = new TextBlock(link)
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
            };
            Children.Add(_toggle);
            Apply();
        }

        protected override void OnVisualChildrenChanged(DependencyObject visualAdded, DependencyObject visualRemoved)
        {
            base.OnVisualChildrenChanged(visualAdded, visualRemoved);
            if (visualAdded is UIElement el && !ReferenceEquals(el, _toggle))
                el.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Apply()
        {
            bool animate = _animateNext && Ms(InMs) > TimeSpan.Zero;
            foreach (UIElement child in Children)
            {
                if (ReferenceEquals(child, _toggle)) continue;
                if (!animate)
                {
                    child.BeginAnimation(OpacityProperty, null);
                    child.Visibility = IsOpen ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (IsOpen) FadeIn(child);
                else FadeOut(child);
            }

            // A binding, not a string: a language switch must relabel it live.
            BindingOperations.SetBinding(_label, Run.TextProperty,
                new Binding($"[{(IsOpen ? "fold_fewer_options" : "fold_more_options")}]")
                { Source = LocalizationManager.Instance, Mode = BindingMode.OneWay });
        }

        private bool _animateNext;
        private const int InMs = 200, OutMs = 140, RiseDip = 8;

        // The same clock as OptionSection: Off is instant, Reduced halves, Gentle only lengthens (300 ms floor).
        private static TimeSpan Ms(int ms)
        {
            try { return TimeSpan.FromMilliseconds(OptionSection.DurationMs(ms, MotionFx.Level, GentleVisuals.On)); }
            catch { return TimeSpan.Zero; }
        }

        /// <summary>IN: from wherever it is (mid-OUT included) to its own opacity, rising a few DIP.
        /// FillBehavior.Stop hands the row back its own value, so a dimmed row stays dimmed.</summary>
        private static void FadeIn(UIElement child)
        {
            double from = child.Visibility == Visibility.Visible ? child.Opacity : 0;
            child.Visibility = Visibility.Visible;
            child.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                From = from,
                Duration = Ms(InMs),
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            });
            // Rise only where the row has no transform of its own.
            if (child.RenderTransform == null || child.RenderTransform == Transform.Identity)
                child.RenderTransform = new TranslateTransform();
            if (child.RenderTransform is TranslateTransform slide)
                slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(RiseDip, 0, Ms(InMs + 40))
                {
                    FillBehavior = FillBehavior.Stop,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                });
        }

        /// <summary>OUT: fade to nothing, then fold. A click back mid-fade replaces the clock, and the
        /// check on IsOpen keeps a stale finish from folding a reopened row.</summary>
        private void FadeOut(UIElement child)
        {
            if (child.Visibility != Visibility.Visible) return;
            var fade = new DoubleAnimation { To = 0, Duration = Ms(OutMs), FillBehavior = FillBehavior.HoldEnd };
            fade.Completed += (_, _) =>
            {
                if (IsOpen) return;
                child.Visibility = Visibility.Collapsed;
                child.BeginAnimation(OpacityProperty, null);
            };
            child.BeginAnimation(OpacityProperty, fade);
        }
    }
}
