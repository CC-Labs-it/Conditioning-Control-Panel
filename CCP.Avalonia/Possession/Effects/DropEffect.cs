// Target safety for WPF Services/Possession/Effects/DropEffect.cs:61-69 and
// PossessionEffectBase.cs:271-310. The animation and catalog entry remain part B.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

internal static class DropEffect
{
    internal static TextBlock? FindTarget(PossessionTarget? target)
    {
        if (target == null || (target.Role != PossessionRole.TabHeader &&
            target.Role != PossessionRole.Title && target.Role != PossessionRole.Label)) return null;
        var text = FindText(target.Visual as Visual, 0);
        if (text == null || BindingOperations.GetBindingExpressionBase(text, TextBlock.TextProperty) != null)
            return null;
        var value = text.Text;
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4 || value.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            return null;
        if (text.Inlines is { Count: > 1 } ||
            (text.Inlines is { Count: 1 } inlines && inlines[0] is not Run)) return null;
        for (int i = 1; i < value.Length; i++)
            if (!char.IsWhiteSpace(value[i])) return text;
        return null;
    }

    private static TextBlock? FindText(Visual? root, int depth)
    {
        if (root == null || depth > 12) return null;
        if (root is TextBlock text) return text;
        foreach (var child in root.GetVisualChildren())
            if (FindText(child, depth + 1) is { } found) return found;
        return null;
    }
}
