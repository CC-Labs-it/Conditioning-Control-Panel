using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ConditioningControlPanel.Controls;
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
                System.Windows.Automation.AutomationProperties.SetName(DoorBack, Loc.Get("btn_back"));
                DoorBack.FontFamily = LauncherDoorCard.DisplayFont;
                DoorBack.FontSize = 15;
                GamesEyebrow.Text = DoorTitle(door);
                GamesEyebrow.FontFamily = LauncherDoorCard.DisplayFont;
                GamesEyebrow.FontSize = 22;
                GamesEyebrow.Foreground = new SolidColorBrush(Lighten(DoorHue(door), 0.55));
                DropDoorCards();
                MarkDoorSeen(door);
            }
            else
            {
                DropDoorCards();
                foreach (var which in new[] { LauncherDoor.Games, LauncherDoor.Tools })
                {
                    var view = BuildDoorCard(which);
                    _doorCards.Add(view);
                    DoorsHost.Children.Add(view.Root);
                }
                PlayDoorsIn(_doorCameFrom);
                _doorCameFrom = null;
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

    /// <summary>Set by Back for one rebuild: the door that was open comes down from forward.</summary>
    private LauncherDoor? _doorCameFrom;

    /// <summary>OUT: a door pushes forward while the other falls away (or the page fades). Then
    /// the column is rebuilt for <paramref name="to"/> and comes IN: the tiles on their usual
    /// stagger, the doors on their own settle (<see cref="PlayDoorsIn"/>).</summary>
    private void SwapDoorView(LauncherDoor? to)
    {
        if (_doorSwapping || _door == to) return;
        bool fromDoors = _door == null;
        FrameworkElement leaving = fromDoors ? DoorsHost : GamesScroller;
        var cameFrom = _door;
        void Arrive()
        {
            _doorSwapping = false;
            ClearFade(leaving);
            _door = to;
            _doorCameFrom = to == null ? cameFrom : null;
            BuildTiles();
            if (MotionFx.AllowTransitions)
                foreach (var t in _tiles) t.Opacity = 0;
            MotionFx.StaggerIn(_tiles);
            ScheduleTileSettle("door");
        }

        if (to != null && fromDoors) LauncherSfx.Edge();
        if (!MotionFx.AllowTransitions || !IsVisible) { Arrive(); return; }
        _doorSwapping = true;
        if (to is { } door && fromDoors && _doorCards.Count > 0) PlayDoorOpen(door, Arrive);
        else FadeElement(leaving, leaving.Opacity, 0, Arrive);
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

    private const int DoorArtMax = 6;

    /// <summary>The cards <see cref="LauncherDoorCard"/> painted for the doors view; empty on a page.</summary>
    private readonly List<LauncherDoorCard> _doorCards = new();
    private LauncherDoorCard? _doorHover;
    private CardSheenAdorner? _doorSheen;

    /// <summary>This door's own tile art, for the reel. A card with no art shows the surface.</summary>
    private static List<ImageSource> DoorArts(IReadOnlyList<LauncherEntry> page)
    {
        var arts = new List<ImageSource>();
        foreach (var entry in page)
        {
            if (arts.Count >= DoorArtMax) break;
            try
            {
                ImageSource? art = null;
                if (entry.Revealed && entry.ArtPath != null) art = ModResourceResolver.ResolveImageDecoded(entry.ArtPath, TileArtDecodeWidth);
                else if (entry.Id == "breakout") art = BreakoutCardArt.Full;
                if (art != null) arts.Add(art);
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] door art {Id} failed", entry.Id); }
        }
        return arts;
    }

    private LauncherDoorCard BuildDoorCard(LauncherDoor door)
    {
        var hue = DoorHue(door);
        var cards = LauncherCatalogue.All.Select(e => e.Card).ToList();
        var page = LauncherCatalogue.Page(door);

        Border? stamp = null;
        int fresh = LauncherDoorRules.NewCount(cards, door, App.Settings?.Current?.LauncherSeenNewIds);
        if (fresh > 0)
        {
            stamp = BuildPill("exclusives_badge_new", null, (Brush)FindResource("AccentGradientBrush"), Brushes.White);
            var stampText = (TextBlock)((StackPanel)stamp.Child).Children[^1];
            stampText.Text = Loc.GetF("launcher_door_new", fresh);
            stampText.FontFamily = LauncherDoorCard.DisplayFont;
            stampText.FontSize = 14;
            stamp.Padding = new Thickness(11, 4, 12, 5);
        }

        Button? go = null;
        var last = LauncherCatalogue.Find(LauncherDoorRules.ContinuePick(cards, App.Settings?.Current?.LauncherLastPlayedId));
        if (door == LauncherDoor.Games && last != null) go = BuildContinueButton(last, hue);

        var view = new LauncherDoorCard(new DoorCardSpec(
            DoorTitle(door),
            page.Count,
            Loc.Get(door == LauncherDoor.Tools ? "launcher_door_count_tools" : "launcher_door_count_games"),
            hue,
            DoorArts(page),
            ((SolidColorBrush)FindResource("SurfaceBgBrush")).Color,
            RimBrush(hue),
            (Brush)FindResource("TextSecondaryBrush"),
            ((SolidColorBrush)FindResource("TextLightBrush")).Color,
            stamp,
            go,
            GlowAllowed));
        var card = view.Card;
        card.Tag = door;
        // UI Automation's Invoke runs the same open as a click (the card names itself).
        view.Invoked = () => OpenDoorPage(door);

        // The whole card is the door; Continue handles its own press, so it never opens the page.
        bool armed = false;
        card.MouseLeftButtonDown += (_, e) => { if (!e.Handled) armed = true; };
        card.MouseLeftButtonUp += (_, e) =>
        {
            bool was = armed;
            armed = false;
            if (!was || e.Handled) return;
            e.Handled = true;
            OpenDoorPage(door);
        };
        card.KeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Enter or Key.Space) || e.OriginalSource is Button) return;
            e.Handled = true;
            OpenDoorPage(door);
        };
        card.MouseEnter += (_, _) => { DoorHover(view, true); LauncherSfx.Hover(); };
        card.MouseLeave += (_, _) => { armed = false; if (!card.IsKeyboardFocusWithin) DoorHover(view, false); };
        card.GotKeyboardFocus += (_, _) => DoorHover(view, true);
        card.LostKeyboardFocus += (_, _) => { if (!card.IsMouseOver) DoorHover(view, false); };
        return view;
    }

    /// <summary>Continue: a filled hue button (the depth law's raised face) that presses in.</summary>
    private Button BuildContinueButton(LauncherEntry last, Color hue)
    {
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new TextBlock
        {
            Text = "", FontFamily = new FontFamily("Segoe MDL2 Assets, Segoe UI Symbol, Segoe UI"),
            FontSize = 13, Margin = new Thickness(0, 1, 9, 0), VerticalAlignment = VerticalAlignment.Center,
        });
        var text = Loc.GetF("launcher_door_continue", last.Title);
        label.Children.Add(new TextBlock
        {
            Text = text,
            FontFamily = LauncherDoorCard.DisplayFont, FontSize = 16, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        });
        var go = new Button
        {
            Content = label,
            Style = (Style)FindResource("LauncherPlay"),
            Height = LauncherGridLayout.PlayHeight + 6, Padding = new Thickness(18, 0, 20, 0),
            MinWidth = 220, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 22, 0, 0),
            Background = new LinearGradientBrush(Lighten(hue, 0.15), Darken(hue, 0.7), 90),
            BorderBrush = new SolidColorBrush(Lighten(hue, 0.3)),
            Foreground = new SolidColorBrush(Lighten(hue, 0.6)),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        // The content is a glyph plus a trimmed label: name it with the whole line.
        System.Windows.Automation.AutomationProperties.SetName(go, text);
        go.PreviewMouseLeftButtonDown += Press_Down;
        go.PreviewMouseLeftButtonUp += Press_Up;
        go.MouseLeave += (_, _) => MotionFx.PressSquish(go, false);
        go.Click += (_, _) =>
        {
            if (last.NeedsAccount) { LauncherSfx.Denied(); OpenSignIn(); return; }
            // Access can drop while the doors are up (ContinuePick hides the button on the next
            // paint): no happy click before the host's refusal.
            if (last.Locked || !last.Available) LauncherSfx.Denied();
            else LauncherSfx.Click();
            LauncherHost.LaunchGame(last.Id);
        };
        return go;
    }

    /// <summary>IN: this door lifts and lights, the other steps back, the backdrop takes the hue,
    /// one sheen pass. OUT: all of it back.</summary>
    private void DoorHover(LauncherDoorCard view, bool on)
    {
        try
        {
            if (_doorSwapping) return;
            if (on && _doorHover != null && !ReferenceEquals(_doorHover, view)) DoorHover(_doorHover, false);
            view.SetHover(on);
            foreach (var other in _doorCards)
                if (!ReferenceEquals(other, view)) other.StepBack(on);
            DetachSheen(ref _doorSheen);
            if (on)
            {
                _doorHover = view;
                _trailTint = view.Hue;
                BleedGlow(view.Hue);
                if (!_perfLow && !_fxParked && MotionFx.AllowAmbientLoops)
                    _doorSheen = AttachSheen(view.Card, LauncherDoorCard.Radius);
            }
            else
            {
                if (ReferenceEquals(_doorHover, view)) _doorHover = null;
                _trailTint = null;
                RestoreGlow(true);
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] door hover failed"); }
    }

    /// <summary>The doors arrive: staggered, settling, the count ticking up, then the reels run.</summary>
    private void PlayDoorsIn(LauncherDoor? cameFrom)
    {
        try
        {
            for (int i = 0; i < _doorCards.Count; i++)
            {
                var view = _doorCards[i];
                bool wasOpen = cameFrom is { } d && Equals(view.Card.Tag, d);
                view.Enter(i, wasOpen);
            }
            Dispatcher.BeginInvoke(DispatcherPriority.Background, StartDoorReels);
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] doors entrance failed"); }
    }

    private void StartDoorReels()
    {
        if (_door != null || _fxParked || _perfLow || !IsVisible) return;
        foreach (var view in _doorCards) view.StartReel();
    }

    private void StopDoorReels()
    {
        foreach (var view in _doorCards) view.StopReel();
    }

    /// <summary>The doors leave the tree: clocks off, hover and sheen dropped, nothing held.</summary>
    private void DropDoorCards()
    {
        StopDoorReels();
        DetachSheen(ref _doorSheen);
        if (_doorHover != null) { _doorHover = null; _trailTint = null; RestoreGlow(false); }
        _doorCards.Clear();
        DoorsHost.Children.Clear();
    }

    /// <summary>The open beat: the chosen door pushes forward, the other falls away, a ring and a
    /// burst in the door's hue, a shimmer. <paramref name="then"/> runs once the door has let go.</summary>
    private void PlayDoorOpen(LauncherDoor door, Action then)
    {
        StopDoorReels();
        DetachSheen(ref _doorSheen);
        LauncherDoorCard? chosen = null;
        foreach (var view in _doorCards)
            if (Equals(view.Card.Tag, door)) { chosen = view; view.PushForward(); }
        int chosenAt = chosen == null ? 0 : _doorCards.IndexOf(chosen);
        for (int i = 0; i < _doorCards.Count; i++)
            if (!ReferenceEquals(_doorCards[i], chosen)) _doorCards[i].FallAway(i > chosenAt);
        if (chosen != null)
        {
            if (MotionFx.AllowAmbientLoops && !GentleVisuals.On) Shockwave(chosen.Card, chosen.Hue);
            BurstAt(chosen.Card, chosen.Hue, 140);
        }
        Later(LauncherDoorCard.OpenMs - 30, then);
    }
}
