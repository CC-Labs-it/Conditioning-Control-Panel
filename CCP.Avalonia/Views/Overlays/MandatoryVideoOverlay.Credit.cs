using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Video;

namespace ConditioningControlPanel.Avalonia.Views.Overlays;

/// <summary>
/// The "via Scrolller" mark on a playing mandatory video (WPF VideoService.Browser.cs:158, player.css:165-171),
/// keyed on the picked source (the URL, while VLC plays its cached file). A press on a clip from a known
/// subreddit opens that subreddit: at once on a floating window, at the clip's end (or its duration cap) on a
/// full-screen one, which shows a tick on every screen's badge meanwhile (player.js:958-961). Strict, Lockdown,
/// Chaos and DtRH refuse the link at the press and again before it opens (VideoService.Browser.cs:634-650,674-684).
/// </summary>
internal sealed partial class MandatoryVideoOverlay
{
    /// <summary>Test seam: the opener (WPF BrowserLauncher.TryOpenUrl).</summary>
    internal static Func<string, bool> CreditOpener = ExternalOpener.Open;
    private readonly List<ScrolllerCreditBadge> _creditBadges = new();
    private string? _queuedCreditPath;
    /// <summary>The picked source of the clip on screen (see <see cref="Services.MandatoryVideoScheduler.ShowingPath"/>).</summary>
    private string _source = "";

    internal static ScrolllerCreditBadge CreditFor(string path, Func<string, ScrolllerCacheMeta?>? lookup = null)
    {
        var credit = new ScrolllerCreditBadge();
        var meta = (lookup ?? ScrolllerItemIndex.MetaFor)(path);
        credit.SetShown(ScrolllerCreditRules.IsScrolllerMedia(path) || ScrolllerCreditRules.LinkFor(meta?.Sub) != null);
        return credit;
    }

    private bool CreditLinkAllowed() => !(_strict || Scheduler.IsStrict) && ScrolllerCreditRules.CanOpenLink(
        CoreSettings.Current?.StrictLockEnabled == true, global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.LockdownActive,
        global::ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.IsActive
        || global::ConditioningControlPanel.Avalonia.Views.Games.GameWindow.IsDtrhRunning);

    private static string? CreditLink(string path) => ScrolllerCreditRules.LinkFor(ScrolllerItemIndex.MetaFor(path)?.Sub);

    internal Button CreditButton(string path)
    {
        var mark = CreditFor(path);
        // WPF player.css:165-166,169-171 fades the badge and its hover over 600 ms.
        mark.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(GentleVisuals.FadeMs(600)) } };
        var label = mark.Child!;
        mark.Child = null;
        var tick = IconGlyph.Of(IconKind.Checkmark, IconVariant.Regular, null, 11);
        tick.Foreground = Brushes.White;
        tick.Margin = new Thickness(3, 0, 0, 0);
        tick.VerticalAlignment = VerticalAlignment.Center;
        tick.IsVisible = false;
        mark.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { label, tick } };
        _creditBadges.Add(mark);
        if (_queuedCreditPath == path) SetCreditQueued(mark, true);
        var clickable = CreditLink(path) != null && CreditLinkAllowed();
        // WPF player.css:169-171: no button chrome, a hand, and only the badge brightens on hover.
        // Never focusable, so the video window keeps its keys (the Esc door), as the video buttons.
        var button = new Button
        {
            Content = mark, Padding = new Thickness(0), BorderThickness = new Thickness(0), Focusable = false,
            Margin = new Thickness(0, 0, MandatoryVideoControls.MarginDip, MandatoryVideoControls.MarginDip),
            Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom, IsVisible = mark.Shown,
            IsHitTestVisible = clickable, Cursor = clickable ? new Cursor(StandardCursorType.Hand) : null,
            Template = new FuncControlTemplate<Button>((b, _) => new ContentPresenter
                { Background = Brushes.Transparent, [!ContentPresenter.ContentProperty] = b[!ContentControl.ContentProperty] }),
        };
        AutomationProperties.SetName(button, ScrolllerCreditRules.Mark);
        if (clickable)
        {
            button.PointerEntered += (_, _) => mark.Opacity = 1;
            button.PointerExited += (_, _) => mark.Opacity = ScrolllerCreditBadge.ShownOpacity;
        }
        button.Click += (_, _) => CreditPressed(path);
        return button;
    }

    internal void CreditPressed(string path)
    {
        var link = CreditLink(path);
        if (!Scheduler.IsPlaying || path != _source || link == null || !CreditLinkAllowed()) return;
        if (!Floating)
        {
            _queuedCreditPath = path;
            foreach (var badge in _creditBadges) SetCreditQueued(badge, true);
            return;
        }
        PostCreditLink(link);
    }

    private static void SetCreditQueued(ScrolllerCreditBadge badge, bool queued)
    {
        if (badge.Child is StackPanel { Children.Count: > 1 } panel && panel.Children[1] is IconGlyph tick)
            tick.IsVisible = queued;
    }

    private void ResetCreditBadges()
    {
        _queuedCreditPath = null;
        foreach (var badge in _creditBadges) SetCreditQueued(badge, false);
        _creditBadges.Clear();
    }

    internal void OpenQueuedCreditLink()
    {
        var path = _queuedCreditPath;
        _queuedCreditPath = null;
        if (!Scheduler.IsPlaying || path == null || path != _source || !CreditLinkAllowed()) return;
        if (CreditLink(path) is { } link) PostCreditLink(link);
    }

    private void PostCreditLink(string link) => Dispatcher.UIThread.Post(() =>
    {
        if (CreditLinkAllowed()) CreditOpener(link);
    });
}
