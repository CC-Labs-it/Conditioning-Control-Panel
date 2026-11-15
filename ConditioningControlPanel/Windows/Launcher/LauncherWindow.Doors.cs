using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Launcher;

/// <summary>
/// The doors (declutter wave 2). The right column opens on two cards and no tiles: GAMES and
/// TOOLS. A door opens its page, which is the old scroller and grid filled with that door's
/// listed cards under a Back link. What a door counts, stamps and continues is decided in
/// <see cref="LauncherDoorRules"/>; this file only paints and asks.
///
/// <para>Rails: the swap is two plain opacity fades (an OUT, then an IN) lengthened by
/// <see cref="GentleVisuals"/> and skipped when transitions are off; no Effect on a door card;
/// the collage is a handful of small stills at low opacity, never a clock.</para>
/// </summary>
public partial class LauncherWindow
{
    private const int DoorSwapMs = 130;
    private const double DoorRadius = 20;

    /// <summary>Null while the doors are showing; otherwise the door whose page is up.</summary>
    private LauncherDoor? _door;
    private bool _doorSwapping;

    /// <summary>The cards <see cref="BuildTiles"/> lays out: the open door's page, or none.</summary>
    private IReadOnlyList<LauncherEntry> PageEntries() =>
        _door is { } door ? LauncherCatalogue.Page(door) : Array.Empty<LauncherEntry>();

    private static string DoorTitle(LauncherDoor door) =>
        Loc.Get(door == LauncherDoor.Tools ? "launcher_door_tools" : "launcher_section_games");

    private static Color DoorHue(LauncherDoor door) =>
        door == LauncherDoor.Tools ? Color.FromRgb(0x55, 0xB7, 0xFF) : Color.FromRgb(0xFF, 0x69, 0xB4);

    /// <summary>Puts the column in the state <see cref="_door"/> names, no motion. Called after
    /// every <see cref="BuildTiles"/>, so a mod switch or a sign-in repaints the doors too.</summary>
    private void ApplyDoorView()
    {
        try
        {
            bool page = _door != null;
            DoorPageHeader.Visibility = page ? Visibility.Visible : Visibility.Collapsed;
            GamesScroller.Visibility = page ? Visibility.Visible : Visibility.Collapsed;
            DoorsHost.Visibility = page ? Visibility.Collapsed : Visibility.Visible;
            if (_door is { } door)
            {
                DoorBack.Content = "‹ " + Loc.Get("btn_back");
                GamesEyebrow.Text = DoorTitle(door).ToUpperInvariant();
                DoorsHost.Children.Clear();
                MarkDoorSeen(door);
            }
            else
            {
                DoorsHost.Children.Clear();
                DoorsHost.Children.Add(BuildDoorCard(LauncherDoor.Games));
                DoorsHost.Children.Add(BuildDoorCard(LauncherDoor.Tools));
            }
        }
        catch (Exception ex) { Log.Warning(ex, "[Launcher] door view failed"); }
    }

    /// <summary>Opening a page is what "seen" means: the door's stamp leaves, the cards keep theirs.</summary>
    private static void MarkDoorSeen(LauncherDoor door)
    {
        try
        {
            var settings = App.Settings;
            var current = settings?.Current;
            if (current == null) return;
            var cards = LauncherCatalogue.All.Select(e => e.Card).ToList();
            if (LauncherDoorRules.NewCount(cards, door, current.LauncherSeenNewIds) == 0) return;
            current.LauncherSeenNewIds = LauncherDoorRules.MarkSeen(cards, door, current.LauncherSeenNewIds);
            settings!.Save();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] seen-set save failed"); }
    }

    private void DoorBack_Click(object sender, RoutedEventArgs e)
    {
        LauncherSfx.Click();
        SwapDoorView(null);
    }

    /// <summary>The host's seam (<see cref="LauncherHost.ShowDoor"/>) and a door card's click.</summary>
    private void OpenDoorPage(LauncherDoor door) => SwapDoorView(door);

    /// <summary>OUT: what is showing fades. Then the column is rebuilt for <paramref name="to"/>
    /// and comes IN: the tiles on their usual stagger, the doors on a fade.</summary>
    private void SwapDoorView(LauncherDoor? to)
    {
        if (_doorSwapping || _door == to) return;
        FrameworkElement leaving = _door == null ? DoorsHost : GamesScroller;
        void Arrive()
        {
            _doorSwapping = false;
            ClearFade(leaving);
            _door = to;
            BuildTiles();
            if (MotionFx.AllowTransitions)
            {
                foreach (var t in _tiles) t.Opacity = 0;
                if (to == null) FadeElement(DoorsHost, 0, 1, null);
            }
            MotionFx.StaggerIn(_tiles);
            ScheduleTileSettle("door");
        }

        if (!MotionFx.AllowTransitions || !IsVisible) { Arrive(); return; }
        _doorSwapping = true;
        FadeElement(leaving, leaving.Opacity, 0, Arrive);
    }

