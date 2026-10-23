// WPF MainWindow.Possession.cs: stage, cached auto-tag walk and window-space geometry.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Views.Windows;

namespace ConditioningControlPanel.Avalonia.Possession;

internal sealed class PossessionHost : IPossessionHost, IDisposable
{
    private readonly Window _window;
    private readonly Grid _root;
    private readonly TimeProvider _time;
    private readonly ConditionalWeakTable<Control, PossessionTarget> _targets = new();
    private IReadOnlyList<PossessionTarget>? _cache;
    private DateTimeOffset _cacheAt = DateTimeOffset.MinValue, _layoutAt = DateTimeOffset.MinValue;
    private volatile bool _dirty = true;
    private bool _disposed;
    internal readonly Canvas GhostLayer = new() { IsHitTestVisible = false, ClipToBounds = false, ZIndex = 9000 };
    internal readonly Canvas RubbleFloor = new() { Height = 28, IsHitTestVisible = false, ClipToBounds = false,
        VerticalAlignment = VerticalAlignment.Bottom, ZIndex = 8999 };
    internal IFrameSource Frames { get; }
    internal PossessionAttribution Attribution { get; }
    internal Point Pointer { get; private set; }
    internal bool PointerInside { get; private set; }
    private static readonly HashSet<string> Never = new(StringComparer.Ordinal)
    {
        "TxtLockdownTimer", "TxtLockdownExit", "BtnEmergencyExit", "EERoot", "LockdownGate",
        "TxtPossessionRung", "PossessionPips", "TxtXP", "EmiDockChip",
    };

