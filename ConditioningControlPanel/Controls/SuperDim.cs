using System;
using System.Windows;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// Locks and dims a base option that does nothing while the feature's V2 is lit (owner,
    /// 2026-11-15). In XAML: <c>ctl:SuperDim.When="Creep"</c> on the option's row. While
    /// <see cref="SuperAccess.IsSelected"/> is true for that effect the row is faded and takes no
    /// input; it comes back the moment the V2 switch goes off. The row's value is never changed.
    /// </summary>
    public static class SuperDim
    {
        public const double DimOpacity = 0.38;

        public static readonly DependencyProperty WhenProperty = DependencyProperty.RegisterAttached(
            "When", typeof(SuperEffect?), typeof(SuperDim), new PropertyMetadata(null, OnWhenChanged));

        public static SuperEffect? GetWhen(DependencyObject d) => (SuperEffect?)d.GetValue(WhenProperty);
        public static void SetWhen(DependencyObject d, SuperEffect? value) => d.SetValue(WhenProperty, value);

        // The live handler per element, so Unloaded can drop exactly the one Loaded added.
        private static readonly DependencyProperty HandlerProperty = DependencyProperty.RegisterAttached(
            "Handler", typeof(Action<SuperEffect>), typeof(SuperDim), new PropertyMetadata(null));

        private static void OnWhenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement el) return;
            el.Loaded -= OnLoaded;
            el.Unloaded -= OnUnloaded;
            if (e.NewValue == null) { Unhook(el); el.Opacity = 1; el.IsHitTestVisible = true; return; }
            el.Loaded += OnLoaded;
            el.Unloaded += OnUnloaded;
            if (el.IsLoaded) { Hook(el); Apply(el); }
        }

        private static void OnLoaded(object sender, RoutedEventArgs e)
        {
            var el = (FrameworkElement)sender;
            Hook(el);
            Apply(el);
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e) => Unhook((FrameworkElement)sender);

        private static void Hook(FrameworkElement el)
        {
            Unhook(el);
            Action<SuperEffect> h = changed =>
            {
                if (GetWhen(el) == changed) el.Dispatcher.BeginInvoke(new Action(() => Apply(el)));
            };
            el.SetValue(HandlerProperty, h);
            SuperAccess.Changed += h;
        }

        private static void Unhook(FrameworkElement el)
        {
            if (el.GetValue(HandlerProperty) is Action<SuperEffect> h) SuperAccess.Changed -= h;
            el.ClearValue(HandlerProperty);
        }

        private static void Apply(FrameworkElement el)
        {
            bool dim = false;
            try { dim = GetWhen(el) is SuperEffect fx && SuperAccess.IsSelected(fx); }
            catch (Exception ex) { App.Logger?.Debug("SuperDim: {E}", ex.Message); }
            el.Opacity = dim ? DimOpacity : 1;
            el.IsHitTestVisible = !dim;
        }
    }
}
