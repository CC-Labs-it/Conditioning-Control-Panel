// PORTED from ConditioningControlPanel/MainWindow/MainWindow.NavRail.cs (the section rail, nav
// rework 2026-10-06: cd426fe36, 8b67e45d2, ac59c3344). An always-labelled rail of seven sections and
// a Settings gear inside the 96px column; no flyout, no hold latch, no door accordion. A row opens its
// section's last tab; the lit row follows every navigation (RefreshSectionRail, from ShowTab).
//
// sync6-nav-rail-c added the edge spur, the art tint (navtint), the label glow, the 160 ms halo
// fade, NavBadges pills, the rich row tooltip and ChromeFx's hover nudge (NudgeNavIcon).
// ponytail: not yet here: the coin depth (BuildNavCoin, press travel, tilt, specular), the
// rail shadow (PaintDepthRail) - lane
// wire-shell-nav-rail-b; the possession reroute seam (WPF PossessionReroute, written only by
// Services/Possession/Effects/MisrouteEffect.cs - that effect has no writer on this head). Mod door art (ApplyDoorArt) landed in sync6-nav-rail-d. No Lobby producer feeds NavBadges here yet, so the
// Social pill stays down until the Lobby service lands.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>One section row's parts, found once by type/Tag inside the row's Grid.</summary>
        internal sealed class NavSectionRow
        {
            internal string Section = "";
            internal Button Button = null!;
            internal Ellipse? Glow;
            internal Border? Tile, Ring, Shade, Art, Tint, Badge, Spur;
            internal TextBlock? Label, BadgeText;
            internal Color Hue;
            internal bool Active, Painted;
        }

        // WPF NavRailRules: row tint 0x33, tile tint 0x40, ring idle/hover/active alpha, lit halo.
        private const byte NavFillAlpha = 0x33, NavTileTintAlpha = 0x40;
        private const double NavGlowActive = 0.55;
        // WPF NavRailRules: the art wash (lit ~5%, idle none), the spur (90% edge, 60% ring),
        // the badge cap and its seen opacity; NavRail.cs: the 160 ms halo fade; ChromeFx: the nudge.
        internal const byte NavArtTintActiveAlpha = 0x0D, NavSpurEdgeAlpha = 0xE6, NavSpurRingAlpha = 0x99;
        internal const int NavGlowFadeMs = 160, NavBadgeCap = 9, NavIconHoverMs = 150;
        internal const double NavBadgeSeenOpacity = 0.45, NavIconHoverScale = 1.06;

        /// <summary>WPF NavRailRules.Ms: Full as authored, Reduced halves it, Off is instant.</summary>
        internal static int NavMs(int fullMs) => AmbientFxCanvas.Env.Level switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => fullMs / 2,
            _ => fullMs,
        };

        /// <summary>WPF NavRailRules.BadgeText: no pill at 0, "9+" past the cap.</summary>
        internal static string? NavBadgeText(int count) =>
            count <= 0 ? null : count > NavBadgeCap ? NavBadgeCap + "+" : count.ToString(CultureInfo.CurrentCulture);

        private readonly List<NavSectionRow> _navSectionRows = new();
        private bool _navRailReady;

        internal IReadOnlyList<NavSectionRow> NavSectionRowsForTests => _navSectionRows;

        /// <summary>Every rail button, top to bottom, gear last (WPF NavSectionButtons).</summary>
        private static readonly string[] NavSectionButtonNames =
            { "DoorHome", "DoorStudio", "DoorCompanion", "DoorPlay", "DoorSocial", "DoorYou", "DoorLibrary", "DoorSettings" };

        /// <summary>WPF InitializeNavRail (:110). Called from the constructor after XAML load; a rail
        /// that fails here stays as authored: rows that navigate, no hue, no lit row.</summary>
        internal void InitializeNavRail()
        {
            try { RefreshNavPremiumTags(); }   // repaints the favorites chips (MainShellWindow.NavPremiumTags.cs)
            catch (Exception ex) { Log.Debug("RefreshNavPremiumTags: {E}", ex.Message); }
            try
            {
                if (_navRailReady) return;
                CacheNavSectionRows();
                RegisterSectionShortcuts();
                // WPF :122. Per-window subscriber, so it leaves with the window (P41/P68).
                NavBadges.Changed += OnNavBadgeChanged;
                Closed += (_, _) => NavBadges.Changed -= OnNavBadgeChanged;
                foreach (var row in _navSectionRows) PaintNavBadge(row, NavBadges.Get(row.Section));
                // WPF :118 + MainWindow.xaml.cs:3653: the active mod's medallions, again on a mod switch.
                ApplyDoorArt();
                ApplyModToNavRailLabels();
                Closed += (_, _) => { _navLabelsClosed = true; ClearNavLabelBindings(); };
                CoreMods.ModChanged += OnModChangedDoorArt;
                Closed += (_, _) => CoreMods.ModChanged -= OnModChangedDoorArt;
                _navRailReady = true;
                RefreshSectionRail(CurrentTab);
            }
            catch (Exception ex) { Log.Warning(ex, "InitializeNavRail failed; rail stays as authored"); }
        }

        private void OnModChangedDoorArt(object? sender, Models.ModPackage e) => Dispatcher.UIThread.Post(() =>
        {
            if (_navLabelsClosed) return;
            ApplyDoorArt();
            ApplyModToNavRailLabels();
        });

        /// <summary>Decode cap for a medallion: 64px native art in a 40px icon, 2x headroom (WPF :816).</summary>
        private const int NavDoorArtDecodeWidth = 128;

        /// <summary>WPF ApplyDoorArt (:825): points the medallions at the active mod's art. The paths
        /// (nav/door_*.png) are the mod compatibility surface. A null resolve leaves the authored
        /// Source alone. Social's XAML placeholder (the Lobby picture under a shade) gives way to the
        /// shipped or modded nav/door_social.png, and the shade goes with it. ModArt also tries
        /// per-mod embedded twins (nav/mods/&lt;mod-id&gt;/) before the default art.</summary>
        internal void ApplyDoorArt()
        {
            try
            {
                foreach (var key in new[] { "home", "studio", "companion", "play", "social", "you", "library", "settings" })
                {
                    var img = Named<Image>("ImgDoor" + char.ToUpperInvariant(key[0]) + key.Substring(1));
                    if (img == null) continue;
                    var art = Helpers.ModArt.TryLoad($"nav/door_{key}.png", NavDoorArtDecodeWidth);
                    if (art != null) img.Source = art;
                    if (key == "social" && Named<Border>("ShadeDoorSocial") is { } shade) shade.IsVisible = art == null;
                }
                var glow = AmbientFxCanvas.Env.GlowColor;
                foreach (var row in _navSectionRows)
                    if (row.Label?.Effect is DropShadowEffect fx) fx.Color = glow;
            }
            catch (Exception ex) { Log.Warning(ex, "ApplyDoorArt failed; nav rail keeps its embedded medallions"); }
        }

        /// <summary>WPF CacheNavSectionRows (:139): hue from the one table (NavStripTable.AccentRgb),
        /// the label bound upper-cased to the section's own key (WPF NavCapsConverter).</summary>
        private void CacheNavSectionRows()
        {
            _navSectionRows.Clear();
            foreach (var name in NavSectionButtonNames)
            {
                if (Named<Button>(name) is not { Tag: string tag } btn) continue;
                var section = tag == "appsettings" ? NavSections.Settings : tag;
                var rgb = NavStripTable.AccentRgb(section);
                var row = new NavSectionRow
                {
                    Section = section,
                    Button = btn,
                    Hue = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb),
                };
                btn.Background = new SolidColorBrush(WithAlpha(row.Hue, NavFillAlpha));
                btn.BorderBrush = new SolidColorBrush(row.Hue);
                if (btn.Content is Panel grid)
                {
                    foreach (var child in grid.Children)
                    {
                        switch (child)
                        {
                            case Ellipse e: row.Glow = e; break;
                            case Border b when (b.Tag as string) == "navring": row.Ring = b; break;
                            case Border b when (b.Tag as string) == "navart": row.Art = b; break;
                            case Border b: row.Tile ??= b; break;
                            case TextBlock t: row.Label = t; break;
                        }
                    }
                }
                row.Shade = Named<Border>("Shade" + name);
                if (btn.Content is Panel face) AddNavRowParts(row, face);
                if (row.Glow != null)
                    row.Glow.Fill = new RadialGradientBrush
                    {
                        GradientStops = { new GradientStop(WithAlpha(row.Hue, 0x99), 0), new GradientStop(WithAlpha(row.Hue, 0), 1) },
                    };
                if (row.Glow != null) row.Glow.Transitions = NavFade();
                if (row.Tile != null) row.Tile.Background = new SolidColorBrush(WithAlpha(row.Hue, NavTileTintAlpha));
                // WPF :187: a quiet, static halo behind the label in the mod's glow colour.
                if (row.Label != null)
                    row.Label.Effect = new DropShadowEffect
                    {
                        Color = AmbientFxCanvas.Env.GlowColor, BlurRadius = 6, OffsetX = 0, OffsetY = 0, Opacity = 0.45,
                    };
                var captured = row;
                btn.PointerEntered += (_, _) => { PaintNavRing(captured, hover: true); NudgeNavIcon(captured, true); };
                btn.PointerExited += (_, _) => { PaintNavRing(captured, hover: false); NudgeNavIcon(captured, false); };
                ToolTip.AddToolTipOpeningHandler(btn, (_, _) => ToolTip.SetTip(btn, BuildNavRowToolTip(captured)));
                PaintNavRowActive(row);
                _navSectionRows.Add(row);
            }
        }

        private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

        /// <summary>A row press: the section's last tab, else its default (WPF NavDoor_Click :568).</summary>
        private void NavDoor_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag }) return;
            OpenNavSection(tag == "appsettings" ? NavSections.Settings : tag);
        }

        /// <summary>WPF OpenNavSection (:575): opens a section the way its rail row does; logs and
        /// stays on an unknown key.</summary>
        internal void OpenNavSection(string section)
        {
            if (NavSections.Find(section) is null)
            {
                Log.Warning("OpenNavSection: no section {Section}", section);
                return;
            }
            ShowTab(section == NavSections.Settings ? "appsettings" : NavLastTabFor(section));
        }

        /// <summary>WPF RegisterSectionShortcuts (:589): Ctrl+1..7 jump to the rail's sections.
        /// Window KeyBindings, so they fire only while this window has focus.</summary>
        private void RegisterSectionShortcuts()
        {
            int n = 0;
            foreach (var s in NavSections.Order)
            {
                if (s.Key == NavSections.Settings || ++n > 7) continue;
                var section = s.Key;
                KeyBindings.Add(new KeyBinding
                {
                    Gesture = new KeyGesture(Key.D0 + n, KeyModifiers.Control),
                    Command = new NavSectionCommand(() => OpenNavSection(section)),
                });
            }
        }

        private sealed class NavSectionCommand(Action run) : System.Windows.Input.ICommand
        {
            public event EventHandler? CanExecuteChanged { add { } remove { } }
            public bool CanExecute(object? parameter) => true;
            public void Execute(object? parameter) => run();
        }

        /// <summary>
        /// WPF RefreshSectionRail (:608): lights the row that owns <paramref name="tabKey"/> - filled
        /// row, 4px bar and label in the section hue, full ring, halo. Called from ShowTab on every
        /// navigation, so the lit row follows deep links, the palette and the strip. A key no section
        /// owns leaves the current row lit. Repaints only rows whose state changed (P07).
        /// </summary>
        internal void RefreshSectionRail(string? tabKey)
        {
            if (!_navRailReady) return;
            var section = NavSections.SectionForTab(tabKey == "lab" ? "play" : tabKey);
            if (section == null) return;
            foreach (var row in _navSectionRows)
            {
                bool on = row.Section == section;
                if (row.Painted && on == row.Active) continue;
                row.Active = on;
                PaintNavRowActive(row);
            }
            try { NavBadges.MarkSeen(section); } catch { }   // WPF :622: being on the section is seeing its count
        }

        private static void PaintNavRowActive(NavSectionRow row)
        {
            row.Painted = true;
            row.Button.Classes.Set("active", row.Active);
            if (row.Shade != null) row.Shade.Opacity = row.Active ? 0 : 1;
            if (row.Label != null)
            {
                if (row.Active) row.Label.Foreground = new SolidColorBrush(row.Hue);
                else row.Label.ClearValue(TextBlock.ForegroundProperty);
            }
            if (row.Glow != null)
            {
                // WPF :656: the halo fades over 160 ms (Reduced 80, Off snaps).
                row.Glow.Transitions = NavFade();
                row.Glow.Opacity = row.Active ? NavGlowActive : 0;
            }
            if (row.Tint != null)
                row.Tint.Background = new SolidColorBrush(WithAlpha(row.Hue, row.Active ? NavArtTintActiveAlpha : (byte)0));
            PaintNavSpur(row);
            PaintNavRing(row, hover: row.Button.IsPointerOver);
        }

        /// <summary>The opacity fade the halo and spur share, or none at Motion Off.</summary>
        private static Transitions? NavFade()
        {
            int ms = NavMs(NavGlowFadeMs);
            return ms <= 0 ? null : new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms) } };
        }

        /// <summary>The parts WPF authors per row (MainWindow.xaml:498-506), built once in code: the hue
        /// wash over the art (navtint), the count pill on the tile corner (navbadge) and the edge spur
        /// (template part NavEdgeSpur: 20x8 at top 25, under everything).</summary>
        private static void AddNavRowParts(NavSectionRow row, Panel face)
        {
            var kids = face.Children;
            row.Spur = new Border
            {
                Width = 20, Height = 8, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 24, 0, 0), CornerRadius = new CornerRadius(0, 4, 4, 0),
                IsVisible = false, Opacity = 0, IsHitTestVisible = false,
                Background = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(WithAlpha(row.Hue, NavSpurEdgeAlpha), 0), new GradientStop(WithAlpha(row.Hue, NavSpurRingAlpha), 1) },
                },
            };
            kids.Insert(0, row.Spur);
            row.Tint = new Border { Tag = "navtint", Width = 52, Height = 52, CornerRadius = new CornerRadius(13), IsHitTestVisible = false };
            kids.Insert(row.Art is { } art ? kids.IndexOf(art) + 1 : kids.Count, row.Tint);
            row.BadgeText = new TextBlock
            {
                FontSize = 9.5, FontWeight = FontWeight.ExtraBold, Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            };
            row.Badge = new Border
            {
                Tag = "navbadge", IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 18, 0), MinWidth = 16, Height = 16, Padding = new Thickness(4, 0),
                CornerRadius = new CornerRadius(8), BorderBrush = new SolidColorBrush(Color.FromUInt32(0xE6101020)),
                BorderThickness = new Thickness(1.5), IsHitTestVisible = false, Child = row.BadgeText,
            };
            kids.Add(row.Badge);
        }

        /// <summary>WPF PaintNavSpur (:719): the lit row's hue tab from the window edge into its ring,
        /// faded in over the halo's timing; hidden at once on every other row.</summary>
        private static void PaintNavSpur(NavSectionRow row)
        {
            if (row.Spur is not { } spur) return;
            spur.Transitions = null;
            spur.Opacity = 0;
            spur.IsVisible = row.Active;
            if (!row.Active) return;
            spur.Transitions = NavFade();
            spur.Opacity = 1;
        }

        /// <summary>WPF ChromeFx NudgeNavIcon (:469): the medallion art grows 6% on hover over 150 ms;
        /// the button itself keeps its hover fill. Motion Off snaps.</summary>
        private static void NudgeNavIcon(NavSectionRow row, bool on)
        {
            if (row.Art is not { } icon) return;
            int ms = AmbientFxCanvas.Env.AllowTransitions ? NavIconHoverMs : 0;
            icon.RenderTransformOrigin = RelativePoint.Center;
            icon.Transitions = ms <= 0 ? null : new Transitions
            {
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = new global::Avalonia.Animation.Easings.QuadraticEaseOut() },
            };
            icon.RenderTransform = TransformOperations.Parse(on ? FormattableString.Invariant($"scale({NavIconHoverScale})") : "scale(1)");
        }

        private void OnNavBadgeChanged(string section, int count)
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => OnNavBadgeChanged(section, count));
                return;
            }
            foreach (var row in _navSectionRows)
            {
                if (row.Section != section) continue;
                // A count that rises while the player is looking at its section is seen (WPF :785).
                if (row.Active && IsVisible && WindowState != WindowState.Minimized) NavBadges.MarkSeen(section);
                PaintNavBadge(row, NavBadges.Get(section));
            }
        }

        /// <summary>WPF PaintNavBadge (:797): the pill wears the section hue with contrast ink, and dims
        /// once the player has seen the count (NavBadges.IsFresh).</summary>
        private static void PaintNavBadge(NavSectionRow row, int count)
        {
            if (row.Badge is not { } badge) return;
            var text = NavBadgeText(count);
            row.BadgeText!.Text = text ?? "";
            badge.IsVisible = text != null;
            if (text == null) return;
            badge.Background = new SolidColorBrush(row.Hue);
            row.BadgeText.Foreground = new SolidColorBrush(NavStripRules.ActiveTextOn(row.Hue));
            bool fresh = NavBadges.IsFresh(count, NavBadges.Seen(row.Section));
            badge.Opacity = fresh ? 1.0 : NavBadgeSeenOpacity;
            // WPF MainWindow.xaml:133: a raised chip (lamp top-left, shadow down-right); a seen badge drops it.
            badge.Effect = fresh ? new DropShadowEffect { Color = Color.FromRgb(0x06, 0x02, 0x0F), OffsetX = 1.06, OffsetY = 1.06, BlurRadius = 3, Opacity = 0.62 } : null;
        }

        /// <summary>WPF BuildNavRowToolTip (:501): the section name, the open count when a badge is up
        /// and a greyed "Ctrl+N". Built on open, so a language switch never leaves it stale.</summary>
        private static object BuildNavRowToolTip(NavSectionRow row)
        {
            var panel = new StackPanel();
            var name = row.Label?.Text ?? row.Section;
            if (!string.IsNullOrEmpty(name))
                name = name.Substring(0, 1) + (name.Length > 1 ? name.Substring(1).ToLowerInvariant() : "");
            panel.Children.Add(new TextBlock { Text = name, FontWeight = FontWeight.SemiBold });
            int count = NavBadges.Get(row.Section);
            if (count > 0) panel.Children.Add(new TextBlock { Text = Loc.GetF("nav_badge_open", count) });
            int n = NavShortcutNumber(row.Section);
            if (n > 0) panel.Children.Add(new TextBlock { Text = "Ctrl+" + n, Opacity = 0.6, FontSize = 11 });
            return panel;
        }

        /// <summary>WPF NavRailRules.ShortcutNumber: the 1-based rail position, 0 for the gear.</summary>
        internal static int NavShortcutNumber(string section)
        {
            int n = 0;
            foreach (var s in NavSections.Order)
            {
                if (s.Key == NavSections.Settings) continue;
                n++;
                if (s.Key == section) return n;
            }
            return 0;
        }

        /// <summary>WPF PaintNavRingParts: vivid diagonal bevel, reversed when selected.</summary>
        private static void PaintNavRing(NavSectionRow row, bool hover)
        {
            var target = row.Ring ?? row.Tile;
            if (target is null) return;
            if (row.Tile is not null) row.Tile.Opacity = 1;
            var gradient = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            };
            foreach (var stop in NavRailPaintRules.RingStops(row.Hue.ToUInt32(), row.Active, hover))
                gradient.GradientStops.Add(new GradientStop(Color.FromUInt32(stop.Color), stop.Offset));
            target.BorderBrush = gradient;
            target.BorderThickness = new Thickness(NavRailPaintRules.RingThickness(row.Active));
        }

        /// <summary>The rail's search pill, one line as in WPF (:559). Toggle refuses during Lockdown.</summary>
        private void BtnNavSearch_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
            => SettingsPaletteWindow.Toggle(this);
    }
}
