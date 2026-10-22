// WPF MainWindow.Possession.cs: auto-tag the visible room, preserving hand tags and safety doors.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow : IPossessionHost
{
    private readonly ConditionalWeakTable<Control, PossessionTarget> _possessionTargets = new();
    private IReadOnlyList<PossessionTarget> _possessionCache = Array.Empty<PossessionTarget>();
    private DateTime _possessionCacheAt = DateTime.MinValue;
    private bool _possessionDirty = true;
    private Canvas? _possessionGhostLayer, _possessionRubbleFloor;
    private PossessionRuntime? _possessionRuntime;
    internal PossessionDirector? PossessionDirector { get; private set; }
    private Point _possessionPointer;
    private bool _possessionClosed;
    private static readonly HashSet<string> PossessionNeverNames = new(StringComparer.Ordinal)
    {
        "TxtLockdownTimer", "TxtLockdownExit", "BtnEmergencyExit", "EERoot", "LockdownGate",
        "TxtPossessionRung", "PossessionPips", "TxtXP", "EmiDockChip",
    };

    private void InitializePossessionHost()
    {
        if (LockdownService.Current is not { } lockdown) return;
        _possessionRuntime = new PossessionRuntime(TopLevelFrameSource.For(this));
        var effects = PossessionRuntime.CreateAll().Select(e => new DirectorHeadEffect(e, _possessionRuntime));
        PossessionDirector = new PossessionDirector(lockdown, effects, new PossessionDirectorHead
        {
            OnUi = a => { if (Dispatcher.UIThread.CheckAccess()) a(); else Dispatcher.UIThread.Post(a); },
            UiAlive = () => !_possessionClosed,
            Settings = () => CoreSettings.Current,
            MainHost = () => this,
            IsVisible = v => v is Control { IsEffectivelyVisible: true },
            ParentOf = v => (v as Visual)?.GetVisualParent(),
            Pointer = () => (_possessionPointer.X, _possessionPointer.Y),
            BoundsOf = (_, v) => v is Control c && TryPossessionBounds(c, out var r)
                ? (r.X, r.Y, r.Width, r.Height) : null,
            Attribution = _ =>
            {
                EnsurePossessionLayers();
                return new EmberHeadAttribution(_possessionGhostLayer!,
                    c => TryPossessionBounds(c, out var r) ? r : null, TopLevelFrameSource.For(this),
                    () => CoreSettings.Current.LockdownPhotosafe
                        || global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level != global::ConditioningControlPanel.Models.MotionLevel.Full);
            },
            // ponytail: full Ghost/cursor/edge, scenes, tripwire reactions, Warden/audio/remember and barks
            // remain possession-head-b2. Reuse the Core lifecycle/cadence without duplicating rules.
        });
        PossessionDirector.AttachHost(this);
        if (lockdown.IsActive) PossessionDirector.OnLockdownActivated();
        LayoutUpdated += (_, _) => _possessionDirty = true; // only flag; rebuild floor is 750 ms
        PointerMoved += (_, e) => _possessionPointer = e.GetPosition(this);
        Opened += (_, _) => EnsurePossessionLayers();
        Closed += (_, _) =>
        {
            _possessionClosed = true;
            PossessionDirector.Dispose();
            _possessionRuntime.UndoAll();
        };
    }

    private void EnsurePossessionLayers()
    {
        if (Named<Grid>("RootGrid") is not { } root || _possessionGhostLayer != null) return;
        _possessionGhostLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = false, ZIndex = 9000 };
        _possessionRubbleFloor = new Canvas
        {
            Height = 28, IsHitTestVisible = false, ClipToBounds = false,
            VerticalAlignment = VerticalAlignment.Bottom, ZIndex = 8999,
        };
        root.Children.Add(_possessionRubbleFloor);
        root.Children.Add(_possessionGhostLayer);
    }

    IReadOnlyList<PossessionTarget> IPossessionHost.Targets => GetPossessionTargets();
    bool IPossessionHost.IsUsable => IsVisible && !_possessionClosed && WindowState != WindowState.Minimized
        && !Games.GameWindow.IsAnyOpen() && !IntakeHostWindow.IsAnyOpen() && !LockCardWindow.IsAnyOpen()
        && !Chaos.ChaosRunHost.IsDescending;

    internal IReadOnlyList<PossessionTarget> GetPossessionTargets()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { _possessionDirty = true; return _possessionCache; }
        var age = DateTime.UtcNow - _possessionCacheAt;
        if (age < TimeSpan.FromSeconds(10) && (!_possessionDirty || age < TimeSpan.FromMilliseconds(750)))
            return _possessionCache;
        var found = new List<PossessionTarget>();
        var labels = new List<PossessionTarget>();
        if (Content is Visual root) WalkPossession(root, found, labels, 0, 17);
        double Score(PossessionTarget t)
        {
            if (!TryPossessionBounds((Control)t.Visual, out var r)) return double.MaxValue;
            if (_possessionPointer == default) return -r.Width * r.Height;
            return Math.Pow(r.Center.X - _possessionPointer.X, 2) + Math.Pow(r.Center.Y - _possessionPointer.Y, 2);
        }
        found.AddRange(labels.OrderBy(Score).Take(24));
        _possessionCache = found;
        _possessionCacheAt = DateTime.UtcNow;
        _possessionDirty = false;
        return found;
    }

    private (bool Interactive, bool Card) WalkPossession(Visual node, List<PossessionTarget> into,
        List<PossessionTarget> labels, int depth, int hash)
    {
        if (depth > 64 || ReferenceEquals(node, _possessionGhostLayer) || ReferenceEquals(node, _possessionRubbleFloor)) return default;
        var c = node as Control;
        var hand = c == null ? PossessionRole.None : PossessionTree.GetRole(c);
        if (c != null && (!c.IsEffectivelyVisible || PossessionTree.GetExclude(c)
            || (hand == PossessionRole.None && PossessionNeverNames.Contains(c.Name ?? "")))) return default;
        var role = hand == PossessionRole.None && c != null ? InferPossessionRole(c) : hand;
        bool interactive = role is PossessionRole.Button or PossessionRole.Toggle or PossessionRole.Slider
            or PossessionRole.Combo or PossessionRole.TextBox;
        bool card = false;
        if (role is PossessionRole.None or PossessionRole.Card or PossessionRole.Scroll)
        {
            int i = 0;
            foreach (var child in node.GetVisualChildren())
            {
                var below = WalkPossession(child, into, labels, depth + 1,
                    unchecked(hash * 31 + i++ * 7 + child.GetType().Name.Length));
                interactive |= below.Interactive;
                card |= below.Card;
            }
        }
        if (role == PossessionRole.None && interactive && !card && c is Border b && b.Background != null
            && b.CornerRadius != default && b.Bounds.Width >= 60 && b.Bounds.Height >= 60) role = PossessionRole.Card;
        if (c != null && role != PossessionRole.None && (hand != PossessionRole.None || PassesPossessionPlacement(c)))
        {
            var target = _possessionTargets.GetValue(c, el => new PossessionTarget
            {
                Visual = el, Role = role, Key = !string.IsNullOrEmpty(el.Name) ? el.Name : role + "#" + hash.ToString("x8"),
                DisplayName = PossessionTargetNames.For(role, PossessionTree.GetName(el),
                    (el as ContentControl)?.Content as string, (el as TextBlock)?.Text,
                    ToolTip.GetTip(el) as string, AutomationProperties.GetName(el)),
            });
            if (role == PossessionRole.Label && hand == PossessionRole.None) labels.Add(target);
            else into.Add(target);
            card |= role == PossessionRole.Card;
        }
        return (interactive, card);
    }

    private static PossessionRole InferPossessionRole(Control c) => c switch
    {
        ToggleButton => PossessionRole.Toggle,
        RepeatButton when c.GetVisualAncestors().Take(6).Any(p => p is ScrollBar) => PossessionRole.None,
        Button => PossessionRole.Button,
        Slider => PossessionRole.Slider,
        ComboBox => PossessionRole.Combo,
        TextBox => PossessionRole.TextBox,
        ProgressBar => PossessionRole.Progress,
        ScrollViewer s when s.Extent.Height > s.Viewport.Height => PossessionRole.Scroll,
        Image i when i.Bounds.Width >= 48 && i.Bounds.Height >= 48 => PossessionRole.Image,
        TextBlock t when t.FontSize >= 18 => PossessionRole.Title,
        TextBlock t when t.Text?.Trim().Length is >= 3 and <= 40 => PossessionRole.Label,
        _ => PossessionRole.None,
    };

    private bool PassesPossessionPlacement(Control c) => c.IsEnabled && c.IsHitTestVisible
        && TryPossessionBounds(c, out var r) && r.Width >= 8 && r.Height >= 8
        && r.Right > 0 && r.Bottom > 0 && r.Left < Bounds.Width && r.Top < Bounds.Height;

    internal bool TryPossessionBounds(Control c, out Rect bounds)
    {
        var transform = c.TransformToVisual(this);
        bounds = transform is { } m ? new Rect(c.Bounds.Size).TransformToAABB(m) : default;
        return bounds.Width > 0 && bounds.Height > 0 && double.IsFinite(bounds.X) && double.IsFinite(bounds.Y);
    }
}
