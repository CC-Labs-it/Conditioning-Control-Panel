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
    private readonly TextBlock _movedText = new() { FontSize = 12.5, Foreground = Brushes.Gainsboro };
    private readonly Border _movedNote = new()
    {
        IsVisible = false, IsHitTestVisible = false, CornerRadius = new CornerRadius(12),
        Padding = new Thickness(12, 4), Background = new SolidColorBrush(Color.Parse("#E61B1026")),
        BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center,
    };
    private DispatcherTimer? _noteTimer;
    internal bool MovedNoteVisible => _movedNote.IsVisible;
    internal string? MovedNoteText => _movedText.Text;

    internal void HideMovedNote()
    {
        _noteTimer?.Stop();
        _movedNote.IsVisible = false;
    }

    public void ShowMovedNote(string text)
    {
        _noteTimer?.Stop();
        _movedNote.Child = _movedText;
        _movedText.Text = text;
        _movedNote.BorderBrush = _accent;
        _movedNote.Transitions = Level == MotionLevel.Off ? null : new Transitions
        {
            new DoubleTransition { Property = OpacityProperty,
                Duration = TimeSpan.FromMilliseconds(Level == MotionLevel.Reduced ? 75 : 150) },
        };
        _movedNote.Opacity = 0;
        _movedNote.IsVisible = true;
        _movedNote.Opacity = 1;
        _noteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
        _noteTimer.Tick += (_, _) =>
        {
            if (_movedNote.Opacity == 0 || Level == MotionLevel.Off) { HideMovedNote(); return; }
            _movedNote.Transitions = new Transitions
            {
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(300) },
            };
            _movedNote.Opacity = 0;
            _noteTimer.Interval = TimeSpan.FromMilliseconds(300);
        };
        _noteTimer.Start();
    }
}
