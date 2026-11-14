// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Possession.cs (the window as a haunted room:
// the ghost layer, the registry of possessable controls and the A1 auto-tag walk, IsUsable, PointOf)
// and MainWindow.Lab.cs:871-986 + 1100-1140 (the rung readout under the Lockdown timer and the
// first-run warden's rules). The choreography is the Core PossessionDirector's (App.Possession); this
// partial is only the stage.
//
// The walk keeps WPF's rules: hand tags (PossessionTree.Role / Name) win, then the never-names
// blocklist, then the type inference (toggle before button, ScrollBar chrome is never a button, a
// scrollable ScrollViewer is a list, 48 px images, 18 pt titles, 3-40 character labels), the leaf rule
// (never descend into an interactive control), the innermost rounded painted Border holding a control
// is a card, size and placement in WINDOW pixels, at most 24 labels (nearest the pointer, else the
// biggest), target objects cached per control so cooldowns survive every rebuild, the rebuild behind a
// 250 ms layout flag, a 750 ms floor and a 10 s max age, and never off the UI thread.
//
// Not here (recorded in docs/avalonia-parity.html shell-possession): the hand tags in this head's XAML (WPF's poss:Possession.Role marks were
// dropped in the port; the inference alone tags the room), the Bureau / Loom / DtRH takeover checks
// (those hosts are not on this head; GameWindow covers the web games).

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Possession;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow : IAvaloniaPossessionHost
    {
        // ==== the stage =================================================================

        private Canvas? _possessionGhostLayer, _possessionRubbleFloor;
        private bool _possessionHostHooked;
        private bool _possessionAttached;
        private PossessionRuntime? _possessionRuntime;
        private readonly PossessionPointer _possessionPointer = new();

        /// <summary>Live target objects, keyed by the control they describe: the director hangs cooldowns and
        /// the "currently possessed" flag off these, so they must outlive every rebuilt list.</summary>
        private readonly ConditionalWeakTable<Control, PossessionTarget> _possessionTargets = new();

        /// <summary>Test seam: the frame source effects and ember overlays step on (default: this window's).</summary>
        internal IFrameSource? PossessionFramesOverride { get; set; }

        /// <summary>The director this shell haunts for (default <see cref="App.Possession"/>; tests inject one).</summary>
        internal PossessionDirector? PossessionDirectorOverride { get; set; }

        private PossessionDirector? Director => PossessionDirectorOverride ?? App.Possession;

        internal Canvas? PossessionGhostLayer => _possessionGhostLayer;

        /// <summary>WPF InitializePossessionHost (called from InitializeLockdown): attach to the director once
        /// the window is up, detach when it closes, follow the pointer, and drive the readout.</summary>
        private void InitializePossessionHost()
        {
            if (_possessionHostHooked) return;
            _possessionHostHooked = true;
            try
            {
                Opened += (_, _) => AttachPossessionHost();
                Closed += (_, _) =>
                {
                    try
                    {
                        _possessionPointer.Detach();
                        if (_possessionAttached) Director?.DetachHost(this);
                        _possessionAttached = false;
                    }
                    catch (Exception ex) { Serilog.Log.Warning(ex, "Possession: failed to detach the main window host"); }
                };
                foreach (var name in NavSectionButtonNames) if (Named<Button>(name) is { } door) PossessionTree.SetRole(door, PossessionRole.TabHeader);
                AddHandler(PointerPressedEvent, (_, e) => { if (_possessionAttached && Director is { } d && d.TargetFor(e.Source, PossessionRole.TabHeader) is { } door) d.RequestReactive("drop", door, PossessionRung.Collapse); }, RoutingStrategies.Tunnel, handledEventsToo: true);
                LayoutUpdated += (_, _) =>
                {
                    var now = DateTime.Now;
                    if (now - _possessionLayoutSeenAt < TimeSpan.FromMilliseconds(250)) return;
                    _possessionLayoutSeenAt = now;
                    _possessionCacheDirty = true;
                };
                HookPossessionLockdown();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Possession: host initialization failed"); }
        }

        /// <summary>Idempotent; also the test entry point (a test shell is not the lifetime's MainWindow).</summary>
        internal void AttachPossessionHost()
        {
            try
            {
                EnsurePossessionLayers();
                if (_possessionAttached || Director is not { } director) return;
                _possessionPointer.Attach(this);
                director.AttachHost(this);
                _possessionAttached = true;
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Possession: failed to attach the main window host"); }
        }

        /// <summary>WPF EnsurePossessionLayers: one transparent, never hit-testable Canvas over the whole
        /// window, above everything (RootGrid is the window's one full-size seat, as for the Lockdown flash).</summary>
        private void EnsurePossessionLayers()
        {
            if (Named<Grid>("RootGrid") is not { } root) return;
            if (_possessionRubbleFloor == null || !root.Children.Contains(_possessionRubbleFloor))
            {
                _possessionRubbleFloor = new Canvas { Height = 28, IsHitTestVisible = false, ClipToBounds = false,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom, ZIndex = 8999 };
                if (root.RowDefinitions.Count > 1) Grid.SetRowSpan(_possessionRubbleFloor, root.RowDefinitions.Count);
                if (root.ColumnDefinitions.Count > 1) Grid.SetColumnSpan(_possessionRubbleFloor, root.ColumnDefinitions.Count);
                root.Children.Add(_possessionRubbleFloor);
            }
            if (_possessionGhostLayer != null && root.Children.Contains(_possessionGhostLayer)) return;
            _possessionGhostLayer = new Canvas
            {
                Name = "PossessionGhostLayer",
                IsHitTestVisible = false,
                ClipToBounds = false,
                Background = null,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Stretch,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Stretch,
                ZIndex = 9000,
            };
            if (root.RowDefinitions.Count > 1) Grid.SetRowSpan(_possessionGhostLayer, root.RowDefinitions.Count);
            if (root.ColumnDefinitions.Count > 1) Grid.SetColumnSpan(_possessionGhostLayer, root.ColumnDefinitions.Count);
            root.Children.Add(_possessionGhostLayer);
        }

        // ==== IAvaloniaPossessionHost ===================================================

        Window? IAvaloniaPossessionHost.PossessionWindow => this;

        Canvas? IAvaloniaPossessionHost.GhostLayer
        {
            get { EnsurePossessionLayers(); return _possessionGhostLayer; }
        }

        Canvas? IAvaloniaPossessionHost.RubbleFloor { get { EnsurePossessionLayers(); return _possessionRubbleFloor; } }

        IFrameSource IAvaloniaPossessionHost.PossessionFrames => PossessionFramesOverride ?? TopLevelFrameSource.For(this);

        PossessionRuntime IAvaloniaPossessionHost.PossessionEffectRuntime =>
            _possessionRuntime ??= new PossessionRuntime(((IAvaloniaPossessionHost)this).PossessionFrames);

        Point IAvaloniaPossessionHost.PointerPosition => _possessionPointer.Position;
        PossessionPointer IAvaloniaPossessionHost.PossessionPointer => _possessionPointer;

        Rect? IAvaloniaPossessionHost.BoundsOnGhostLayer(Control element)
        {
            try
            {
                Visual target = (Visual?)_possessionGhostLayer ?? this;
                if (element == null || !element.IsEffectivelyVisible) return null;
                if (element.Bounds.Width <= 0 || element.Bounds.Height <= 0) return null;
                if (element.TransformToVisual(target) is not { } m) return null;
                var r = new Rect(element.Bounds.Size).TransformToAABB(m);
                return double.IsNaN(r.X) || double.IsNaN(r.Y) ? null : r;
            }
            catch { return null; }
        }

        /// <summary>WPF IsUsable: "can the room be haunted right this second". Conservative: a false costs one
        /// skipped pick; a true behind a takeover paints an ember ripple nobody can see. Gates STARTING only.</summary>
        bool IPossessionHost.IsUsable
        {
            get
            {
                try
                {
                    if (!IsLoaded || !IsVisible || WindowState == WindowState.Minimized) return false;
                    // A window that has taken over the screen (every web game room, the intake host, the
                    // Emergency Exit minigame, a Chaos run) or the Lock Card holding the user's input.
                    if (Games.GameWindow.IsAnyOpen()) return false;
                    if (IntakeHostWindow.IsAnyOpen()) return false;
                    if (EmergencyExitWindow.Current != null) return false;
                    if (Chaos.ChaosRunHost.IsActive) return false;
                    if (LockCardWindow.IsAnyOpen()) return false;
                    return true;
                }
                catch { return false; }   // a throwing check reads as "not now", never as "go ahead"
            }
        }

        // ==== A1 auto-tag ===============================================================

        private List<PossessionTarget>? _possessionTargetCache;
        private DateTime _possessionCacheAt = DateTime.MinValue;
        private bool _possessionCacheDirty = true;
        private DateTime _possessionLayoutSeenAt = DateTime.MinValue;

        private static readonly TimeSpan PossessionRebuildFloor = TimeSpan.FromMilliseconds(750);
        private static readonly TimeSpan PossessionCacheMaxAge = TimeSpan.FromSeconds(10);

        /// <summary>WPF PossessionNeverNames, verbatim: never a victim, subtree and all, unless hand-tagged.</summary>
        internal static readonly HashSet<string> PossessionNeverNames = new(StringComparer.Ordinal)
        {
            "TxtLockdownTimer", "TxtLockdownExit", "BtnEmergencyExit", "EERoot",
            "LockdownGate", "TxtPossessionRung", "PossessionPips", "TxtXP",
            "EmiDockChip",
        };

        internal const int MaxAutoLabels = 24;
        private const double MinTargetPx = 8;

        IReadOnlyList<PossessionTarget> IPossessionHost.Targets => GetPossessionTargets();

        /// <summary>Forces the next read to walk again (tests; WPF marks dirty from LayoutUpdated).</summary>
        internal void InvalidatePossessionTargets() { _possessionTargetCache = null; _possessionCacheDirty = true; }

        internal IReadOnlyList<PossessionTarget> GetPossessionTargets()
        {
            // Off the UI thread the walk is illegal: hand back the last good snapshot (ccp-bugs #1160).
            if (!Dispatcher.UIThread.CheckAccess())
            {
                _possessionCacheDirty = true;
                return _possessionTargetCache ?? (IReadOnlyList<PossessionTarget>)Array.Empty<PossessionTarget>();
            }
            try
            {
                var now = DateTime.Now;
                var age = now - _possessionCacheAt;
                var stale = _possessionTargetCache == null || age > PossessionCacheMaxAge
                            || (_possessionCacheDirty && age > PossessionRebuildFloor);
                if (!stale) return _possessionTargetCache!;

                var found = new List<PossessionTarget>();
                var labels = new List<PossessionTarget>();
                if (Named<Grid>("RootGrid") is { } root) WalkPossession(root, found, labels, 0, 17);
                if (labels.Count > MaxAutoLabels) TrimLabels(labels);
                found.AddRange(labels);
                _possessionTargetCache = found;
                _possessionCacheAt = now;
                _possessionCacheDirty = false;
                return found;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Possession: target walk failed");
                return _possessionTargetCache ?? (IReadOnlyList<PossessionTarget>)Array.Empty<PossessionTarget>();
            }
        }

        private void TrimLabels(List<PossessionTarget> labels)
        {
            try
            {
                var origin = _possessionPointer.Position;
                bool haveCursor = origin.X > 0 || origin.Y > 0;
                double Score(PossessionTarget t)
                {
                    if (t.Visual is not Control c || !TryWindowBounds(c, out var r)) return double.MaxValue;
                    if (!haveCursor) return -(r.Width * r.Height);              // biggest first
                    var dx = r.X + r.Width / 2 - origin.X;
                    var dy = r.Y + r.Height / 2 - origin.Y;
                    return dx * dx + dy * dy;                                   // nearest first
                }
                labels.Sort((a, b) => Score(a).CompareTo(Score(b)));
            }
            catch { }
            if (labels.Count > MaxAutoLabels) labels.RemoveRange(MaxAutoLabels, labels.Count - MaxAutoLabels);
        }

        private readonly record struct PossessionSubtree(bool Interactive, bool Card);

        private PossessionSubtree WalkPossession(Visual node, List<PossessionTarget> into, List<PossessionTarget> labels, int depth, int pathHash)
        {
            if (node == null || depth > 64) return default;
            if (ReferenceEquals(node, _possessionGhostLayer) || ReferenceEquals(node, _possessionRubbleFloor)) return default;

            var c = node as Control;
            var handRole = PossessionRole.None;
            if (c != null)
            {
                if (!c.IsVisible) return default;                    // a collapsed tab prunes its whole subtree
                if (PossessionTree.GetExclude(c)) return default;
                handRole = PossessionTree.GetRole(c);                // hand tags win, then the never-names
                if (handRole == PossessionRole.None && !string.IsNullOrEmpty(c.Name) && PossessionNeverNames.Contains(c.Name)) return default;
            }

            var role = handRole;
            if (c != null && role == PossessionRole.None) role = InferLeafRole(c);
            var leaf = role != PossessionRole.None && role != PossessionRole.Card && role != PossessionRole.Scroll;
            var interactive = IsInteractiveRole(role);

            PossessionSubtree below = default;
            if (!leaf)
            {
                int i = 0;
                foreach (var child in node.GetVisualChildren())
                {
                    var h = unchecked(pathHash * 31 + i * 7 + child.GetType().Name.Length);
                    var r = WalkPossession(child, into, labels, depth + 1, h);
                    if (r.Interactive) below = below with { Interactive = true };
                    if (r.Card) below = below with { Card = true };
                    i++;
                }
            }
            if (c == null) return below;

            // The card heuristic, bottom-up so "innermost wins" is decidable.
            if (role == PossessionRole.None && below.Interactive && !below.Card && IsCardBorder(c)) role = PossessionRole.Card;

            if (role != PossessionRole.None && (handRole != PossessionRole.None || PassesSizeAndPlacement(c)))
            {
                if (PossessionTargetFor(c, role, pathHash) is { } t)
                {
                    if (role == PossessionRole.Label && handRole == PossessionRole.None) labels.Add(t);
                    else into.Add(t);
                    if (role == PossessionRole.Card) below = below with { Card = true };
                }
            }
            return new PossessionSubtree(interactive || below.Interactive, below.Card);
        }

        private PossessionTarget? PossessionTargetFor(Control c, PossessionRole role, int pathHash)
        {
            try
            {
                if (_possessionTargets.TryGetValue(c, out var existing)) return existing;
                var target = new PossessionTarget
                {
                    Visual = c,
                    Role = role,
                    Key = !string.IsNullOrEmpty(c.Name) ? c.Name! : role + "#" + pathHash.ToString("x8"),
                    DisplayName = PossessionDisplayNameFor(c, role),
                };
                _possessionTargets.Add(c, target);
                return target;
            }
            catch { return null; }
        }

        /// <summary>WPF InferLeafRole over the Avalonia control types.</summary>
        internal static PossessionRole InferLeafRole(Control c)
        {
            if (c is ToggleButton) return PossessionRole.Toggle;   // CheckBox, RadioButton, ToggleSwitch
            if (c is Button)
            {
                if (c is RepeatButton && IsInsideScrollBar(c)) return PossessionRole.None;
                return PossessionRole.Button;
            }
            if (c is Slider) return PossessionRole.Slider;
            if (c is ComboBox) return PossessionRole.Combo;
            if (c is TextBox) return PossessionRole.TextBox;
            if (c is ProgressBar) return PossessionRole.Progress;
            if (c is ScrollViewer sv)
            {
                double scrollable;
                try { scrollable = sv.Extent.Height - sv.Viewport.Height; } catch { scrollable = 0; }
                return scrollable > 0 ? PossessionRole.Scroll : PossessionRole.None;
            }
            if (c is Image img)
                return img.Bounds.Width >= 48 && img.Bounds.Height >= 48 ? PossessionRole.Image : PossessionRole.None;
            if (c is TextBlock tb)
            {
                double size;
                try { size = tb.FontSize; } catch { size = 0; }
                if (size >= 18) return PossessionRole.Title;
                var text = tb.Text;
                if (string.IsNullOrWhiteSpace(text)) return PossessionRole.None;
                return text.Trim().Length is >= 3 and <= 40 ? PossessionRole.Label : PossessionRole.None;
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

        /// <summary>WPF IsCardBorder: a rounded Border that paints something, at least 60x60, holding a control.</summary>
        private static bool IsCardBorder(Control c)
        {
            try
            {
                if (c is not Border b || b.Background == null) return false;
                var cr = b.CornerRadius;
                if (cr.TopLeft <= 0 && cr.TopRight <= 0 && cr.BottomLeft <= 0 && cr.BottomRight <= 0) return false;
                return b.Bounds.Height >= 60 && b.Bounds.Width >= 60;
            }
            catch { return false; }
        }

        private bool PassesSizeAndPlacement(Control c)
        {
            try
            {
                if (!c.IsEffectivelyEnabled || !c.IsHitTestVisible) return false;
                // WPF IsHitTestVisible is effective (a passive ancestor makes the subtree passive); Avalonia's is local.
                for (Visual? a = c.GetVisualParent(); a != null; a = a.GetVisualParent())
                    if (a is global::Avalonia.Input.InputElement { IsHitTestVisible: false }) return false;
                if (c.Bounds.Width <= 0 || c.Bounds.Height <= 0) return false;
                if (!TryWindowBounds(c, out var r)) return false;
                if (r.Width < MinTargetPx || r.Height < MinTargetPx) return false;
                var w = Bounds.Width;
                var h = Bounds.Height;
                if (w <= 0 || h <= 0) return false;
                return r.Right > 0 && r.Bottom > 0 && r.Left < w && r.Top < h;   // not fully off-screen
            }
            catch { return false; }
        }

        /// <summary>The control's rectangle in WINDOW coordinates (every scale above it included).</summary>
        internal bool TryWindowBounds(Control c, out Rect bounds)
        {
            bounds = default;
            try
            {
                if (c.TransformToVisual(this) is not { } m) return false;
                bounds = new Rect(c.Bounds.Size).TransformToAABB(m);
                return !double.IsNaN(bounds.X) && !double.IsNaN(bounds.Y);
            }
            catch { return false; }
        }

        /// <summary>WPF DisplayNameFor: hand name, then string content, text, tooltip, automation name, then the
        /// role's own noun. Untranslated by design (the bark packs own the sentence around it).</summary>
        internal static string PossessionDisplayNameFor(Control c, PossessionRole role)
        {
            try
            {
                var hand = PossessionTree.GetName(c);
                if (!string.IsNullOrWhiteSpace(hand)) return hand;
                var text = Tidy(c is ContentControl cc ? cc.Content as string : null)
                           ?? Tidy(c is TextBlock tb ? tb.Text : null)
                           ?? Tidy(ToolTip.GetTip(c) as string)
                           ?? Tidy(AutomationProperties.GetName(c));
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
            return s.Length is < 2 or > 32 ? null : s;
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

        // ==== Lockdown readout + first-run rules (WPF MainWindow.Lab.cs:688-690, 720, 871-986, 1100) ====

        private static readonly Color PossessionEmber = Color.Parse("#FF8A5C");
        private static readonly Color PossessionEmberDim = Color.Parse("#33FF8A5C");

        /// <summary>Stored so the unsubscribe removes the SAME delegate it added (WPF Lab.cs:879).</summary>
        private Action<PossessionRung>? _possessionRungHandler;
        private PossessionDirector? _possessionRungSource;

        private void HookPossessionLockdown()
        {
            void Activated() => Dispatcher.UIThread.Post(() =>
            {
                if (_lockdownVisualsClosed) return;
                AttachPossessionHost();
                HookPossessionReadout();
                ShowPossessionRulesIfFirstTime();
            });
            void Deactivated() => Dispatcher.UIThread.Post(UnhookPossessionReadout);
            if (LockdownService.Current is not { } ld) return;
            ld.LockdownActivated += Activated;
            ld.LockdownDeactivated += Deactivated;
            Closed += (_, _) =>
            {
                ld.LockdownActivated -= Activated;
                ld.LockdownDeactivated -= Deactivated;
                DetachPossessionRungHandler();
            };
        }

        private Tabs.LockdownTabView? LockdownTabPage => Named<Tabs.LockdownTabView>("LockdownTab");

        /// <summary>WPF HookPossessionReadout: drop any stale subscription FIRST (unsubscribe only - the
        /// combined unhook used to blank the row it had just painted), show the row when Possession is on,
        /// paint Settle (the director raises RungChanged only when the rung MOVES) and follow it.</summary>
        internal void HookPossessionReadout()
        {
            try
            {
                DetachPossessionRungHandler();
                var on = CoreSettings.Service?.Current?.LockdownPossessionEnabled == true;
                if (LockdownTabPage is { } page)
                {
                    page.TxtPossessionRung.IsVisible = on;
                    page.PossessionPips.IsVisible = on;
                }
                if (!on) return;
                UpdatePossessionReadout(PossessionRung.Settle);
                if (Director is not { } director) return;
                // The paint is queued; one that lands after the exit unhooked the row must not show it again
                // (the reassembly raises Settle as it finishes, which can queue behind the unhook).
                _possessionRungHandler = rung => Dispatcher.UIThread.Post(() =>
                {
                    if (_possessionRungSource != null) UpdatePossessionReadout(rung);
                });
                _possessionRungSource = director;
                director.RungChanged += _possessionRungHandler;
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Possession: failed to hook the rung readout"); }
        }

        private void DetachPossessionRungHandler()
        {
            try
            {
                if (_possessionRungHandler != null && _possessionRungSource != null)
                    _possessionRungSource.RungChanged -= _possessionRungHandler;
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Possession: failed to detach the rung handler"); }
            _possessionRungHandler = null;
            _possessionRungSource = null;
        }

        internal void UnhookPossessionReadout()
        {
            try
            {
                DetachPossessionRungHandler();
                if (LockdownTabPage is not { } page) return;
                page.TxtPossessionRung.Text = "";
                page.TxtPossessionRung.IsVisible = false;
                foreach (var child in page.PossessionPips.Children)
                    if (child is Border pip) pip.Background = new SolidColorBrush(PossessionEmberDim);
                page.PossessionPips.IsVisible = false;
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Possession: failed to clear the rung readout"); }
        }

        /// <summary>WPF UpdatePossessionReadout: the word plus five pips, lit up to the rung. Paint implies show.</summary>
        internal void UpdatePossessionReadout(PossessionRung rung)
        {
            try
            {
                if (LockdownTabPage is not { } page) return;
                var index = (int)rung;
                page.TxtPossessionRung.Text = Loc.GetF("lockdown_poss_readout_fmt", Loc.Get("lockdown_poss_rung_" + index));
                page.TxtPossessionRung.IsVisible = true;
                page.PossessionPips.IsVisible = true;
                for (int i = 0; i < page.PossessionPips.Children.Count; i++)
                    if (page.PossessionPips.Children[i] is Border pip)
                        pip.Background = new SolidColorBrush(i <= index ? PossessionEmber : PossessionEmberDim);
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Possession: failed to paint the rung readout"); }
        }

        /// <summary>WPF ShowPossessionRulesIfFirstTime (POSSESSION.md decision 7): card AND bark. Through the
        /// presenter, so it is an Inbox row rather than an ambush mid-session; the flag is spent when the card
        /// OPENS (a row waved away unread used to burn the only offer of the rules).</summary>
        internal void ShowPossessionRulesIfFirstTime()
        {
            try
            {
                var s = CoreSettings.Service?.Current;
                if (s == null || !s.LockdownPossessionEnabled || s.LockdownPossessionIntroSeen) return;
                Platform.StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
                {
                    Key = "intro:possession",
                    Glyph = global::ConditioningControlPanel.Avalonia.Controls.IconMap.PossessionRules,
                    Title = "The warden's rules",
                    Summary = "What possession does before the room starts moving.",
                    Open = () =>
                    {
                        var live = CoreSettings.Service?.Current;
                        if (live != null && !live.LockdownPossessionIntroSeen)
                        {
                            live.LockdownPossessionIntroSeen = true;
                            CoreSettings.Save();
                        }
                        FeatureIntroPopup.ShowIfFirstTime("possession", this);
                    },
                });
                PossessionAvaloniaHead.BarkRules();
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Possession: failed to show the first-run rules"); }
        }
    }
}
