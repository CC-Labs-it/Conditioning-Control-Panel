using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private BeatLoop? _tabChromeBeat;
    private Control? _chromeIncoming, _chromeOutgoing;
    private List<Control>? _chromeCards;

    // WPF ChromeFx AnimateTabIn: one finite shared beat owns the outgoing 100 ms fade,
    // incoming 200 ms QuadEaseOut and capped 40 ms card stagger. No Transform.RunAsync.
    private void AnimateChromeTab(Control incoming, Control? outgoing, string from, string to)
    {
        _chromeIncoming = incoming;
        _chromeOutgoing = ReferenceEquals(incoming, outgoing) ? null : outgoing;
        incoming.IsHitTestVisible = true; // rapid back navigation must always restore input
        var level = CoreSettings.Current.MotionLevel;
        if (level == MotionLevel.Off) { FinishTabChoreography(); return; }
        if (ChromeFxNav.IsAirspaceTab(from)) _chromeOutgoing = null;
        if (_chromeOutgoing is { } leaving)
        {
            leaving.IsVisible = true;
            leaving.IsHitTestVisible = false;
        }
        var slide = level == MotionLevel.Full && !ChromeFxNav.IsAirspaceTab(to)
            ? TabTranslate(incoming) : null;
        var offset = ChromeFxNav.EntranceOffset(from, to, 12);
        if (level == MotionLevel.Full && !ChromeFxNav.IsAirspaceTab(to))
            _chromeCards = FindChromeCards(incoming);
        _tabChromeBeat = new BeatLoop(this, seconds =>
        {
            if (CoreSettings.Current.MotionLevel == MotionLevel.Off)
            {
                FinishTabChoreography();
                return;
            }
            incoming.Opacity = QuadOut(seconds / .2);
            if (slide != null)
            {
                slide.X = offset.X * (1 - QuadOut(seconds / .2));
                slide.Y = offset.Y * (1 - QuadOut(seconds / .2));
            }
            if (_chromeOutgoing is { } old)
            {
                old.Opacity = Math.Clamp(1 - seconds / .1, 0, 1);
                if (seconds >= .1)
                {
                    old.IsVisible = false;
                    old.Opacity = 1;
                    old.IsHitTestVisible = true;
                    _chromeOutgoing = null;
                }
            }
            if (_chromeCards is { } cards)
                for (int i = 0; i < cards.Count; i++)
                {
                    var card = cards[i];
                    double elapsed = seconds - .04 * Math.Min(i, 6);
                    // WPF's delayed animation leaves the authored resting value until BeginTime.
                    card.Opacity = elapsed < 0 ? 1 : QuadOut(elapsed / .22);
                    if (TabTranslate(card) is { } rise)
                        rise.Y = elapsed < 0 ? 0 : 10 * (1 - QuadOut(elapsed / .26));
                }
            if (seconds >= .62) FinishTabChoreography();
        });
        _tabChromeBeat.Start();
    }

    private static double QuadOut(double t) { t = Math.Clamp(t, 0, 1); return 1 - (1 - t) * (1 - t); }

    private void FinishTabChoreography()
    {
        _tabChromeBeat?.Stop();
        _tabChromeBeat = null;
        if (_chromeOutgoing is { } old)
        {
            old.IsVisible = false;
            old.IsHitTestVisible = true;
            old.Opacity = 1;
        }
        if (_chromeIncoming is { } incoming)
        {
            incoming.Opacity = 1;
            incoming.IsHitTestVisible = true;
            ResetChromeSlide(incoming);
        }
        if (_chromeCards is { } cards)
            foreach (var card in cards)
            {
                card.Opacity = 1;
                // Stagger owns Y only; never disturb Width or another animation's X.
                if (card.RenderTransform is TranslateTransform t) t.Y = 0;
            }
        _chromeIncoming = _chromeOutgoing = null;
        _chromeCards = null;
    }

    private static TranslateTransform? TabTranslate(Control control)
    {
        if (control.RenderTransform is TranslateTransform existing) return existing;
        // An authored scale/group/rotation is never ours, even when currently identity.
        if (control.RenderTransform is not null) return null;
        var slide = new TranslateTransform();
        control.RenderTransform = slide;
        return slide;
    }

    private static void ResetChromeSlide(Control control)
    {
        if (control.RenderTransform is TranslateTransform t) t.X = t.Y = 0;
    }

    internal static List<Control> FindChromeCards(Control? node)
    {
        for (int depth = 0; depth < 8 && node != null; depth++)
            switch (node)
            {
                case ContentControl { Content: Control content }: node = content; break;
                case Decorator { Child: { } child }: node = child; break;
                case Panel panel:
                    var cards = panel.Children.Where(c => c.IsVisible && c.Opacity > .99).ToList();
                    if (cards.Count == 1) { node = cards[0]; break; }
                    return cards.Take(8).ToList();
                default: return new();
            }
        return new();
    }
}
