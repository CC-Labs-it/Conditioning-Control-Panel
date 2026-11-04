using System;
using System.Collections.Generic;
using Avalonia.Controls;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Effects;

namespace ConditioningControlPanel.Avalonia.Possession;

// WPF Effects/RewriteEffect.cs:20-60, 63-132. The director attribution and bark remain unported.
internal sealed class RewriteHeadEffect : HeadEffect
{
    private static readonly PossessionRole[] TargetRoles =
        { PossessionRole.Label, PossessionRole.Title, PossessionRole.TabHeader, PossessionRole.Button };
    private ContentControl? _content;
    private object? _originalContent;
    private TextBlock? _text;
    private string? _originalText, _written;

    public override string Id => "rewrite";
    public override PossessionRung MinRung => PossessionRung.Drift;
    public override double Weight => 4;
    public override TimeSpan HoldFor => TimeSpan.FromSeconds(3);
    public override IReadOnlyList<PossessionRole> Roles => TargetRoles;
    internal override bool UsesTransform => false;
    internal override double UndoMs => 0;

    internal override bool CanApply(Control target)
    {
        if (target is ContentControl cc && cc.Content is string s && !string.IsNullOrWhiteSpace(s))
            return RewritePools.Rewrite(s, RewritePools.ActiveModId, Rng) != null;
        var tb = TypoHeadEffect.FindText(target);
        return TypoHeadEffect.IsRewritable(tb, 2)
            && RewritePools.Rewrite(tb!.Text, RewritePools.ActiveModId, Rng) != null;
    }

    protected override void Start()
    {
        if (Element is ContentControl cc && cc.Content is string s)
        {
            var line = RewritePools.Rewrite(s, RewritePools.ActiveModId, Rng);
            if (line == null || line == s) return;
            _content = cc;
            _originalContent = cc.Content;
            _written = line;
            cc.Content = line;
            return;
        }

        var tb = TypoHeadEffect.FindText(Element);
        if (!TypoHeadEffect.IsRewritable(tb, 2)) return;
        var rewritten = RewritePools.Rewrite(tb!.Text, RewritePools.ActiveModId, Rng);
        if (rewritten == null || rewritten == tb.Text) return;
        _text = tb;
        _originalText = tb.Text;
        _written = rewritten;
        tb.Text = rewritten;
    }

    internal override void Frame(double ms) { }

    internal override void Release()
    {
        if (_content != null && _originalContent != null && _written != null
            && _content.Content is string current && current == _written)
            _content.Content = _originalContent;
        if (_text != null && _originalText != null && _written != null && _text.Text == _written)
            _text.Text = _originalText;
        _content = null;
        _text = null;
        _originalContent = null;
        _originalText = _written = null;
    }
}
