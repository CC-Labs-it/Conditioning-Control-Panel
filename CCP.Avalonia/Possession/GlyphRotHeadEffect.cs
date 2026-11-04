using System;
using System.Collections.Generic;
using Avalonia.Controls;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession;

// WPF Effects/GlyphRotEffect.cs:26-80, :83-185, :199-260. Attribution remains in part B.
internal sealed class GlyphRotHeadEffect : HeadEffect
{
    private static readonly PossessionRole[] TargetRoles =
        { PossessionRole.Label, PossessionRole.Title, PossessionRole.TabHeader };
    private static readonly char[] Boxes = { '▯', '▮', '□', '▪', '◻', '▉', '▒' };
    private static readonly char[] Marks = { '̀', '́', '̰', '̶' };
    private static readonly Dictionary<char, char[]> LookAlikes = new()
    {
        ['a'] = new[] { 'а', '@' }, ['e'] = new[] { 'е', '3' }, ['o'] = new[] { 'о', '0' },
        ['i'] = new[] { 'і', '1' }, ['s'] = new[] { 'ѕ', '5' }, ['c'] = new[] { 'с' },
        ['n'] = new[] { 'п' }, ['t'] = new[] { 'т', '7' }, ['r'] = new[] { 'г' },
        ['y'] = new[] { 'у' }, ['h'] = new[] { 'һ' }, ['m'] = new[] { 'м' },
    };

    private TextBlock? _text;
    private string? _original, _written;
    private string[]? _cells;
    private int[]? _order;
    private int _step;
    private double _nextAt, _rotHold;

    public override string Id => "glyphrot";
    public override PossessionRung MinRung => PossessionRung.Melt;
    public override PossessionIntensity MinIntensity => PossessionIntensity.Eerie;
    public override double Weight => 2;
    public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(3500);
    public override IReadOnlyList<PossessionRole> Roles => TargetRoles;
    internal override bool UsesTransform => false;
    internal override double UndoMs => 0;

    internal override bool CanApply(Control target)
    {
        var tb = TypoHeadEffect.FindText(target);
        return TypoHeadEffect.IsRewritable(tb, 3) && Words(tb!.Text!).Count > 0;
    }

    protected override void Start()
    {
        _text = TypoHeadEffect.FindText(Element)!;
        _original = _written = _text.Text!;
        var words = Words(_original);
        var (start, end) = words[Rng.Next(words.Count)];
        _cells = new string[_original.Length];
        for (int i = 0; i < _cells.Length; i++) _cells[i] = _original[i].ToString();
        _order = new int[end - start];
        for (int i = 0; i < _order.Length; i++) _order[i] = start + i;
        for (int i = _order.Length - 1; i > 0; i--)
        {
            int j = Rng.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }
        _step = 0;
        _nextAt = 60;
        _rotHold = Photosafe ? 1680 : 1200;
    }

    internal override void Frame(double ms)
    {
        if (_text == null || _original == null || _cells == null || _order == null) return;
        int count = _order.Length;
        while (_step < count * 2 && ms >= _nextAt)
        {
            bool healing = _step >= count;
            int i = healing ? _order[count * 2 - 1 - _step] : _order[_step];
            _cells[i] = healing ? _original[i].ToString() : Rot(_original[i]);
            // WPF checks ownership on every paint, not just on undo.
            if (_text.Text == _written)
            {
                _written = string.Concat(_cells);
                _text.Text = _written;
            }
            _step++;
            _nextAt += 60 + (_step == count ? _rotHold : 0);
        }
    }

    internal override void Release()
    {
        if (_text != null && _original != null && _written != null && _text.Text == _written)
            _text.Text = _original;
        _text = null;
        _original = _written = null;
        _cells = null;
        _order = null;
    }

    private string Rot(char c)
    {
        char lower = char.ToLowerInvariant(c);
        char result = LookAlikes.TryGetValue(lower, out var options) && Rng.Next(100) < 55
            ? options[Rng.Next(options.Length)] : Boxes[Rng.Next(Boxes.Length)];
        return Rng.Next(100) < 30 ? result.ToString() + Marks[Rng.Next(Marks.Length)] : result.ToString();
    }

    private static List<(int Start, int End)> Words(string text)
    {
        var words = new List<(int, int)>();
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && !char.IsLetter(text[i])) i++;
            int start = i;
            while (i < text.Length && char.IsLetter(text[i])) i++;
            if (i - start >= 3) words.Add((start, i));
        }
        return words;
    }
}
