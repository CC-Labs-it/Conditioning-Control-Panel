using System;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Services.Possession;

// The visual-tree half of PossessionOffLimits (the name rule is in CCP.Core/Services/Possession/
// PossessionOffLimits.cs). Read that file's header for why containers count.
public static class PossessionOffLimitsTree
{
    /// <summary>Same ceiling the other visual-tree walks in this folder use: a card is a handful of
    /// levels deep, and an unbounded walk on every CanApply is a per-tick cost nobody notices until
    /// the room is full.</summary>
    public const int MaxDepth = 16;

    /// <summary>
    /// True when no effect may drop, hide or hit-test-kill this element (or anything it contains).
    /// A null element, or one we cannot reason about, is off limits: the failure mode of guessing
    /// wrong here is a user who cannot end their own lockdown.
    /// </summary>
    public static bool IsOffLimits(FrameworkElement? el)
    {
        if (el == null) return true;
        try
        {
            if (Possession.GetExclude(el)) return true;
            if (ContainsExcluded(el)) return true;

            for (DependencyObject? node = el; node != null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is not FrameworkElement fe) continue;
                if (Possession.GetExclude(fe)) return true;
                if (PossessionOffLimits.IsReservedName(fe.Name)) return true;
            }
        }
        catch { return true; }
        return false;
    }

    /// <summary>True when anything BELOW this node carries Possession.Exclude. The node itself does
    /// not count (that is the caller's own check) so a hand-excluded victim and a container full of
    /// them stay two separate, readable answers.</summary>
    public static bool ContainsExcluded(DependencyObject? node) => ContainsExcluded(node, 0);

    private static bool ContainsExcluded(DependencyObject? node, int depth)
    {
        if (node == null || depth > MaxDepth) return false;
        try
        {
            if (depth > 0 && node is FrameworkElement fe && Possession.GetExclude(fe)) return true;
            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
            {
                if (ContainsExcluded(VisualTreeHelper.GetChild(node, i), depth + 1)) return true;
            }
        }
        catch { }
        return false;
    }
}
