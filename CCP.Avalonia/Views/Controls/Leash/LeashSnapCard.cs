// WPF Controls/Leash/LeashSnapCard.cs:20-146 (leashed side; LeashSurfaces.ShowAsk :205-209 opens it on accept).
// Missing: the sparks, the Thud scale-bounce on the avatar and the heart tag swing.
using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using F = ConditioningControlPanel.Avalonia.Views.Controls.FriendsDrawer;
namespace ConditioningControlPanel.Avalonia.Views.Controls.Leash;
/// <summary>THE SNAP: the two avatars, the chain shooting across, "Leashed." and both recaps.</summary>
public sealed class LeashSnapCard : Border
{
    private readonly Canvas _chain = new() { Width = 190, Height = 24, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Tag = "leash-snap-chain" };
    private readonly TextBlock _title;
    public event Action? Closed;
    internal LeashSnapCard(string holder, string leashed)
    {
        Width = 380;
        Background = LeashAskCard.Card;
        BorderBrush = LeashAskCard.GoldDeep;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(18);
        Padding = new Thickness(18);
        Tag = "leash-snap";
        for (var i = 0; i < 10; i++)
        {
            var link = new Ellipse { Width = 24, Height = 13, Stroke = F.Gold, StrokeThickness = 2 };
            Canvas.SetLeft(link, i * 18.5); Canvas.SetTop(link, 5); _chain.Children.Add(link);
        }
        var row = new Grid { Height = 84, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        row.Children.Add(F.Avatar(holder, 68, null));
        Grid.SetColumn(_chain, 1); row.Children.Add(_chain);
        var you = F.Avatar(leashed, 68, null); Grid.SetColumn(you, 2); row.Children.Add(you);
        _title = new TextBlock { Text = Loc.Get("leash_snap_title"), FontSize = 26, FontFamily = F.Display, FontWeight = FontWeight.SemiBold, Foreground = F.Gold,
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0), Tag = "leash-snap-title" };
        var recap = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0),
            Children = { Recap(Loc.GetF("leash_snap_holder", holder), F.Gold), Recap(Loc.GetF("leash_snap_leashed", leashed), F.Mint) } };
        var ok = F.Pill(new TextBlock { Text = Loc.Get("leash_snap_ok"), FontFamily = F.Display, FontSize = 15 }, F.Pink, F.Text, "leash-snap-ok", F.Pink);
        ok.HorizontalAlignment = HorizontalAlignment.Stretch;
        ok.HorizontalContentAlignment = HorizontalAlignment.Center;
        ok.Padding = new Thickness(12, 9);
        ok.Margin = new Thickness(0, 16, 0, 0);
        ok.Click += (_, _) => Closed?.Invoke();
        Child = new StackPanel { Children = { row, _title, recap, ok } };
    }
    private static Control Recap(string text, IBrush fg) => new Border
    {
        Background = F.Raised, CornerRadius = new CornerRadius(10), Padding = new Thickness(10), Margin = new Thickness(3),
        Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = fg, TextWrapping = TextWrapping.Wrap },
    };
    /// <summary>WPF Play (:116-146): the snap cue, then the chain grows on the Thud curve and the title lands after 300 ms.
    /// Motion Off: the chain is simply there.</summary>
    internal void Play()
    {
        LeashSound.Snap(DateTime.UtcNow);
        if (CoreSettings.Current.MotionLevel == Models.MotionLevel.Off) return;
        var scale = new ScaleTransform(0, 1) { Transitions = new Transitions { new DoubleTransition
            { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(340), Easing = new SplineEasing(0.2, 1.5, 0.4, 1) } } };
        _chain.RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
        _chain.RenderTransform = scale;
        _title.Opacity = 0;
        Dispatcher.UIThread.Post(() => scale.ScaleX = 1);
        DispatcherTimer.RunOnce(() => _title.Opacity = 1, TimeSpan.FromMilliseconds(300));
    }
}
