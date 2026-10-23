using System;
using System.Collections.Generic;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>WPF ToolTipService.ShowDuration for ordinary help-button tooltips.
/// Interactive HelpPopover cards clear ToolTip.Tip and keep their own pin/hover lifetime.</summary>
public sealed class ToolTipLifetime : AvaloniaObject
{
    public static readonly AttachedProperty<int> DurationProperty =
        AvaloniaProperty.RegisterAttached<ToolTipLifetime, Control, int>("Duration");
    public static int GetDuration(Control owner) => owner.GetValue(DurationProperty);
    public static void SetDuration(Control owner, int value) => owner.SetValue(DurationProperty, value);

    internal static TimeProvider Time = TimeProvider.System;
    private static readonly Dictionary<Control, Lease> Open = new();

    static ToolTipLifetime()
    {
        ToolTip.IsOpenProperty.Changed.AddClassHandler<Control>((owner, _) => Update(owner));
        DurationProperty.Changed.AddClassHandler<Control>((owner, _) => Update(owner));
    }

    private static void Update(Control owner)
    {
        if (Open.Remove(owner, out var old)) old.Dispose();
        if (GetDuration(owner) <= 0 || !ToolTip.GetIsOpen(owner)) return;
        var lease = new Lease(owner);
        Open.Add(owner, lease);
        lease.Timer = Time.CreateTimer(_ => Dispatcher.UIThread.Post(() =>
        {
            // A queued callback from a closed tooltip must never close a later opening.
            if (Open.TryGetValue(owner, out var current) && ReferenceEquals(current, lease))
                ToolTip.SetIsOpen(owner, false);
        }), null, TimeSpan.FromMilliseconds(GetDuration(owner)), Timeout.InfiniteTimeSpan);
    }

    internal static void CloseAll()
    {
        foreach (var owner in new List<Control>(Open.Keys)) ToolTip.SetIsOpen(owner, false);
    }

    private sealed class Lease : IDisposable
    {
        private readonly Control _owner;
        internal ITimer? Timer;
        internal Lease(Control owner)
        {
            _owner = owner;
            owner.DetachedFromVisualTree += Detached;
        }
        private void Detached(object? sender, VisualTreeAttachmentEventArgs e) => ToolTip.SetIsOpen(_owner, false);
        public void Dispose()
        {
            Timer?.Dispose();
            _owner.DetachedFromVisualTree -= Detached;
        }
    }
}
