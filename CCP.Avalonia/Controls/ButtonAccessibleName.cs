using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.LogicalTree;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// The accessible name of a button whose content is a control. Avalonia's
    /// ContentControlAutomationPeer falls back to <c>Content.ToString()</c> when it finds no text,
    /// so a button holding a panel was read out as "Avalonia.Controls.StackPanel" (and a TextBlock
    /// behind an unnamed template presenter as "Avalonia.Controls.TextBlock"). WPF's peer reads the
    /// text instead (ButtonBaseAutomationPeer). Installed once for every Button, ToggleButton and
    /// RadioButton: the button is labelled by its first TextBlock (live, the peer reads its text on
    /// demand); an icon-only button is named by its string tooltip. A name set in XAML wins: both are
    /// Style priority.
    /// </summary>
    internal static class ButtonAccessibleName
    {
        private static bool _installed;
        private static readonly ConditionalWeakTable<Button, IDisposable> TipBindings = new();

        internal static void Install()
        {
            if (_installed) return;
            _installed = true;
            // Content first (a TextBlock is known at once); Loaded again, once a panel has its children.
            ContentControl.ContentProperty.Changed.AddClassHandler<Button>((b, _) => Apply(b));
            Control.LoadedEvent.AddClassHandler<Button>((b, _) => Apply(b));
        }

        private static void Apply(Button b)
        {
            // A string (or nothing) names itself: undo whatever an earlier control content set.
            var content = b.Content as Control;
            var label = content as TextBlock ?? content?.GetLogicalDescendants().OfType<TextBlock>().FirstOrDefault();
            b.SetValue(AutomationProperties.LabeledByProperty, label, BindingPriority.Style);
            var iconOnly = content != null && label is null;
            if (iconOnly && !TipBindings.TryGetValue(b, out _))
                TipBindings.Add(b, b.Bind(AutomationProperties.NameProperty,
                    b.GetObservable(ToolTip.TipProperty, t => t as string), BindingPriority.Style));
            else if (!iconOnly && TipBindings.TryGetValue(b, out var tip))
            {
                tip.Dispose();   // a label arrived (panel filled after Content was set): it names the button
                TipBindings.Remove(b);
            }
        }
    }
}
