using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>WPF MainWindow.TabFxPresetsQuestsAchievements: the preset/session builders now live
/// in this view, so their focal sheen and interaction hooks live beside the selection source.</summary>
public partial class PresetsTabView
{
    private CardSheenAdorner? _selectedSheen;
    private Control? _sheenHost;
    private Window? _fxWindow;
    private CancellationTokenSource? _entranceCancellation;

    private void InitializeFocalFx()
    {
        WirePress(BtnLoadPreset);
        WirePress(BtnStartSession);
        Loaded += (_, _) => { if (IsEffectivelyVisible) StaggerZones(); RefreshCardSheen(); };
    }

    private void AttachFocalFx()
    {
        DetachFocalFx();
        _fxWindow = TopLevel.GetTopLevel(this) as Window;
        if (_fxWindow != null)
        {
            _fxWindow.Activated += OnFxWindowChanged;
            _fxWindow.Deactivated += OnFxWindowChanged;
            _fxWindow.Closed += OnFxClosed;
            _fxWindow.PropertyChanged += OnFxWindowProperty;
        }
        CoreMods.ModChanged += OnFxModChanged;
        Env.MotionGateChanged += RefreshCardSheen;
        RefreshCardSheen();
    }

    private void DetachFocalFx()
    {
        if (_fxWindow != null)
        {
            _fxWindow.Activated -= OnFxWindowChanged;
            _fxWindow.Deactivated -= OnFxWindowChanged;
            _fxWindow.Closed -= OnFxClosed;
            _fxWindow.PropertyChanged -= OnFxWindowProperty;
        }
        _fxWindow = null;
        CoreMods.ModChanged -= OnFxModChanged;
        Env.MotionGateChanged -= RefreshCardSheen;
        CancelEntrances();
        DetachCardSheen();
        DetachRowSweep();
    }

