using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Avalonia.Controls.Super;

/// <summary>
/// WPF Controls/SuperDim: locks and dims a base option that does nothing while the feature's V2 is
/// lit. In XAML: <c>super:SuperDim.When="Creep"</c> on the row. While <see cref="SuperAccess.IsSelected"/>
/// holds for that effect the row is faded and takes no input; the value is never changed. WPF 7.4.1
/// marks no base row with it (its only user is the Whirlpool "New spiral every" row, which borrows
/// <see cref="DimOpacity"/> by hand), so neither does this head; the helper is here for parity.
/// </summary>
public sealed class SuperDim
{
    public const double DimOpacity = 0.38;

    private SuperDim() { }

    public static readonly AttachedProperty<SuperEffect?> WhenProperty =
        AvaloniaProperty.RegisterAttached<SuperDim, Control, SuperEffect?>("When");

    private static readonly AttachedProperty<Action<SuperEffect>?> HandlerProperty =
        AvaloniaProperty.RegisterAttached<SuperDim, Control, Action<SuperEffect>?>("Handler");

    public static SuperEffect? GetWhen(Control c) => c.GetValue(WhenProperty);
    public static void SetWhen(Control c, SuperEffect? value) => c.SetValue(WhenProperty, value);

    static SuperDim()
    {
        WhenProperty.Changed.AddClassHandler<Control>((c, e) =>
        {
            c.AttachedToVisualTree -= OnAttached;
            c.DetachedFromVisualTree -= OnDetached;
            Unhook(c);
            if (e.NewValue is null) { c.Opacity = 1; c.IsHitTestVisible = true; return; }
            c.AttachedToVisualTree += OnAttached;
            c.DetachedFromVisualTree += OnDetached;
            if (TopLevel.GetTopLevel(c) != null) { Hook(c); Apply(c); }
        });
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Control c) return;
        Hook(c);
        Apply(c);
    }

    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control c) Unhook(c);
    }

    private static void Hook(Control c)
    {
        Unhook(c);
        Action<SuperEffect> h = changed => { if (GetWhen(c) == changed) Dispatcher.UIThread.Post(() => Apply(c)); };
        c.SetValue(HandlerProperty, h);
        SuperAccess.Changed += h;
    }

    private static void Unhook(Control c)
    {
        if (c.GetValue(HandlerProperty) is { } h) SuperAccess.Changed -= h;
        c.ClearValue(HandlerProperty);
    }

    internal static void Apply(Control c)
    {
        bool dim = false;
        try { dim = GetWhen(c) is SuperEffect fx && SuperAccess.IsSelected(fx); } catch { }
        c.Opacity = dim ? DimOpacity : 1;
        c.IsHitTestVisible = !dim;
    }
}
