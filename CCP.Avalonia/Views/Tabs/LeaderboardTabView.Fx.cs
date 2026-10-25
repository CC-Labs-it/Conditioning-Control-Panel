using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class LeaderboardTabView
{
    // WPF MainWindow.LeaderboardFx.cs: only the three podium cards enter; never animate the virtualized roster.
    private readonly List<Control> _podiumSlots = new();
    private readonly List<(ListBoxItem Row, object? Item, SolidColorBrush Brush)> _rankWashes = new();
    private BeatLoop? _podiumBreath, _podiumEntrance, _rankFlash;
    private Border? _goldGlow;
    private Window? _fxWindow;
    private bool _rankFlashPending;

    private void InitializeLeaderboardFx()
    {
        AttachedToVisualTree += (_, _) =>
        {
            _fxWindow = TopLevel.GetTopLevel(this) as Window;
            if (_fxWindow != null)
            {
                _fxWindow.PropertyChanged += OnFxWindowPropertyChanged;
                _fxWindow.Closed += OnFxWindowClosed;
            }
            AmbientFxCanvas.Env.MotionGateChanged += ApplyLeaderboardFxGate;
            RunLeaderboardFxPass(null);
        };
        DetachedFromVisualTree += (_, _) => ReleaseLeaderboardFx();
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty) return;
            if (!IsVisible) { ClearRankFlashes(); StopPodiumEntrance(); }
            ApplyLeaderboardFxGate();
        };
    }

    private void OnFxWindowClosed(object? sender, EventArgs e) => ReleaseLeaderboardFx();

    private void ReleaseLeaderboardFx()
    {
        if (_fxWindow != null)
        {
            _fxWindow.PropertyChanged -= OnFxWindowPropertyChanged;
            _fxWindow.Closed -= OnFxWindowClosed;
        }
        AmbientFxCanvas.Env.MotionGateChanged -= ApplyLeaderboardFxGate;
        _fxWindow = null;
        ClearPodiumFx();
        ClearRankFlashes();
    }

    private void OnFxWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.IsActiveProperty || e.Property == Window.WindowStateProperty)
            ApplyLeaderboardFxGate();
    }

    private void RunLeaderboardFxPass(IReadOnlyList<LeaderboardRow>? moved)
    {
        ClearPodiumFx();
        // Containers only exist after layout. Called after ItemsSource was replaced, as WPF does.
        if (_fxWindow == null || !IsEffectivelyVisible) return;
        _podium.UpdateLayout();
        _podiumSlots.AddRange(_podium.GetVisualDescendants().OfType<Grid>()
            .Where(g => g.Classes.Contains("lbpodium-slot")));
        _goldGlow = _podium.GetVisualDescendants().OfType<Border>()
            .FirstOrDefault(b => b.Name == "PodiumGoldGlow" && b.DataContext is LeaderboardRow { Rank: 1 });
        // WPF template blur 26, capped per tier (MainWindow.LeaderboardFx.cs:211).
        if (_goldGlow != null)
            _goldGlow.BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = Math.Min(26, AmbientFxCanvas.Env.MaxGlowBlurRadius(AmbientFxCanvas.Env.CurrentTier)),
                Color = Color.FromUInt32(0xFFFFD700),
            });
        if (_podiumSlots.Count > 0 && AmbientFxCanvas.Env.AllowTransitions)
        {
            foreach (var slot in _podiumSlots) slot.RenderTransform = new TranslateTransform();
            _podiumEntrance = new BeatLoop(this, t =>
            {
                for (var i = 0; i < _podiumSlots.Count; i++)
                {
                    var elapsed = Math.Max(0, t - .04 * i);
                    _podiumSlots[i].Opacity = 1 - Math.Pow(1 - Math.Clamp(elapsed / .22, 0, 1), 2);
                    ((TranslateTransform)_podiumSlots[i].RenderTransform!).Y =
                        10 * Math.Pow(1 - Math.Clamp(elapsed / .26, 0, 1), 2);
                }
                if (t >= .26 + .04 * (_podiumSlots.Count - 1)) StopPodiumEntrance();
            });
            _podiumEntrance.Start();
        }
        ApplyLeaderboardFxGate();
        if (moved?.Count > 0 && AmbientFxCanvas.Env.AllowTransitions) FlashMovedRows(moved);
    }

    private void ApplyLeaderboardFxGate()
    {
        if (!AmbientFxCanvas.Env.AllowTransitions) { StopPodiumEntrance(); ClearRankFlashes(); }
        var glow = _goldGlow;
        if (glow == null) return;
        // WPF ReleasePodiumGlow hands back the template's static .28 shadow when motion/glow is gated.
        var wanted = AmbientFxCanvas.Env.AllowGlow(AmbientFxCanvas.Env.CurrentTier)
                     && IsEffectivelyVisible && _podium.IsVisible
                     && _fxWindow is { IsActive: true, WindowState: not WindowState.Minimized }
                     && AmbientFxCanvas.Env.AllowAmbientLoops;
        if (!wanted)
        {
            _podiumBreath?.Stop(); _podiumBreath = null;
            glow.Opacity = .28; // template rest, also under Reduced or inactive
            return;
        }
        if (_podiumBreath != null) return;
        // Sibling BoxShadow, never an Effect on an animated card. Gold is a rank colour, not a mod accent.
        _podiumBreath = new BeatLoop(glow, t => glow.Opacity = .16 + .20 * BeatLoop.Breath(t, 4.2));
        _podiumBreath.Start();
    }

    private void StopPodiumEntrance()
    {
        _podiumEntrance?.Stop(); _podiumEntrance = null;
        foreach (var slot in _podiumSlots)
        {
            slot.Opacity = 1;
            if (slot.RenderTransform is TranslateTransform slide) slide.Y = 0;
        }
    }

    private void ClearPodiumFx()
    {
        _podiumBreath?.Stop(); _podiumBreath = null;
        if (_goldGlow != null) _goldGlow.Opacity = .28;
        _goldGlow = null;
        StopPodiumEntrance();
        _podiumSlots.Clear();
    }

    private void FlashMovedRows(IReadOnlyList<LeaderboardRow> moved)
    {
        ClearRankFlashes();
        _roster.UpdateLayout();
        foreach (var entry in moved)
        {
            if (_roster.ContainerFromItem(entry) is not ListBoxItem { IsEffectivelyVisible: true } row) continue;
            var key = entry.DeltaState == "up" ? "SuccessGreen" : "Danger";
            var color = this.TryFindResource(key, out var resource) && resource is Color c
                ? c : entry.DeltaState == "up" ? Colors.LimeGreen : Colors.OrangeRed;
            var brush = new SolidColorBrush(color) { Opacity = 0 };
            row.Background = brush;
            _rankWashes.Add((row, entry, brush));
        }
        if (_rankWashes.Count == 0) return;
        _rankFlash = new BeatLoop(this, t =>
        {
            for (var i = 0; i < _rankWashes.Count; i++)
            {
                var (row, item, brush) = _rankWashes[i];
                // A recycled container must never hand its wash to another subject.
                var elapsed = t - .035 * i;
                if (!ReferenceEquals(row.DataContext, item) || elapsed >= .4)
                {
                    if (ReferenceEquals(row.Background, brush)) row.ClearValue(BackgroundProperty);
                    continue;
                }
                brush.Opacity = elapsed < 0 ? 0 : elapsed <= .07
                    ? .30 * (1 - Math.Pow(1 - elapsed / .07, 2))
                    : .30 * (1 - Math.Pow(Math.Clamp((elapsed - .07) / .33, 0, 1), 2));
            }
            if (t >= .4 + .035 * (_rankWashes.Count - 1)) ClearRankFlashes();
        });
        _rankFlash.Start();
    }

    private void ClearRankFlashes()
    {
        _rankFlash?.Stop(); _rankFlash = null;
        foreach (var (row, _, brush) in _rankWashes)
            if (ReferenceEquals(row.Background, brush)) row.ClearValue(BackgroundProperty);
        _rankWashes.Clear();
    }
}
