// WPF spec: ConditioningControlPanel/MainWindow/MainWindow.Possession.cs:
// stage/lifecycle :53/:96, weak identity :48/:432, invalidation/cache :149/:266,
// label budget :323, walk/pruning :355, roles/cards :454/:513, geometry :529/:623,
// display names :563, usability :658. Role/Name: Services/Possession/Possession.cs:34/:57.
// Pointer tunneling: Services/Possession/PossessionPointer.cs:62.
// ponytail: stage/registry only. Do not start the director before attribution and the effect
// adapters exist: WPF requires a charge and outline before any control moves.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Services.Possession;
namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow : IPossessionHost
    {
        private Canvas? _possessionGhostLayer, _possessionRubbleFloor;
        private readonly ConditionalWeakTable<Control, PossessionTarget> _possessionTargets = new();
        internal Func<DateTime> PossessionNow { get; set; } = () => DateTime.Now;
        private Point _possessionPointer;
        private bool _possessionClosed;
        private void InitializePossessionHost()
        {
            Opened += (_, _) => EnsurePossessionLayers();
            // WPF PossessionPointer.cs:62 uses Preview (tunnel) move + down, so handled child events still count.
            AddHandler(PointerMovedEvent, (_, e) => _possessionPointer = e.GetPosition(this),
                RoutingStrategies.Tunnel, handledEventsToo: true);
            AddHandler(PointerPressedEvent, (_, e) => _possessionPointer = e.GetPosition(this),
                RoutingStrategies.Tunnel, handledEventsToo: true);
            Closed += (_, _) =>
            {
                _possessionClosed = true;
                _possessionTargetCache = null;
            };
        }
        internal Canvas PossessionGhostLayer
        {
            get { EnsurePossessionLayers(); return _possessionGhostLayer ??= new Canvas { IsHitTestVisible = false }; }
        }
        internal Canvas PossessionRubbleFloor
        {
            get { EnsurePossessionLayers(); return _possessionRubbleFloor ??= new Canvas { IsHitTestVisible = false }; }
        }
        internal Point PossessionPointOf(Control element) =>
            element.TranslatePoint(default, PossessionGhostLayer) ?? default;
        internal void EnsurePossessionLayers()
        {
            if (Content is not Grid root) return;
            if (_possessionGhostLayer == null || !root.Children.Contains(_possessionGhostLayer))
            {
                _possessionGhostLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = false, ZIndex = 9000 };
                root.Children.Add(_possessionGhostLayer);
            }
            if (_possessionRubbleFloor == null || !root.Children.Contains(_possessionRubbleFloor))
            {
                _possessionRubbleFloor = new Canvas { Height = 28, IsHitTestVisible = false,
                    ClipToBounds = false, VerticalAlignment = VerticalAlignment.Bottom, ZIndex = 8999 };
                root.Children.Add(_possessionRubbleFloor);
            }
            if (_possessionLayoutHooked) return;
            _possessionLayoutHooked = true;
            LayoutUpdated += (_, _) =>
            {
                var now = PossessionNow();
                if (now - _possessionLayoutSeenAt < TimeSpan.FromMilliseconds(250)) return;
                _possessionLayoutSeenAt = now;
                _possessionCacheDirty = true;
            };
        }
        bool IPossessionHost.IsUsable => !_possessionClosed && IsVisible
            && WindowState != WindowState.Minimized && !LockCardWindow.IsAnyOpen()
            && !OwnedWindows.Any(w => w.IsVisible);
        // ponytail: conservatively pause behind any owned surface until each takeover has a live service seam.
        private List<PossessionTarget>? _possessionTargetCache;
        private DateTime _possessionCacheAt = DateTime.MinValue;
        private bool _possessionCacheDirty = true;
        private DateTime _possessionLayoutSeenAt = DateTime.MinValue;
        private bool _possessionLayoutHooked;
        private static readonly TimeSpan PossessionRebuildFloor = TimeSpan.FromMilliseconds(750);
        private static readonly TimeSpan PossessionCacheMaxAge = TimeSpan.FromSeconds(10);
        private static readonly HashSet<string> PossessionNeverNames = new(StringComparer.Ordinal)
        {
            "TxtLockdownTimer", "TxtLockdownExit", "BtnEmergencyExit", "EERoot",
            "LockdownGate", "TxtPossessionRung", "PossessionPips", "TxtXP",
            "EmiDockChip",
        };
        private const int MaxAutoLabels = 24;
        private const double MinTargetPx = 8;
        IReadOnlyList<PossessionTarget> IPossessionHost.Targets => GetPossessionTargets();
        internal IReadOnlyList<PossessionTarget> GetPossessionTargets()
        {
            if (_possessionClosed) return Array.Empty<PossessionTarget>();
            if (!Dispatcher.UIThread.CheckAccess())
            {
                _possessionCacheDirty = true;
                return _possessionTargetCache ?? (IReadOnlyList<PossessionTarget>)Array.Empty<PossessionTarget>();
            }
            try
            {
                var now = PossessionNow();
                var age = now - _possessionCacheAt;
                var stale = _possessionTargetCache == null
                            || age > PossessionCacheMaxAge
                            || (_possessionCacheDirty && age > PossessionRebuildFloor);
                if (!stale) return _possessionTargetCache!;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var found = new List<PossessionTarget>();
                var labels = new List<PossessionTarget>();
                var root = Content as Visual;
                if (root != null) WalkPossession(root, found, labels, 0, 17);
                if (labels.Count > MaxAutoLabels) TrimLabels(labels);
                found.AddRange(labels);
                _possessionTargetCache = found;
                _possessionCacheAt = now;
                _possessionCacheDirty = false;
                sw.Stop();
                Serilog.Log.Logger?.Debug("Possession: auto-tag walk found {Count} targets in {Ms:F1} ms",
                    found.Count, sw.Elapsed.TotalMilliseconds);
                return found;
            }
            catch (Exception ex)
            {
                Serilog.Log.Logger?.Warning(ex, "Possession: target walk failed");
                return _possessionTargetCache ?? (IReadOnlyList<PossessionTarget>)Array.Empty<PossessionTarget>();
            }
        }
        private void TrimLabels(List<PossessionTarget> labels)
        {
            try
            {
                var origin = _possessionPointer;
                bool haveCursor = origin.X > 0 || origin.Y > 0;
                double Score(PossessionTarget t)
                {
                    try
                    {
                        if (!TryWindowBounds((Control)t.Visual, out var r)) return double.MaxValue;
                        if (!haveCursor) return -(r.Width * r.Height);         // biggest first
                        var dx = r.X + r.Width / 2 - origin.X;
                        var dy = r.Y + r.Height / 2 - origin.Y;
                        return dx * dx + dy * dy;                              // nearest first
                    }
                    catch { return double.MaxValue; }
                }
                labels.Sort((a, b) => Score(a).CompareTo(Score(b)));
            }
            catch { }
            if (labels.Count > MaxAutoLabels) labels.RemoveRange(MaxAutoLabels, labels.Count - MaxAutoLabels);
        }
        private readonly record struct PossessionSubtree(bool Interactive, bool Card);
        private PossessionSubtree WalkPossession(Visual node, List<PossessionTarget> into,
                                                 List<PossessionTarget> labels, int depth, int pathHash)
        {
            if (node == null || depth > 64) return default;
            if (ReferenceEquals(node, _possessionGhostLayer) || ReferenceEquals(node, _possessionRubbleFloor))
                return default;
            Control? fe = node as Control;
            var handRole = PossessionRole.None;
            if (fe != null)
            {
                if (!fe.IsEffectivelyVisible) return default;
                if (PossessionTree.GetExclude(fe)) return default;
                handRole = PossessionTree.GetRole(fe);
                if (handRole == PossessionRole.None
                    && !string.IsNullOrEmpty(fe.Name) && PossessionNeverNames.Contains(fe.Name)) return default;
            }
            var role = handRole;
            if (fe != null && role == PossessionRole.None) role = InferLeafRole(fe);
            var leaf = role != PossessionRole.None && role != PossessionRole.Card && role != PossessionRole.Scroll;
            var interactive = IsInteractiveRole(role);
            PossessionSubtree below = default;
            if (!leaf)
            {
                var children = node.GetVisualChildren().ToArray();
                var count = children.Length;
                for (int i = 0; i < count; i++)
                {
                    var child = children[i];
                    var h = unchecked(pathHash * 31 + i * 7 + (child?.GetType().Name.Length ?? 0));
                    var r = WalkPossession(child, into, labels, depth + 1, h);
                    if (r.Interactive) below = below with { Interactive = true };
                    if (r.Card) below = below with { Card = true };
                }
            }
            if (fe == null) return below;
            if (role == PossessionRole.None && below.Interactive && !below.Card && IsCardBorder(fe))
                role = PossessionRole.Card;
            if (role != PossessionRole.None && (handRole != PossessionRole.None || PassesSizeAndPlacement(fe)))
            {
                var t = TargetFor(fe, role, pathHash);
                if (t != null)
                {
                    if (role == PossessionRole.Label && handRole == PossessionRole.None) labels.Add(t);
                    else into.Add(t);
                    if (role == PossessionRole.Card) below = below with { Card = true };
                }
            }
            return new PossessionSubtree(interactive || below.Interactive, below.Card);
        }
        private PossessionTarget? TargetFor(Control fe, PossessionRole role, int pathHash)
        {
            try
            {
                if (_possessionTargets.TryGetValue(fe, out var existing)) return existing;
                var target = new PossessionTarget
                {
                    Visual = fe,
                    Role = role,
                    Key = !string.IsNullOrEmpty(fe.Name) ? fe.Name : role + "#" + pathHash.ToString("x8"),
                    DisplayName = DisplayNameFor(fe, role),
                };
                _possessionTargets.Add(fe, target);
                return target;
            }
            catch { return null; }
        }
        private static PossessionRole InferLeafRole(Control fe)
        {
            if (fe is ToggleButton) return PossessionRole.Toggle;
            if (fe is Button)
            {
                if (fe is RepeatButton && IsInsideScrollBar(fe)) return PossessionRole.None;
                return PossessionRole.Button;
            }
            if (fe is Slider) return PossessionRole.Slider;
            if (fe is ComboBox) return PossessionRole.Combo;
            if (fe is TextBox) return PossessionRole.TextBox;
            if (fe is ProgressBar) return PossessionRole.Progress;
            if (fe is ScrollViewer sv)
            {
                double scrollable;
                try { scrollable = sv.Extent.Height - sv.Viewport.Height; } catch { scrollable = 0; }
                return scrollable > 0 ? PossessionRole.Scroll : PossessionRole.None;
            }
            if (fe is Image img)
                return (img.Bounds.Width >= 48 && img.Bounds.Height >= 48) ? PossessionRole.Image : PossessionRole.None;
            if (fe is TextBlock tb)
            {
                double size;
                try { size = tb.FontSize; } catch { size = 0; }
                if (size >= 18) return PossessionRole.Title;
                var text = tb.Text;
                if (string.IsNullOrWhiteSpace(text)) return PossessionRole.None;
                var trimmed = text.Trim();
                return trimmed.Length is >= 3 and <= 40 ? PossessionRole.Label : PossessionRole.None;
            }
            return PossessionRole.None;
        }
        private static bool IsInsideScrollBar(Visual? node)
        {
            try
            {
                for (int i = 0; node != null && i < 6; i++)
                {
                    if (node is ScrollBar) return true;
                    node = node.GetVisualParent();
                }
            }
            catch { }
            return false;
        }
        private static bool IsInteractiveRole(PossessionRole role) => role is PossessionRole.Button
            or PossessionRole.Toggle or PossessionRole.Slider or PossessionRole.Combo or PossessionRole.TextBox;
        private static bool IsCardBorder(Control fe)
        {
            try
            {
                if (fe is not Border b) return false;
                if (b.Background == null) return false;
                var cr = b.CornerRadius;
                if (cr.TopLeft <= 0 && cr.TopRight <= 0 && cr.BottomLeft <= 0 && cr.BottomRight <= 0) return false;
                return b.Bounds.Height >= 60 && b.Bounds.Width >= 60;
            }
            catch { return false; }
        }
        private bool PassesSizeAndPlacement(Control fe)
        {
            try
            {
                if (!fe.IsEnabled || !fe.IsHitTestVisible) return false;
                if (fe.Bounds.Width <= 0 || fe.Bounds.Height <= 0) return false;
                if (!TryWindowBounds(fe, out var r)) return false;
                if (r.Width < MinTargetPx || r.Height < MinTargetPx) return false;
                var w = Bounds.Width;
                var h = Bounds.Height;
                if (w <= 0 || h <= 0) return false;
                return r.Right > 0 && r.Bottom > 0 && r.X < w && r.Y < h;
            }
            catch { return false; }
        }
        internal bool TryWindowBounds(Control fe, out Rect bounds)
        {
            bounds = default;
            try
            {
                var t = fe.TransformToVisual(this);
                if (t == null) return false;
                bounds = new Rect(fe.Bounds.Size).TransformToAABB(t.Value);
                return bounds.Width > 0 && bounds.Height > 0 && !double.IsNaN(bounds.X) && !double.IsNaN(bounds.Y);
            }
            catch { return false; }
        }
        private static string DisplayNameFor(Control fe, PossessionRole role)
        {
            try
            {
                var hand = PossessionTree.GetName(fe);
                if (!string.IsNullOrWhiteSpace(hand)) return hand;
                var text = Tidy(fe is ContentControl cc ? cc.Content as string : null)
                           ?? Tidy(fe is TextBlock tb ? tb.Text : null)
                           ?? Tidy(ToolTip.GetTip(fe) as string)
                           ?? Tidy(AutomationProperties.GetName(fe));
                if (string.IsNullOrEmpty(text)) return FallbackDisplayName(role);
                return role switch
                {
                    PossessionRole.Button => "the " + text + " button",
                    PossessionRole.Toggle => "the " + text + " toggle",
                    PossessionRole.TabHeader => "the " + text + " tab",
                    PossessionRole.Slider => "the " + text + " slider",
                    PossessionRole.Combo => "the " + text + " dropdown",
                    PossessionRole.Card => "the " + text + " card",
                    PossessionRole.Title => "the " + text + " heading",
                    PossessionRole.Label => "the " + text + " label",
                    _ => "the " + text,
                };
            }
            catch { return FallbackDisplayName(role); }
        }
        private static string? Tidy(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            while (s.Contains("  ", StringComparison.Ordinal)) s = s.Replace("  ", " ", StringComparison.Ordinal);
            if (s.Length is < 2 or > 32) return null;
            return s;
        }
        private static string FallbackDisplayName(PossessionRole role) => role switch
        {
            PossessionRole.Button => "that button",
            PossessionRole.Card => "that card",
            PossessionRole.Toggle => "that toggle",
            PossessionRole.Title => "the title",
            PossessionRole.Label => "that label",
            PossessionRole.TabHeader => "that tab",
            PossessionRole.Timer => "the timer",
            PossessionRole.Slider => "that slider",
            PossessionRole.Combo => "that dropdown",
            PossessionRole.Image => "that picture",
            PossessionRole.Scroll => "that list",
            PossessionRole.Progress => "that bar",
            PossessionRole.TextBox => "that box",
            _ => "something",
        };
    }
}
