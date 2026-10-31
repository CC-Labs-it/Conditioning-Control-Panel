using System;
using System.Collections.Generic;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

// WPF Services/Possession/Effects/TypoEffect.cs:19-39, 99-146: same roles, weights,
// four-second hold and single look-alike/trade, leaving the first character alone.
internal sealed class TypoHeadEffect : HeadEffect
{
    private static readonly PossessionRole[] TargetRoles =
        { PossessionRole.Label, PossessionRole.TabHeader, PossessionRole.Title };
    private static readonly (char From, char To)[] LookAlikes =
    {
        ('o', '0'), ('O', '0'), ('l', '1'), ('I', '1'), ('e', '3'), ('E', '3'),
        ('a', '4'), ('A', '4'), ('s', '5'), ('S', '5'), ('t', '7'), ('g', '9'),
    };
    private TextBlock? _text;
    private string? _original, _written;
    public override string Id => "typo";
    public override PossessionRung MinRung => PossessionRung.Settle;
    public override double Weight => 3;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(4);
    public override IReadOnlyList<PossessionRole> Roles => TargetRoles;
    internal override double UndoMs => 0;

    // WPF PossessionEffectBase.cs:271-307: first TextBlock, unbound, at least three chars,
    // no rich inlines. Never write timer text (the role gate is in PossessionRuntime.TryApply).
    internal override bool CanApply(Control target)
    {
        var tb = FindText(target);
        return tb != null && BindingOperations.GetBindingExpressionBase(tb, TextBlock.TextProperty) == null
            && !string.IsNullOrWhiteSpace(tb.Text) && tb.Text.Length >= 3
            && (tb.Inlines == null || tb.Inlines.Count == 0 || tb.Inlines.Count == 1 && tb.Inlines[0] is Run)
            && Candidates(tb.Text).Count > 0;
    }

    private static TextBlock? FindText(Visual root, int depth = 0)
    {
        if (depth > 12) return null;
        if (root is TextBlock tb) return tb;
        foreach (var child in root.GetVisualChildren())
            if (FindText(child, depth + 1) is { } found) return found;
        return null;
    }

    // WPF TypoEffect.cs:102-145: swaps have 65 percent preference when both choices exist.
    private static List<(int Index, char? To)> Candidates(string text)
    {
        var result = new List<(int, char?)>();
        for (int i = 1; i < text.Length; i++)
        {
            foreach (var (from, to) in LookAlikes) if (text[i] == from) result.Add((i, to));
            if (i + 1 < text.Length && char.IsLetter(text[i]) && char.IsLetter(text[i + 1]) && text[i] != text[i + 1])
                result.Add((i, null));
        }
        return result;
    }

    protected override void Start()
    {
        _text = FindText(Lease.Element);
        _original = _text!.Text!;
        var candidates = Candidates(_original);
        var swaps = candidates.FindAll(c => c.To != null);
        var trades = candidates.FindAll(c => c.To == null);
        var pool = swaps.Count > 0 && (trades.Count == 0 || Rng.Next(100) < 65) ? swaps : trades;
        var pick = pool[Rng.Next(pool.Count)];
        var text = new StringBuilder(_original);
        if (pick.To is { } to) text[pick.Index] = to;
        else (text[pick.Index], text[pick.Index + 1]) = (text[pick.Index + 1], text[pick.Index]);
        _written = text.ToString();
        _text.Text = _written;
    }

    internal override void Frame(double ms) { }

    // WPF TypoEffect.cs:81-97: restore only our exact write, never overwrite a newer counter/label.
    internal override void Release()
    {
        if (_text != null && _original != null && _written != null && _text.Text == _written)
            _text.Text = _original;
        _text = null;
        _original = _written = null;
    }
}
