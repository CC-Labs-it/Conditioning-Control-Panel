using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private sealed record Stamp(string Key, string Label, ActiveQuest? Quest, QuestDefinition? Definition, bool Weekly);
    private readonly Dictionary<string, Stamp> _stamps = new();
    private readonly Dictionary<string, bool> _stampDone = new();
    private readonly Dictionary<Grid, IDisposable> _stampTweens = new();
    private static bool StampMotion => CoreSettings.Current.MotionLevel != MotionLevel.Off;
    private static readonly double[] StampAngles = { -4, 3, -2, 5 };
    private Popup? _stampPopup;
    private string? _hoveredStamp;
    internal Popup? QuestStampDetails => _stampPopup;
    private bool _stampsClosed;
    private readonly Dictionary<string, global::Avalonia.Media.Imaging.Bitmap?> _stampArt = new();

    private void InitializeQuestStamps()
    {
        var quests = App.Quests;
        void Repaint() => Dispatcher.UIThread.Post(() => { if (!_stampsClosed) RefreshQuestStamps(); });
        void Completed(object? sender, QuestCompletedEventArgs e) => Repaint();
        void Progress(object? sender, QuestProgressEventArgs e) => Repaint();
        void Refreshed(object? sender, EventArgs e) => Repaint();
        void ModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(() =>
        { if (_stampsClosed) return; HideStampPopup(); _stampArt.Clear(); RefreshQuestStamps(); });
        void Dismiss(object? sender, EventArgs e) => HideStampPopup();
        if (quests != null)
        {
            quests.QuestCompleted += Completed;
            quests.QuestProgressChanged += Progress;
            quests.QuestsRefreshed += Refreshed;
        }
        LocalizationManager.Instance.LanguageChanged += Refreshed;
        CoreMods.ModChanged += ModChanged;
        Deactivated += Dismiss;
        PositionChanged += (_, _) => HideStampPopup();
        Closed += (_, _) =>
        {
            _stampsClosed = true;
            foreach (var timer in _stampTweens.Values) timer.Dispose();
            _stampTweens.Clear();
            HideStampPopup();
            if (quests != null)
            {
                quests.QuestCompleted -= Completed;
                quests.QuestProgressChanged -= Progress;
                quests.QuestsRefreshed -= Refreshed;
            }
            LocalizationManager.Instance.LanguageChanged -= Refreshed;
            CoreMods.ModChanged -= ModChanged;
            Deactivated -= Dismiss;
        };
        RefreshQuestStamps();
    }

    internal void RefreshQuestStamps()
    {
        foreach (var timer in _stampTweens.Values) timer.Dispose();
        _stampTweens.Clear();
        QuestStampHost.Children.Clear();
        _stamps.Clear();
        QuestStampHost.IsVisible = App.Quests != null;
        if (App.Quests is not { } quests) { HideStampPopup(); return; }
        var board = quests.GetDailySlots();
        for (int i = 0; i < 3; i++)
            AddQuestStamp(new Stamp("d" + i, $"{Loc.Get("quest_daily")} {i + 1}",
                i < board.Count ? board[i].Quest : null, i < board.Count ? board[i].Definition : null, false), i);
        AddQuestStamp(new Stamp("w", Loc.Get("quest_weekly"), quests.Progress.WeeklyQuest, quests.GetCurrentWeeklyDefinition(), true), 3);
        if (_hoveredStamp != null && QuestStampHost.IsPointerOver && _stamps.TryGetValue(_hoveredStamp, out var stamp))
            ShowStampPopup(stamp);
        else { _hoveredStamp = null; LiftStampHost(false); foreach (var child in QuestStampHost.Children) if (child is Grid cell) ZoomStamp(cell, false); HideStampPopup(); }
    }

    private static double StampFraction(Stamp stamp) => stamp.Quest?.IsCompleted == true ? 1 :
        stamp.Definition is { TargetValue: > 0 } def && stamp.Quest is { } q
            ? Math.Clamp((double)q.CurrentProgress / def.TargetValue, 0, 1) : 0;

    private void AddQuestStamp(Stamp stamp, int index)
    {
        _stamps[stamp.Key] = stamp;
        bool ghost = stamp.Quest == null || stamp.Definition == null, done = stamp.Quest?.IsCompleted == true;
        var accent = new SolidColorBrush(Color.Parse(ghost ? "#3D3D60" : done ? "#FFD700" : stamp.Weekly ? "#B57EDC" : "#FF69B4"));
        double size = stamp.Weekly ? 23 : 20;
        var cell = new Grid
        {
            Width = size, Height = size, Margin = new Thickness(index * 16, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, Opacity = ghost ? .5 : done ? .85 : 1,
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new RotateTransform(StampAngles[index]) } },
            RenderTransformOrigin = RelativePoint.Center, Tag = stamp.Key,
        };
        var art = ghost ? null : StampArt(stamp.Definition!);
        cell.Children.Add(new global::Avalonia.Controls.Shapes.Rectangle
        {
            RadiusX = 3, RadiusY = 3, Fill = art != null ? new ImageBrush(art) { Stretch = Stretch.UniformToFill }
                : new SolidColorBrush(Color.Parse(stamp.Weekly ? "#2A1F3D" : "#252542")),
            Stroke = accent, StrokeThickness = ghost ? 1 : 1.5,
            StrokeDashArray = ghost ? new global::Avalonia.Collections.AvaloniaList<double> { 2, 1.6 } : null,
        });
        if (art != null) cell.Children.Add(new Border { CornerRadius = new CornerRadius(3), IsHitTestVisible = false,
            Background = new SolidColorBrush(Color.Parse(done ? "#888A6D00" : "#2E0A0A18")) });
        if (!ghost)
        {
            IconMap.TryGet(stamp.Definition?.Icon, out var entry);
            var icon = IconGlyph.Of(done ? IconKind.Checkmark : entry.Kind,
                done ? IconVariant.Filled : entry.Variant, null, 11);
            icon.Foreground = accent;
            icon.IsVisible = done || art == null;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
            cell.Children.Add(icon);
            if (StampFraction(stamp) > 0)
                cell.Children.Add(new Border { Width = Math.Max(1, (size - 5) * StampFraction(stamp)), Height = 2.5,
                    CornerRadius = new CornerRadius(1), Background = accent, Margin = new Thickness(2.5, 0, 0, 2.5),
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom });
        }
        cell.PointerEntered += (_, _) =>
        {
            foreach (var child in QuestStampHost.Children)
                if (child is Grid previous && previous != cell) ZoomStamp(previous, false);
            HoverQuestStamp(stamp.Key);
        };
        cell.PointerExited += (_, _) =>
        {
            ZoomStamp(cell, false);
            if (_hoveredStamp == stamp.Key) _hoveredStamp = null;
            Dispatcher.UIThread.Post(() => { if (_hoveredStamp == null) HideStampPopup(); }, DispatcherPriority.Input);
        };
        QuestStampHost.Children.Add(cell);
        bool known = _stampDone.TryGetValue(stamp.Key, out var wasDone);
        _stampDone[stamp.Key] = done;
        if (done && known && !wasDone && StampMotion)
        {
            var glow = new Border { BorderBrush = Brushes.Gold, BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(3), IsHitTestVisible = false };
            cell.Children.Add(glow);
            glow.Transitions = new global::Avalonia.Animation.Transitions { new global::Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(420) } };
            Dispatcher.UIThread.Post(() => glow.Opacity = 0);
            DispatcherTimer.RunOnce(() => cell.Children.Remove(glow), TimeSpan.FromMilliseconds(420));
            TweenStamp(cell, 420, t => t < .6 ? .7 + .65 * (1 - Math.Pow(1 - t / .6, 3))
                : 1.35 - .35 * ((t - .6) / .4), _ => StampAngles[index]);
        }
        if (_hoveredStamp == stamp.Key && QuestStampHost.IsPointerOver) ZoomStamp(cell, true);
    }

    internal void HoverQuestStamp(string key)
    {
        if (!_stamps.TryGetValue(key, out var stamp)) return;
        _hoveredStamp = key;
        foreach (var child in QuestStampHost.Children)
            if (child is Grid cell) ZoomStamp(cell, cell.Tag as string == key);
        ShowStampPopup(stamp);
    }

    private global::Avalonia.Media.Imaging.Bitmap? StampArt(QuestDefinition definition)
    {
        var path = definition.EffectiveImagePath;
        if (string.IsNullOrEmpty(path)) return null;
        if (_stampArt.TryGetValue(path, out var cached)) return cached;
        global::Avalonia.Media.Imaging.Bitmap? art = null;
        try
        {
            const string prefix = "pack://application:,,,/Resources/";
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                var relative = path.Substring(prefix.Length);
                // ModArt checks the mod override before the bundled resource, as WPF does.
                if (CoreModArt.OverridePath(relative) == null)
                {
                    if ((CoreSettings.Current.IsSissyMode || App.Mods?.IsCCPDefault == true) && relative.Contains("logo.png")) relative = "logo2.png";
                    if (CoreSettings.Current.IsSissyMode && relative.Contains("bambi takeover.png")) relative = "features/mandatory_videos.png";
                }
                art = Helpers.ModArt.TryLoad(relative);
            }
            else if (System.IO.File.Exists(path)) art = new global::Avalonia.Media.Imaging.Bitmap(path);
        }
        catch { /* WPF LoadQuestImage: unusable art falls back to the quest glyph. */ }
        _stampArt[path] = art;
        return art;
    }

    private static string StampText(string localized, string fallback) => CoreMods.MakeModAware(
        string.IsNullOrWhiteSpace(localized) || localized.StartsWith("quest_", StringComparison.Ordinal) ? fallback : localized);

    private void ShowStampPopup(Stamp stamp)
    {
        _stampPopup ??= new Popup { PlacementTarget = QuestStampHost, Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = false, IsHitTestVisible = false };
        var content = new StackPanel { Spacing = 6 };
        void Text(string text, double size = 12) => content.Children.Add(new TextBlock
        {
            Text = text, FontSize = size, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
        });
        Text(stamp.Label.ToUpperInvariant());
        if (stamp.Quest is not { } q || stamp.Definition is not { } def)
        {
            Text(stamp.Weekly ? "No weekly quest yet" : "No quest in this slot", 16);
            Text("It will be here after the next roll.");
        }
        else
        {
            var artFrame = new Grid { Height = 132, Background = new SolidColorBrush(Color.Parse("#1A1A2E")) };
            if (StampArt(def) is { } art) artFrame.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill });
            if (q.IsCompleted)
            {
                var check = IconGlyph.Of(IconKind.Checkmark, IconVariant.Filled, null, 60);
                check.Foreground = Brushes.SpringGreen;
                check.HorizontalAlignment = HorizontalAlignment.Center;
                check.VerticalAlignment = VerticalAlignment.Center;
                artFrame.Children.Add(new Border { Background = new SolidColorBrush(Color.Parse("#992D4A2D")), Child = check });
            }
            content.Children.Add(artFrame);
            Text(StampText(def.LocalizedName, def.Name), 16);
            Text(StampText(def.LocalizedDescription, def.Description));
            var (xp, streak, reroll) = QuestsTabView.ComputeQuestXpDisplay(def, CoreSettings.Current);
            Text($"{xp} XP" + (streak > 0 ? $" +{streak}%" : "") + (reroll > 0 ? $" +{reroll}%" : ""));
            var track = new Grid { Width = 272, Height = 5, Background = Brushes.DimGray };
            track.Children.Add(new Border { Width = 272 * StampFraction(stamp), Background = Brushes.Gold,
                HorizontalAlignment = HorizontalAlignment.Left });
            content.Children.Add(track);
            Text($"{(q.IsCompleted ? Math.Max(q.CurrentProgress, def.TargetValue) : q.CurrentProgress)} / {def.TargetValue}");
            int left = Math.Max(0, def.TargetValue - q.CurrentProgress);
            Text(q.IsCompleted ? "done" : left > 0 ? $"{left} to go" : "");
        }
        Text(Loc.Get("tooltip_quest_stamps"), 10);
        bool entering = !_stampPopup.IsOpen;
        _stampPopup.Child = new Border { Width = 300, Padding = new Thickness(14), CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.Parse("#252542")), BorderBrush = stamp.Quest?.IsCompleted == true ? Brushes.SpringGreen : stamp.Weekly ? Brushes.MediumPurple : Brushes.HotPink,
            BorderThickness = new Thickness(1), Child = content };
        _stampPopup.IsOpen = true;
        if (entering && StampMotion && _stampPopup.Child is Border card)
        {
            card.Opacity = 0;
            card.RenderTransform = new TranslateTransform(0, -7);
            card.Transitions = new global::Avalonia.Animation.Transitions
            {
                new global::Avalonia.Animation.DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) },
            };
            ((TranslateTransform)card.RenderTransform).Transitions = new global::Avalonia.Animation.Transitions
            {
                new global::Avalonia.Animation.DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(180) },
            };
            Dispatcher.UIThread.Post(() => { card.Opacity = 1; ((TranslateTransform)card.RenderTransform).Y = 0; });
        }
    }

    // Finite UI-thread tweens: never Animation.RunAsync on a Transform, and no resident loop/effect.
    private void TweenStamp(Grid cell, int milliseconds, Func<double, double> scaleAt, Func<double, double> angleAt)
    {
        if (_stampTweens.Remove(cell, out var old)) old.Dispose();
        var group = (TransformGroup)cell.RenderTransform!;
        var scale = (ScaleTransform)group.Children[0];
        var rotate = (RotateTransform)group.Children[1];
        void Apply(double t) { scale.ScaleX = scale.ScaleY = scaleAt(t); rotate.Angle = angleAt(t); }
        Apply(0);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _stampTweens[cell] = DispatcherTimer.Run(() =>
        {
            double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / milliseconds);
            Apply(t);
            if (t < 1 && !_stampsClosed) return true;
            _stampTweens.Remove(cell);
            return false;
        }, TimeSpan.FromMilliseconds(16));
    }

    private void ZoomStamp(Grid cell, bool on)
    {
        var group = (TransformGroup)cell.RenderTransform!;
        var scale = (ScaleTransform)group.Children[0];
        var rotate = (RotateTransform)group.Children[1];
        double startScale = scale.ScaleX, startAngle = rotate.Angle;
        int index = cell.Tag as string == "w" ? 3 : int.Parse(((string)cell.Tag!)[1..]);
        double target = on ? 1.5 : 1, angle = on ? StampAngles[index] * .25 : StampAngles[index];
        cell.ZIndex = on ? 20 : 0;
        if (!StampMotion) { scale.ScaleX = scale.ScaleY = target; rotate.Angle = angle; return; }
        TweenStamp(cell, on ? 170 : 200, t => startScale + (target - startScale) * (1 - Math.Pow(1 - t, 3)),
            t => startAngle + (angle - startAngle) * (1 - Math.Pow(1 - t, 3)));
    }

    internal void HideStampPopup() { if (_stampPopup != null) _stampPopup.IsOpen = false; }

    private void QuestStamps_Click(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton != MouseButton.Left) return;
        HideStampPopup();
        ShowTab("quests");
        e.Handled = true;
    }
    private void LiftStampHost(bool on)
    {
        if (QuestStampHost.RenderTransform is not ScaleTransform scale)
            QuestStampHost.RenderTransform = scale = new ScaleTransform(1, 1);
        scale.Transitions = StampMotion ? new global::Avalonia.Animation.Transitions
        {
            new global::Avalonia.Animation.DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(150) },
            new global::Avalonia.Animation.DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(150) },
        } : null;
        scale.ScaleX = scale.ScaleY = on ? 1.02 : 1;
    }
    private void QuestStamps_MouseEnter(object? sender, PointerEventArgs e)
    { LiftStampHost(true); }
    private void QuestStamps_MouseLeave(object? sender, PointerEventArgs e) { _hoveredStamp = null; LiftStampHost(false); foreach (var child in QuestStampHost.Children) if (child is Grid cell) ZoomStamp(cell, false); HideStampPopup(); }
}