    private static void ClearFade(UIElement element)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = 1;
    }

    private static void FadeElement(UIElement element, double from, double to, Action? then)
    {
        bool done = false;
        void Finish()
        {
            if (done) return;
            done = true;
            try { then?.Invoke(); }
            catch (Exception ex) { Log.Warning(ex, "[Launcher] step after a door fade failed"); }
        }
        try
        {
            var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(GentleVisuals.FadeMs(DoorSwapMs)))
            {
                EasingFunction = new QuadraticEase { EasingMode = to > from ? EasingMode.EaseOut : EasingMode.EaseIn },
                FillBehavior = to > from ? FillBehavior.Stop : FillBehavior.HoldEnd,
            };
            if (to > from) element.Opacity = to;
            anim.Completed += (_, _) => Finish();
            element.BeginAnimation(UIElement.OpacityProperty, anim);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "[Launcher] door fade failed");
            element.Opacity = to > from ? to : 1;
            Finish();
        }
    }

    // ------------------------------------------------------------------ a door card

    private Border BuildDoorCard(LauncherDoor door)
    {
        var hue = DoorHue(door);
        var cards = LauncherCatalogue.All.Select(e => e.Card).ToList();
        var page = LauncherCatalogue.Page(door);
        var surface = ((SolidColorBrush)FindResource("SurfaceBgBrush")).Color;

        var card = new Border
        {
            CornerRadius = new CornerRadius(DoorRadius),
            Background = (Brush)FindResource("SurfaceBgBrush"),
            BorderBrush = RimBrush(hue),
            BorderThickness = new Thickness(1.5),
            Margin = new Thickness(9),
            Cursor = Cursors.Hand,
            Tag = door,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new TransformGroup { Children = { new ScaleTransform(1, 1), new TranslateTransform() } },
        };
        System.Windows.Automation.AutomationProperties.SetName(card, DoorTitle(door));

        var body = new Grid();
        body.SizeChanged += (_, _) =>
            body.Clip = new RectangleGeometry(new Rect(0, 0, body.ActualWidth, body.ActualHeight), DoorRadius, DoorRadius);

        // The collage: this door's own tile art, dimmed, fading into the card on the left so
        // the words sit on the surface. A card with no art leaves its slot to the others.
        var collage = new UniformGrid { Rows = 1, Opacity = 0.42, IsHitTestVisible = false };
        foreach (var entry in page)
        {
            if (collage.Children.Count >= 4) break;
            ImageSource? art = null;
            try
            {
                if (entry.Revealed && entry.ArtPath != null) art = ModResourceResolver.ResolveImageDecoded(entry.ArtPath, 360);
                else if (entry.Id == "breakout") art = BreakoutCardArt.Full;
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] door art {Id} failed", entry.Id); }
            if (art != null) collage.Children.Add(new Image { Source = art, Stretch = Stretch.UniformToFill });
        }
        body.Children.Add(collage);
        body.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5),
                GradientStops =
                {
                    new GradientStop(surface, 0),
                    new GradientStop(Color.FromArgb(0xE0, surface.R, surface.G, surface.B), 0.38),
                    new GradientStop(Color.FromArgb(0x50, surface.R, surface.G, surface.B), 1),
                },
            },
        });

        var text = new StackPanel { Margin = new Thickness(34, 0, 34, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = DoorTitle(door).ToUpperInvariant(),
            FontFamily = new FontFamily("/Fonts/#Fredoka, Segoe UI"), FontSize = 40, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Lighten(hue, 0.35)),
        });
        int fresh = LauncherDoorRules.NewCount(cards, door, App.Settings?.Current?.LauncherSeenNewIds);
        if (fresh > 0)
        {
            var stamp = BuildPill("exclusives_badge_new", null, (Brush)FindResource("AccentGradientBrush"), Brushes.White);
            ((TextBlock)((StackPanel)stamp.Child).Children[^1]).Text = Loc.GetF("launcher_door_new", fresh);
            stamp.Margin = new Thickness(14, 0, 0, 0);
            stamp.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(stamp);
        }
        text.Children.Add(titleRow);
        text.Children.Add(new TextBlock
        {
            Text = Loc.GetF(door == LauncherDoor.Tools ? "launcher_door_count_tools" : "launcher_door_count_games", page.Count),
            FontSize = 15, Margin = new Thickness(2, 6, 0, 0),
            Foreground = (Brush)FindResource("TextSecondaryBrush"),
        });

        var last = LauncherCatalogue.Find(LauncherDoorRules.ContinuePick(cards, App.Settings?.Current?.LauncherLastPlayedId));
        if (door == LauncherDoor.Games && last != null)
        {
            var go = new Button
            {
                Content = Loc.GetF("launcher_door_continue", last.Title),
                Style = (Style)FindResource("LauncherPlay"),
                Height = LauncherGridLayout.PlayHeight, Padding = new Thickness(0),
                MinWidth = 220, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 18, 0, 0),
                Background = new LinearGradientBrush(Lighten(hue, 0.15), Darken(hue, 0.75), 0),
                BorderBrush = new SolidColorBrush(Lighten(hue, 0.25)),
                Foreground = new SolidColorBrush(Lighten(hue, 0.45)),
            };
            go.PreviewMouseLeftButtonDown += Press_Down;
            go.PreviewMouseLeftButtonUp += Press_Up;
            go.Click += (_, _) =>
            {
                if (last.NeedsAccount) { LauncherSfx.Denied(); OpenSignIn(); return; }
                LauncherSfx.Click();
                LauncherHost.LaunchGame(last.Id);
            };
            text.Children.Add(go);
        }
        body.Children.Add(text);

        body.Children.Add(new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI"),
            FontSize = 22, Foreground = new SolidColorBrush(Lighten(hue, 0.45)), IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 26, 0),
        });
        card.Child = body;

        // The whole card is the door; Continue handles its own press, so it never opens the page.
        bool armed = false;
        card.MouseLeftButtonDown += (_, e) => { if (!e.Handled) armed = true; };
        card.MouseLeftButtonUp += (_, e) =>
        {
            bool was = armed;
            armed = false;
            if (!was || e.Handled) return;
            e.Handled = true;
            LauncherSfx.Click();
            OpenDoorPage(door);
        };
        card.MouseEnter += (_, _) => { MotionFx.HoverLift(card, true); LauncherSfx.Hover(); };
        card.MouseLeave += (_, _) => { armed = false; MotionFx.HoverLift(card, false); };
        return card;
    }
}
