using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public partial class SectionTabStrip
{
    private readonly TextBlock _movedText = new() { FontSize = 12.5, FontWeight = FontWeight.SemiBold };
    private readonly Border _movedNote = new()
    {
        IsVisible = false, IsHitTestVisible = false, CornerRadius = new CornerRadius(12),
        Padding = new Thickness(12, 4), Background = new SolidColorBrush(Color.Parse("#E61B1026")),
        BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
    };
    private DispatcherTimer? _noteTimer;
    internal Border MovedNoteHost => _movedNote;
    internal DispatcherTimer? MovedNoteTimer => _noteTimer;
    internal bool MovedNoteVisible => _movedNote.IsVisible;
    internal string? MovedNoteText => _movedText.Text;

    internal void HideMovedNote()
    {
        _noteTimer?.Stop();
        _noteTimer = null;
        _movedNote.Transitions = null;
        _movedNote.Opacity = 0;
        _movedNote.IsVisible = false;
    }

    public void ShowMovedNote(string text)
    {
        // Reset without transitions before installing the new fade, including repeat shows.
        HideMovedNote();
        _movedNote.Child = _movedText;
        _movedText.Text = text;
        var hue = NavStripRules.Accent(_section);
        _movedNote.BorderBrush = new SolidColorBrush(hue);
        _movedText.Foreground = new SolidColorBrush(NavStripRules.WithAlpha(hue, 0.90));
        _movedNote.Transitions = Level == MotionLevel.Off ? null : new Transitions
        {
            new DoubleTransition { Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(Level == MotionLevel.Reduced ? 75 : 150) },
        };
        _movedNote.IsVisible = true;
        _movedNote.Opacity = 1;
        var timer = _noteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
        bool fading = false;
        timer.Tick += (_, _) =>
        {
            if (!ReferenceEquals(_noteTimer, timer)) return;
            if (fading || Level == MotionLevel.Off) { HideMovedNote(); return; }
            fading = true;
            _movedNote.Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) },
            };
            _movedNote.Opacity = 0;
            timer.Interval = TimeSpan.FromMilliseconds(300);
        };
        timer.Start();
    }
}
