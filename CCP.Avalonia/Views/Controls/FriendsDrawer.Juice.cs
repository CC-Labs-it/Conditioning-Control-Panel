// WPF FriendsDrawer.Juice.cs:34-89,136-151: the open spring with staggered rows, a card or the add box
// rising in, breathing online dots and the avatar hop. Pop, sparks and shockwave are LinkBurst (Link.cs).
// Every move reads FeedMotionAmount: Full plays it, Reduced at half the size, Off leaves everything at rest.
using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services.Friends;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed partial class FriendsDrawer
{
    private readonly OrderedDictionary<string, Control> _rows = new();
    private readonly Dictionary<string, Control> _avatars = new();
    private FriendsSnapshot _last = FriendsSnapshot.Empty;
    private BeatLoop? _breath;

    /// <summary>WPF Juice.cs:34-56: up from the chip with a little overshoot, then the rows stagger in.</summary>
    private void PlayEntrance()
    {
        double k = FeedMotionAmount;
        if (k <= 0) { Opacity = 1; RenderTransform = null; return; }
        RenderTransformOrigin = new RelativePoint(0, 1, RelativeUnit.Relative);
        var (scale, slide) = (new ScaleTransform(), new TranslateTransform());
        RenderTransform = new TransformGroup { Children = { scale, slide } };
        var ease = new Theme.BackEaseOutAmplitude { Amplitude = 0.45 * k };
        double ms = 220 / Math.Max(k, 0.5) * (k < 1 ? 0.8 : 1);
        Play(this, Math.Max(ms, 160), t =>
        {
            double e = ease.Ease(Math.Min(t / ms, 1));
            scale.ScaleX = scale.ScaleY = 1 - 0.06 * k * (1 - e);
            slide.Y = 14 * k * (1 - e);
            Opacity = Math.Min(t / 160, 1);
        });
        StaggerIn(new List<Control>(_rows.Values));
    }

    /// <summary>WPF MotionFx.StaggerIn: 40 ms apart (capped at six), a 220 ms fade and a 10 px, 260 ms rise.</summary>
    private static void StaggerIn(IReadOnlyList<Control> items)
    {
        if (!AmbientFxCanvas.Env.AllowTransitions) return;
        for (int i = 0; i < items.Count; i++)
        {
            var (item, slide, delay) = (items[i], new TranslateTransform(), 40.0 * Math.Min(i, 6));
            item.RenderTransform = slide;
            Play(item, delay + 260, t =>
            {
                double p = Math.Max(0, t - delay);
                item.Opacity = QuadOut(p / 220);
                slide.Y = 10 * (1 - QuadOut(p / 260));
            });
        }
    }

    private static double QuadOut(double p) { p = Math.Clamp(p, 0, 1); return 1 - (1 - p) * (1 - p); }

    /// <summary>Steps <paramref name="step"/> (elapsed ms) on a BeatLoop and settles it at <paramref name="total"/>,
    /// also when the owner is parked or leaves the tree.</summary>
    private static void Play(Control owner, double total, Action<double> step)
    {
        if (TopLevel.GetTopLevel(owner) == null) { step(total); return; }
        BeatLoop? beat = null;
        void Settle()
        {
            if (beat is not { IsRunning: true }) return;
            beat.Stop();
            owner.DetachedFromVisualTree -= Detached;
            step(total);
        }
        void Detached(object? s, VisualTreeAttachmentEventArgs e) => Settle();
        beat = new BeatLoop(owner, s => { if (s * 1000 >= total) Settle(); else step(s * 1000); }) { OnParked = Settle };
        owner.DetachedFromVisualTree += Detached;
        beat.Start();
    }

    /// <summary>WPF Juice.cs:63-89: every online dot in the list breathes 0.45 to 1 over 1.8 s, ambient (Full only).</summary>
    private void StartAmbient()
    {
        StopAmbient();
        if (!_isOpen || !AmbientFxCanvas.Env.AllowAmbientLoops) return;
        var dots = new List<Control>();
        foreach (var av in _avatars.Values)
            if (av is Grid g) foreach (var c in g.Children) if (c is Ellipse { Tag: "friends-dot-on" } e) dots.Add(e);
        if (dots.Count == 0) return;
        _breath = new BeatLoop(this, t => { foreach (var d in dots) d.Opacity = 0.45 + 0.55 * BeatLoop.Breath(t, 1.8); });
        _breath.Start();
    }

    private void StopAmbient() { _breath?.Stop(); _breath = null; }

    /// <summary>WPF Juice.cs:136-151: a friend came online, their avatar hops once and throws 8 mint sparks.</summary>
    private void HopAvatar(string friendId)
    {
        double k = FeedMotionAmount;
        if (k <= 0 || !_avatars.TryGetValue(friendId, out var av)) return;
        var hop = new TranslateTransform();
        av.RenderTransform = hop;
        var bounce = new BounceEaseOut();
        Play(av, 560, t => hop.Y = t <= 170 ? -10 * k * QuadOut(t / 170) : -10 * k * (1 - bounce.Ease((t - 170) / 390)));
        PlayLinkFx(av, false, Mint, 8);
    }

    private void OnSnapshot(FriendsSnapshot snap)
    {
        var hop = FriendsDrawerRules.CameOnline(_last, snap);
        Render();
        // After layout, so the sparks start from the avatar's centre.
        if (hop.Count > 0) Dispatcher.UIThread.Post(() => { foreach (var id in hop) HopAvatar(id); });
    }
}