    internal PossessionHost(Window window, Grid root, IFrameSource? frames = null, TimeProvider? time = null)
    {
        _window = window; _root = root; _time = time ?? TimeProvider.System;
        Frames = frames ?? TopLevelFrameSource.For(window);
        foreach (var layer in new[] { GhostLayer, RubbleFloor })
        {
            Grid.SetRowSpan(layer, Math.Max(1, root.RowDefinitions.Count));
            Grid.SetColumnSpan(layer, Math.Max(1, root.ColumnDefinitions.Count));
            root.Children.Add(layer);
        }
        Attribution = new PossessionAttribution(this);
        window.LayoutUpdated += OnLayout;
        window.AddHandler(InputElement.PointerMovedEvent, OnPointer, global::Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
        window.PointerExited += OnLeave;
    }
    private void OnPointer(object? sender, PointerEventArgs e) { Pointer = e.GetPosition(GhostLayer); PointerInside = true; }
    private void OnLeave(object? sender, PointerEventArgs e) => PointerInside = false;
    private void OnLayout(object? sender, EventArgs e)
    {
        var now = _time.GetUtcNow();
        if (now - _layoutAt < TimeSpan.FromMilliseconds(250)) return;
        _layoutAt = now; _dirty = true;
    }
    public bool IsUsable
    {
        get
        {
            try
            {
                if (_disposed || !_window.IsVisible || _window.WindowState == WindowState.Minimized || LockCardWindow.IsAnyOpen()) return false;
                foreach (var surface in PanicSurfaces.All)
                    if (surface.OwnsTheScreen?.Invoke() == true) return false;
                return true;
            }
            catch { return false; }
        }
    }
    internal Rect? BoundsOf(Control c) => c.TransformToVisual(GhostLayer) is { } matrix
        ? new Rect(c.Bounds.Size).TransformToAABB(matrix) : null;
    public IReadOnlyList<PossessionTarget> Targets
    {
        get
        {
            if (!Dispatcher.UIThread.CheckAccess()) { _dirty = true; return _cache ?? Array.Empty<PossessionTarget>(); }
            if (_disposed) return Array.Empty<PossessionTarget>();
            var now = _time.GetUtcNow(); var age = now - _cacheAt;
            if (_cache != null && age <= TimeSpan.FromSeconds(10) && (!_dirty || age <= TimeSpan.FromMilliseconds(750))) return _cache;
            try
            {
                var found = new List<PossessionTarget>(); var labels = new List<PossessionTarget>();
                Walk(_root, found, labels, 0, 17);
                if (labels.Count > 24)
                {
                    double Score(PossessionTarget t)
                    {
                        if (BoundsOf((Control)t.Visual) is not { } r) return double.MaxValue;
                        return Pointer.X > 0 || Pointer.Y > 0 ? Math.Pow(r.Center.X - Pointer.X, 2) + Math.Pow(r.Center.Y - Pointer.Y, 2) : -r.Width * r.Height;
                    }
                    labels.Sort((a, b) => Score(a).CompareTo(Score(b)));
                    labels.RemoveRange(24, labels.Count - 24);
                }
                found.AddRange(labels); _cache = found; _cacheAt = now; _dirty = false;
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Possession target walk failed"); }
            return _cache ?? Array.Empty<PossessionTarget>();
        }
    }
    private (bool Interactive, bool Card) Walk(Visual node, List<PossessionTarget> into, List<PossessionTarget> labels, int depth, int hash)
    {
        if (depth > 64 || ReferenceEquals(node, GhostLayer) || ReferenceEquals(node, RubbleFloor)) return default;
        var c = node as Control;
        if (!node.IsEffectivelyVisible || c != null && PossessionTree.GetExclude(c)) return default;
        var hand = c == null ? PossessionRole.None : PossessionTree.GetRole(c);
        if (c != null && hand == PossessionRole.None && Never.Contains(c.Name ?? "")) return default;
        var role = hand != PossessionRole.None ? hand : Infer(c);
        bool interactive = role is PossessionRole.Button or PossessionRole.Toggle or PossessionRole.Slider or PossessionRole.Combo or PossessionRole.TextBox;
        bool belowInteractive = false, belowCard = false;
        if (role is PossessionRole.None or PossessionRole.Card or PossessionRole.Scroll)
        {
            int i = 0;
            foreach (var child in node.GetVisualChildren())
            {
                var below = Walk(child, into, labels, depth + 1, unchecked(hash * 31 + i++ * 7 + child.GetType().Name.Length));
                belowInteractive |= below.Interactive; belowCard |= below.Card;
            }
        }
        if (role == PossessionRole.None && belowInteractive && !belowCard && c is Border b && b.Background != null
            && b.CornerRadius != default && b.Bounds.Width >= 60 && b.Bounds.Height >= 60) role = PossessionRole.Card;
        if (c != null && role != PossessionRole.None && (hand != PossessionRole.None || Fits(c)))
        {
            var t = _targets.GetValue(c, el => new PossessionTarget { Visual = el, Role = role,
                Key = string.IsNullOrEmpty(el.Name) ? role + "#" + hash.ToString("x8") : el.Name, DisplayName = DisplayName(el, role) });
            if (role == PossessionRole.Label && hand == PossessionRole.None) labels.Add(t); else into.Add(t);
            belowCard |= role == PossessionRole.Card;
        }
        return (interactive || belowInteractive, belowCard);
    }
    private bool Fits(Control c) => c.IsEffectivelyEnabled && c.IsHitTestVisible && BoundsOf(c) is { } r
        && r.Width >= 8 && r.Height >= 8 && r.Right > 0 && r.Bottom > 0 && r.Left < _window.Bounds.Width && r.Top < _window.Bounds.Height;
    private static PossessionRole Infer(Control? c) => c switch
    {
        ToggleButton => PossessionRole.Toggle,
        RepeatButton r when r.GetVisualAncestors().Take(6).Any(a => a is ScrollBar) => PossessionRole.None,
        Button => PossessionRole.Button, Slider => PossessionRole.Slider, ComboBox => PossessionRole.Combo,
        TextBox => PossessionRole.TextBox, ProgressBar => PossessionRole.Progress,
        ScrollViewer s when s.Extent.Height > s.Viewport.Height => PossessionRole.Scroll,
        Image i when i.Bounds.Width >= 48 && i.Bounds.Height >= 48 => PossessionRole.Image,
        TextBlock t when t.FontSize >= 18 => PossessionRole.Title,
        TextBlock t when t.Text?.Trim().Length is >= 3 and <= 40 => PossessionRole.Label,
        _ => PossessionRole.None,
    };
    private static string DisplayName(Control c, PossessionRole role)
    {
        var hand = PossessionTree.GetName(c);
        if (!string.IsNullOrWhiteSpace(hand)) return hand;
        string? Tidy(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            s = s.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            while (s.Contains("  ", StringComparison.Ordinal)) s = s.Replace("  ", " ", StringComparison.Ordinal);
            return s.Length is >= 2 and <= 32 ? s : null;
        }
        var text = Tidy((c as ContentControl)?.Content as string) ?? Tidy((c as TextBlock)?.Text)
            ?? Tidy(ToolTip.GetTip(c) as string) ?? Tidy(AutomationProperties.GetName(c));
        string noun = role switch { PossessionRole.Button => "button", PossessionRole.Toggle => "toggle", PossessionRole.TabHeader => "tab",
            PossessionRole.Slider => "slider", PossessionRole.Combo => "dropdown", PossessionRole.Card => "card", PossessionRole.Title => "heading",
            PossessionRole.Label => "label", _ => "" };
        if (text != null) return "the " + text + (noun.Length > 0 ? " " + noun : "");
        return role switch { PossessionRole.Title => "the title", PossessionRole.Timer => "the timer", PossessionRole.Image => "that picture",
            PossessionRole.Scroll => "that list", PossessionRole.Progress => "that bar", PossessionRole.TextBox => "that box",
            _ => noun.Length > 0 ? "that " + noun : "something" };
    }
    public void Dispose()
    {
        _disposed = true; Attribution.ReleaseAll(); _cache = null;
        _window.LayoutUpdated -= OnLayout; _window.RemoveHandler(InputElement.PointerMovedEvent, OnPointer); _window.PointerExited -= OnLeave;
        _root.Children.Remove(GhostLayer); _root.Children.Remove(RubbleFloor);
    }
}