    private void OnFxWindowChanged(object? sender, EventArgs e) => RefreshCardSheen();
    private void OnFxClosed(object? sender, EventArgs e) => DetachFocalFx();
    private void OnFxWindowProperty(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty) RefreshCardSheen();
    }
    private void OnFxModChanged(object? sender, ModPackage mod) => Dispatcher.UIThread.Post(() =>
    {
        DetachCardSheen();
        DetachRowSweep();
        RefreshCardSheen();
    });

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != IsVisibleProperty) return;
        // WPF OnPresetsTabVisibilityChanged: DATA BEFORE FX. A session drafted while the tab was hidden
        // joins the rack, "Recent" re-sorts on the files' new dates, and the shelf repaints - all
        // before the stagger animates them in. The shell snaps tabs (no outgoing re-show), so WPF's
        // IsIncomingTab guard has nothing to filter here.
        if (IsVisible && SessionRackPanel != null)
        {
            SyncCustomSessionsFromDisk();
            RepaintSessionRack();
            RefreshTakeawayShelf();
        }
        if (IsVisible && _fxWindow != null) StaggerZones();
        else { CancelEntrances(); DetachRowSweep(); }
        RefreshCardSheen();
    }

    private void DetachCardSheen()
    {
        CardSheenAdorner.Detach(_selectedSheen);
        _selectedSheen = null;
        _sheenHost = null;
    }

    private void RefreshCardSheen()
    {
        Control? target = null;
        if (IsEffectivelyVisible && _fxWindow is { IsActive: true } window
            && window.WindowState != WindowState.Minimized && Env.AllowAmbientLoops)
        {
            target = _selectedPreset != null
                ? PresetCardsPanel.Children.OfType<Border>().FirstOrDefault(c => c.Tag as string == _selectedPreset.Id)
                : SessionRackPanel.Children.OfType<Border>().FirstOrDefault(c => c.Tag is Session s && s.Id == _selectedSession?.Id);
        }
        if (ReferenceEquals(target, _sheenHost) && _selectedSheen != null) return;
        DetachCardSheen();
        if (target == null || !target.IsEffectivelyVisible) return;
        _selectedSheen = CardSheenAdorner.Attach(target, _selectedPreset != null ? 17 : 10);
        if (_selectedSheen != null) _sheenHost = target;
    }

    private static TransformGroup? CardTransforms(Control card)
    {
        if (card.RenderTransform is TransformGroup group) return group;
        if (card.RenderTransform != null) return null; // preserve authored transforms, as WPF
        card.RenderTransformOrigin = RelativePoint.Center;
        card.RenderTransform = group = new TransformGroup();
        group.Children.Add(new ScaleTransform(1, 1));
        group.Children.Add(new TranslateTransform());
        return group;
    }

    private void PrepareChipFx(Control chip)
    {
        CardTransforms(chip);
        chip.PointerEntered += (_, _) => ScaleCard(chip, 1.02, 150);
        chip.PointerExited += (_, _) => ScaleCard(chip, 1, 150);
        chip.Loaded += (_, _) => RefreshCardSheen();
    }

    private static void ScaleCard(Control card, double target, int ms)
    {
        var scale = CardTransforms(card)?.Children.OfType<ScaleTransform>().FirstOrDefault();
        if (scale == null) return;
        scale.Transitions = Env.AllowTransitions ? new Transitions
        {
            new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = new QuadraticEaseOut() },
            new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = new QuadraticEaseOut() },
        } : null;
        scale.ScaleX = scale.ScaleY = target;
    }

    private static void LiftSessionRow(Control row, bool on)
    {
        var slide = CardTransforms(row)?.Children.OfType<TranslateTransform>().FirstOrDefault();
        if (slide == null) return;
        slide.Transitions = Env.AllowTransitions ? new Transitions
        {
            new DoubleTransition { Property = TranslateTransform.YProperty, Duration = TimeSpan.FromMilliseconds(150), Easing = new QuadraticEaseOut() },
        } : null;
        slide.Y = on ? -2 : 0; // no scale: full-width rows must not grow beyond their viewport
    }

    private static void WirePress(Button button)
    {
        CardTransforms(button);
        button.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) ScaleCard(button, 0.97, 80);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        button.AddHandler(PointerReleasedEvent, (_, _) => ScaleCard(button, 1, 80), RoutingStrategies.Tunnel, handledEventsToo: true);
        button.PointerExited += (_, _) => ScaleCard(button, 1, 80);
        button.PointerCaptureLost += (_, _) => ScaleCard(button, 1, 80);
    }

    private void CancelEntrances()
    {
        _entranceCancellation?.Cancel();
        _entranceCancellation?.Dispose();
        _entranceCancellation = null;
    }
    private void StaggerZones()
    {
        CancelEntrances();
        if (!Env.AllowTransitions) return;
        _entranceCancellation = new CancellationTokenSource();
        foreach (var zone in new Panel[] { PresetCardsPanel, SessionRackPanel, TakeawayShelf })
        {
            int index = 0;
            foreach (var card in zone.Children.OfType<Border>().Where(c => c.IsVisible))
            {
                if (CardTransforms(card) == null) continue;
                var delay = TimeSpan.FromMilliseconds(40 * Math.Min(index++, 6));
                _ = EnterAsync(card, delay, 220, OpacityProperty, 0, 1, _entranceCancellation.Token);
                _ = EnterAsync(card, delay, 260, TranslateTransform.YProperty, 10, 0, _entranceCancellation.Token);
            }
        }
    }
    private static async Task EnterAsync(Control card, TimeSpan delay, int ms, AvaloniaProperty property,
                                         double from, double to, CancellationToken cancellation)
    {
        try
        {
            await new Animation
            {
                Delay = delay, Duration = TimeSpan.FromMilliseconds(ms), Easing = new QuadraticEaseOut(), FillMode = FillMode.Backward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(property, from) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(property, to) } },
                },
            }.RunAsync(card, cancellation); // Visual target, never Animation.RunAsync on a Transform
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Serilog.Log.Debug(ex, "Preset card entrance"); }
    }
}
