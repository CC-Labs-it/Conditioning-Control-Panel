// PORTED from WPF feat/declutter-doors (LauncherWindow.Doors.cs + LauncherDoorCard.cs, 2026-11-15).
// The right column opens on two door cards and no tiles: GAMES and TOOLS. A door opens its page,
// which is the old scroller and grid filled with that door's listed cards under a Back link. What a
// door counts, stamps and continues is decided in Core's LauncherDoorRules; this file paints and asks.
// Rails: every pose is a tween on the launcher's FX clock (FrameClock, 30 fps), never Animation.RunAsync
// on a transform; no Effect on a door card or under the reel; transitions off = every pose at rest;
// Gentle visuals lengthen fades and drop the shockwave; the reel runs only with ambient loops on.
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.JustDrop;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Safety;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class LauncherWindow
{
    internal const double DoorRadius = 20;
    private const int DoorArtMax = 6;
    private const double DoorSwapSeconds = 0.13, DoorOpenSeconds = 0.23, DoorHoverSeconds = 0.18;
    private const double DoorEnterSeconds = 0.52, DoorEnterStagger = 0.09, DoorEnterFromX = 46, DoorEnterFromScale = 0.94;
    private const double DoorReelHoldSeconds = 4.6, DoorReelFadeSeconds = 1.1, DoorReelZoom = 1.05;
    // WPF LauncherDoorCard.cs:50 (ReelDriftSeconds), :361-363 (hover zoom, 420 ms), :413 (odometer, 0.6 s).
    private const double DoorDriftSeconds = 7.5 + DoorReelHoldSeconds, DoorDriftPan = 5, DoorLensSeconds = 0.42, DoorTallySeconds = 0.6;
    private static readonly FontFamily DoorFont = new("Fredoka, Segoe UI");

    /// <summary>Null while the doors are showing; otherwise the door whose page is up.</summary>
    private LauncherDoor? _door;
    private bool _doorsEnter, _doorSwapping, _doorKeyed;
    private LauncherDoor? _doorCameFrom;
    private readonly List<DoorView> _doorViews = new();
    private DoorView? _doorHover;
    private Tween _doorFade;
    private double _doorThenIn = -1;
    private Action? _doorThen;
    private DispatcherTimer? _doorReelTimer;
    private ConditioningControlPanel.Avalonia.Controls.CardSheenAdorner? _doorSheen;

    /// <summary>The door a coming show opens on (<see cref="ShowDoor"/>), read once by the window.</summary>
    private static LauncherDoor? _pendingDoor;

    /// <summary>The page that is up, or null on the doors view.</summary>
    internal LauncherDoor? OpenDoor => _door;

    /// <summary>The door cards on screen (empty on a page), GAMES first.</summary>
    internal IReadOnlyList<Button> DoorButtons => _doorViews.Select(v => v.Button).ToList();

    /// <summary>The Continue button on the Games door, or null when there is none.</summary>
    internal Button? ContinueButton => _doorViews.FirstOrDefault(v => v.Door == LauncherDoor.Games)?.Continue;

    /// <summary>The "N new" stamp on the Games door, or null when nothing new is unseen.</summary>
    internal Border? NewStamp => _doorViews.FirstOrDefault(v => v.Door == LauncherDoor.Games)?.Stamp;

    // ------------------------------------------------------------------ rules in, cards out

    /// <summary>WPF LauncherEntry.Available: this head can open it (Just Drop also asks its door).</summary>
    internal static bool Available(LauncherCard card)
    {
        if (!Destinations.ContainsKey(card.Id)) return false;
        if (!string.Equals(card.Id, Games.GameWindow.JustDropId, StringComparison.OrdinalIgnoreCase)) return true;
        try { return JustDropService.DoorAvailable; }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] just drop door probe failed"); return false; }
    }

    /// <summary>Every card as the door rules see it today.</summary>
    private static List<LauncherDoorCard> RuleCards() =>
        LauncherCards.Every.Select(c =>
        {
            bool open = Available(c);
            return c.DoorCard(open, open && Locked(Destinations[c.Id]));
        }).ToList();

    private static IReadOnlyList<LauncherCard> PageFor(LauncherDoor door) =>
        LauncherDoorRules.Page(RuleCards(), door).Select(id => LauncherCards.FindAny(id)!).ToList();

    /// <summary>The cards <see cref="BuildTiles"/> lays out: the open door's page, or none.</summary>
    private IReadOnlyList<LauncherCard> PageCards() => _door is { } door ? PageFor(door) : Array.Empty<LauncherCard>();

    private static string DoorTitle(LauncherDoor door) =>
        Loc.Get(door == LauncherDoor.Tools ? "launcher_door_tools" : "launcher_section_games");

    private static Color DoorHue(LauncherDoor door) =>
        door == LauncherDoor.Tools ? Color.FromRgb(0x55, 0xB7, 0xFF) : Color.FromRgb(0xFF, 0x69, 0xB4);

    /// <summary>WPF LauncherHost.NoteLaunched: a game that really started is what Continue offers next.</summary>
    private static void NoteLaunched(LauncherCard card)
    {
        try
        {
            if (!LauncherDoorRules.RemembersLaunch(card.Door)) return;
            var id = LauncherDoorRules.ListedId(card.Id);
            var settings = CoreSettings.Current;
            if (string.Equals(settings.LauncherLastPlayedId, id, StringComparison.OrdinalIgnoreCase)) return;
            settings.LauncherLastPlayedId = id;
            CoreSettings.Save();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] last-played note failed"); }
    }

    /// <summary>Opening a page is what "seen" means: the door's stamp leaves, the cards keep theirs.</summary>
    private static void MarkDoorSeen(LauncherDoor door)
    {
        try
        {
            var settings = CoreSettings.Current;
            var cards = RuleCards();
            if (LauncherDoorRules.NewCount(cards, door, settings.LauncherSeenNewIds) == 0) return;
            settings.LauncherSeenNewIds = LauncherDoorRules.MarkSeen(cards, door, settings.LauncherSeenNewIds);
            CoreSettings.Save();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] seen-set save failed"); }
    }

    // ------------------------------------------------------------------ the panel's way in

    /// <summary>
    /// WPF LauncherHost.ShowDoor: the launcher comes up from <paramref name="panel"/> (the panel tucks
    /// away exactly as its back-to-launcher button does) already opened on <paramref name="door"/>'s
    /// page. False when Lockdown refuses the trip; the panel then stays where it is.
    /// </summary>
    internal static bool ShowDoor(MainShellWindow panel, LauncherDoor door)
    {
        if (MainShellWindow.LockdownActive)
        {
            try { Services.LockdownService.Current?.NotifyEscapeAttempt(Services.Possession.EscapeKinds.Close); } catch { }
            return false;
        }
        if (_window is { IsVisible: true } shown)
        {
            Open();
            shown.OpenDoorPage(door);
            return true;
        }
        _pendingDoor = door;
        if (BackToLauncher(panel)) return true;
        _pendingDoor = null;
        return false;
    }

    private static LauncherDoor? PeekPendingDoor() => _pendingDoor;

    private static LauncherDoor? TakePendingDoor()
    {
        var door = _pendingDoor;
        _pendingDoor = null;
        return door;
    }

    // ------------------------------------------------------------------ the column

    /// <summary>Puts the column in the state <see cref="_door"/> names. Called by every
    /// <see cref="BuildTiles"/>; only a rebuild flagged by <see cref="_doorsEnter"/> (the launcher
    /// shown, or Back) plays the doors' entrance, a data refresh repaints them at rest.</summary>
    private void ApplyDoorView()
    {
        try
        {
            bool page = _door != null;
            DoorPageHeader.IsVisible = page;
            GamesScroller.IsVisible = page;
            DoorsHost.IsVisible = !page;
            if (_door is { } door)
            {
                DoorBackText.Text = "‹ " + Loc.Get("btn_back");
                AutomationProperties.SetName(DoorBack, Loc.Get("btn_back"));
                GamesEyebrow.Text = DoorTitle(door);
                GamesEyebrow.Foreground = new SolidColorBrush(Lighten(DoorHue(door), 0.55));
                DropDoorCards();
                MarkDoorSeen(door);
                _doorsEnter = false;
                return;
            }
            bool enter = _doorsEnter;
            _doorsEnter = false;
            // A refresh under a focused door keeps the focus on that door.
            LauncherDoor? focused = enter ? null : _doorViews.FirstOrDefault(v => v.Button.IsKeyboardFocusWithin)?.Door;
            DropDoorCards();
            foreach (var which in new[] { LauncherDoor.Games, LauncherDoor.Tools })
            {
                var view = BuildDoorCard(which);
                _doorViews.Add(view);
                DoorsHost.Children.Add(view.Root);
            }
            if (enter && Env.AllowTransitions)
            {
                for (int i = 0; i < _doorViews.Count; i++) DoorEnter(_doorViews[i], i, Equals(_doorCameFrom, _doorViews[i].Door));
                EnsureFxClock();
            }
            else foreach (var view in _doorViews) DoorRest(view);
            _doorCameFrom = null;
            StartDoorReels();
            if (focused is { } f) FocusDoorLater(f);
        }
        catch (Exception ex) { Log.Warning(ex, "[Launcher] door view failed"); }
    }

    private void DoorBack_Click(object? sender, RoutedEventArgs e) => SwapDoorView(null);

    /// <summary><see cref="ShowDoor"/>'s seam and a door card's click.</summary>
    internal void OpenDoorPage(LauncherDoor door) => SwapDoorView(door);

    /// <summary>Back to the two doors (the page's Back link).</summary>
    internal void CloseDoorPage() => SwapDoorView(null);

    /// <summary>Puts the column on <paramref name="door"/>'s page (null = the doors) at once, with no
    /// beat: a pending swap lands first. Tests use it to reach a page's tiles without a clock.</summary>
    internal void SeatDoorPage(LauncherDoor? door)
    {
        if (_doorThen != null) RunDoorThen();
        _doorSwapping = false;
        GamesScroller.Opacity = 1;
        _door = door;
        BuildTiles();
    }

    /// <summary>OUT: a door pushes forward while the other falls away (or the page fades). Then the
    /// column is rebuilt for <paramref name="to"/> and comes IN: the tiles on their usual stagger,
    /// the doors on their own settle.</summary>
    private void SwapDoorView(LauncherDoor? to)
    {
        if (_doorSwapping || _door == to || MainShellWindow.LockdownActive) return;
        bool fromDoors = _door == null;
        var cameFrom = _door;
        // Focus follows the swap when the player drives from the keyboard or was focused in this column.
        bool carryFocus = _doorKeyed || GamesColumn.IsKeyboardFocusWithin;
        _doorKeyed = false;
        void Arrive()
        {
            _doorSwapping = false;
            GamesScroller.Opacity = 1;
            _door = to;
            _doorCameFrom = to == null ? cameFrom : null;
            if (to == null) _doorsEnter = true;
            BuildTiles();
            StartEntrance();
            if (!carryFocus) return;
            if (to != null) FocusLater(DoorBack);
            else if (cameFrom is { } was) FocusDoorLater(was);
        }

        if (to != null && fromDoors) LauncherSfx.Edge();
        if (!Env.AllowTransitions || !FxLive) { Arrive(); return; }
        _doorSwapping = true;
        _doorThen = Arrive;
        if (to is { } door && fromDoors && _doorViews.Count > 0) PlayDoorOpen(door);
        else _doorFade.Go(GamesScroller.Opacity, 0, GentleVisuals.FadeMs((int)(DoorSwapSeconds * 1000)) / 1000.0, Ease.QuadIn);
        EnsureFxClock();
    }

    private void FocusDoorLater(LauncherDoor door)
    {
        var view = _doorViews.FirstOrDefault(v => v.Door == door);
        if (view != null) FocusLater(view.Button);
    }

    /// <summary>After the layout pass, so the freshly built element can take focus.</summary>
    private void FocusLater(Control target) => Dispatcher.UIThread.Post(() =>
    {
        try { if (IsVisible) target.Focus(NavigationMethod.Tab); }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] door focus failed"); }
    }, DispatcherPriority.Input);

    // ------------------------------------------------------------------ a door card

    /// <summary>One door card and its poses. Every value is a tween stepped on the FX clock.</summary>
    private sealed class DoorView
    {
        public LauncherDoor Door;
        public Color Hue;
        public Panel Root = null!;
        public Button Button = null!;
        public Button? Continue;
        public Border Tint = null!, Mask = null!;
        public Border? Stamp;
        public readonly ScaleTransform RootScale = new(1, 1), BarScale = new(1, 1), StampScale = new(1, 1);
        public readonly TranslateTransform RootSlide = new(), ChevronSlide = new();
        public readonly Image[] Layers = new Image[2];
        public readonly ScaleTransform[] Zoom = { new(1, 1), new(1, 1) };
        public readonly TranslateTransform[] Pan = { new(), new() };
        public readonly ScaleTransform Lens = new(1, 1);
        public readonly Tween[] Drift = new Tween[2];
        public readonly bool[] DriftLeft = new bool[2];
        public bool NextLeft;
        public SolidColorBrush Title = null!;
        public Color TitleRest;
        public TextBlock Count = null!;
        public string CountFormat = "";
        public int CountTo;
        public readonly List<IImage> Arts = new();
        public int ArtAt, Front;
        public Tween Scale, X, Y, Fade, Hover, Back, Pop, ReelIn, Zoomed, Ink, Tally;
        public double Wait, StampWait = -1;
        public bool Busy => Wait > 0 || StampWait >= 0 || Scale.On || X.On || Y.On || Fade.On || Hover.On || Back.On || Pop.On || ReelIn.On
            || Zoomed.On || Ink.On || Tally.On || Drift[0].On || Drift[1].On;
    }

    /// <summary>The title shrinks to fit the room the stamp leaves (never grows): the stamp is
    /// measured first and sits right after the title (WPF DoorTitleRow).</summary>
    private sealed class DoorTitleRow : Panel
    {
        protected override Size MeasureOverride(Size available)
        {
            var stamp = Children.Count > 1 ? Children[1] : null;
            stamp?.Measure(new Size(double.PositiveInfinity, available.Height));
            double sw = stamp?.DesiredSize.Width ?? 0;
            Children[0].Measure(new Size(Math.Max(0, available.Width - sw), available.Height));
            var t = Children[0].DesiredSize;
            return new Size(t.Width + sw, Math.Max(t.Height, stamp?.DesiredSize.Height ?? 0));
        }

        protected override Size ArrangeOverride(Size final)
        {
            var t = Children[0].DesiredSize;
            Children[0].Arrange(new Rect(0, (final.Height - t.Height) / 2, t.Width, t.Height));
            if (Children.Count > 1)
            {
                var s = Children[1].DesiredSize;
                Children[1].Arrange(new Rect(t.Width, (final.Height - s.Height) / 2, s.Width, s.Height));
            }
            return final;
        }
    }

    /// <summary>This door's own tile art, for the reel. A card with no art adds nothing.</summary>
    private static List<IImage> DoorArts(IReadOnlyList<LauncherCard> page)
    {
        var arts = new List<IImage>();
        foreach (var card in page)
        {
            if (arts.Count >= DoorArtMax) break;
            try
            {
                IImage? art = card.ArtPath != null && Revealed(card.Id) ? ModArt.TryLoad(card.ArtPath, 640) : null;
                if (art == null && card.Id is "breakout" or "breakoutdemo") art = BreakoutCardArt.Demo;
                if (art != null) arts.Add(art);
            }
            catch (Exception ex) { Log.Debug(ex, "[Launcher] door art {Id} failed", card.Id); }
        }
        return arts;
    }

    private DoorView BuildDoorCard(LauncherDoor door)
    {
        var hue = DoorHue(door);
        var page = PageFor(door);
        var view = new DoorView { Door = door, Hue = hue };
        view.Arts.AddRange(DoorArts(page));
        var surface = ((ISolidColorBrush)Res("SurfaceBgBrush")).Color;
        string title = DoorTitle(door);
        view.CountFormat = Loc.Get(door == LauncherDoor.Tools ? "launcher_door_count_tools" : "launcher_door_count_games");
        view.CountTo = page.Count;
        view.TitleRest = ((ISolidColorBrush)Res("TextLightBrush")).Color;
        view.Title = new SolidColorBrush(view.TitleRest);
        string count = SafeFormat(view.CountFormat, page.Count);

        view.Root = new Panel
        {
            Margin = new Thickness(9),
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform = new TransformGroup { Children = { view.RootScale, view.RootSlide } },
        };
        var body = new Grid();
        body.SizeChanged += (_, _) => body.Clip = new RectangleGeometry(new Rect(body.Bounds.Size), DoorRadius, DoorRadius);
        var face = new Border
        {
            CornerRadius = new CornerRadius(DoorRadius),
            Background = new SolidColorBrush(surface),
            BorderBrush = RimBrush(hue),
            BorderThickness = new Thickness(1.5),
            Child = body,
        };
        view.Button = new Button
        {
            Theme = (ControlTheme)this.FindResource("LauncherDoorButton")!,
            Content = face,
            Tag = door,
        };
        // UI Automation reads the door as a button: its title and what it holds; Invoke = a click.
        AutomationProperties.SetName(view.Button, title + ", " + count);
        AutomationProperties.SetAutomationId(view.Button, "LauncherDoor_" + LauncherDoorRules.Key(door));
        view.Root.Children.Add(view.Button);

        // --- the reel: the picture shown WHOLE, as tall as the card, hugging its right edge; the
        // left side of every picture melts away so no picture shows a hard side.
        // WPF LauncherDoorCard.cs:143-148: the hover zoom scales the whole reel about (0.7, 0.5).
        var reel = new Panel
        {
            IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Right,
            RenderTransformOrigin = new RelativePoint(0.7, 0.5, RelativeUnit.Relative), RenderTransform = view.Lens,
        };
        body.SizeChanged += (_, _) =>
        {
            if (body.Bounds.Height > 0) reel.Width = Math.Min(body.Bounds.Width, body.Bounds.Height * 16.0 / 9.0);
        };
        var melt = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0), new GradientStop(Colors.Black, 0.38), new GradientStop(Colors.Black, 1) },
        };
        for (int i = 0; i < 2; i++)
        {
            view.Layers[i] = new Image
            {
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Right,
                OpacityMask = melt,
                Opacity = 0,
                // The zoom grows away from the card's right edge.
                RenderTransformOrigin = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                RenderTransform = new TransformGroup { Children = { view.Zoom[i], view.Pan[i] } },
            };
            reel.Children.Add(view.Layers[i]);
        }
        if (view.Arts.Count > 0) { view.Layers[0].Source = view.Arts[0]; view.Layers[0].Opacity = 1; }
        body.Children.Add(reel);

        // The door's hue washed over the picture, brighter on hover.
        view.Tint = new Border
        {
            IsHitTestVisible = false,
            Opacity = 0.45,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0x66, hue.R, hue.G, hue.B), 0),
                    new GradientStop(Color.FromArgb(0x10, hue.R, hue.G, hue.B), 0.55),
                    new GradientStop(Color.FromArgb(0x00, hue.R, hue.G, hue.B), 1),
                },
            },
        };
        body.Children.Add(view.Tint);
        // The mask: the card's surface on the left, where the words sit, opening to the picture.
        view.Mask = new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0xF4, surface.R, surface.G, surface.B), 0),
                    new GradientStop(Color.FromArgb(0xCC, surface.R, surface.G, surface.B), 0.30),
                    new GradientStop(Color.FromArgb(0x55, surface.R, surface.G, surface.B), 0.58),
                    new GradientStop(Color.FromArgb(0x10, surface.R, surface.G, surface.B), 1),
                },
            },
        };
        body.Children.Add(view.Mask);
        body.Children.Add(new Border
        {
            IsHitTestVisible = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.55), new GradientStop(Color.FromArgb(0x70, 0, 0, 0), 1) },
            },
        });

        // --- the words
        var text = new StackPanel { Margin = new Thickness(36, 0, 90, 0), VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new DoorTitleRow();
        titleRow.Children.Add(new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock
            {
                Text = title, FontFamily = DoorFont, FontSize = 60, FontWeight = FontWeight.SemiBold,
                Foreground = view.Title,
            },
        });
        int fresh = LauncherDoorRules.NewCount(RuleCards(), door, CoreSettings.Current.LauncherSeenNewIds);
        if (fresh > 0)
        {
            view.Stamp = new Border
            {
                Background = Res("AccentGradientBrush"), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(11, 4, 12, 5), Margin = new Thickness(16, 6, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = RelativePoint.Center,
                RenderTransform = new TransformGroup { Children = { view.StampScale, new RotateTransform(-6) } },
                Child = new TextBlock
                {
                    Text = SafeFormat(Loc.Get("launcher_door_new"), fresh), FontFamily = DoorFont, FontSize = 14,
                    FontWeight = FontWeight.Bold, Foreground = Brushes.White,
                },
            };
            titleRow.Children.Add(view.Stamp);
        }
        text.Children.Add(titleRow);
        // The hue bar under the title: it stretches on hover.
        text.Children.Add(new Border
        {
            Width = 64, Height = 5, CornerRadius = new CornerRadius(2.5),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 4, 0, 0),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Lighten(hue, 0.25), 0), new GradientStop(Color.FromArgb(0x30, hue.R, hue.G, hue.B), 1) },
            },
            RenderTransformOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            RenderTransform = view.BarScale,
            IsHitTestVisible = false,
        });
        view.Count = new TextBlock { Text = count, FontSize = 16, Margin = new Thickness(3, 12, 0, 0), Foreground = Res("TextSecondaryBrush") };
        text.Children.Add(view.Count);
        if (door == LauncherDoor.Games
            && LauncherCards.FindAny(LauncherDoorRules.ContinuePick(RuleCards(), CoreSettings.Current.LauncherLastPlayedId)) is { } last)
        {
            view.Continue = BuildContinueButton(last, hue);
            text.Children.Add(view.Continue);
        }
        body.Children.Add(text);

        // --- the chevron, on a round plate in the door's hue
        body.Children.Add(new Border
        {
            Width = 50, Height = 50, CornerRadius = new CornerRadius(25),
            Background = new SolidColorBrush(Color.FromArgb(0x38, hue.R, hue.G, hue.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, hue.R, hue.G, hue.B)),
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 28, 0),
            IsHitTestVisible = false,
            RenderTransform = view.ChevronSlide,
            Child = new TextBlock
            {
                Text = "›", FontFamily = DoorFont, FontSize = 30, Margin = new Thickness(2, -4, 0, 0),
                Foreground = new SolidColorBrush(Lighten(hue, 0.55)),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        });

        // The whole card is the door; Continue handles (and marks) its own click.
        view.Button.Click += (_, e) =>
        {
            if (e.Source != view.Button) return;
            e.Handled = true;
            if (!MainShellWindow.LockdownActive) OpenDoorPage(door);
        };
        view.Button.AddHandler(KeyDownEvent, (_, e) => { if (e.Key is Key.Enter or Key.Space) _doorKeyed = true; }, RoutingStrategies.Tunnel);
        view.Button.PointerEntered += (_, _) => { DoorHover(view, true); LauncherSfx.Hover(); };
        view.Button.PointerExited += (_, _) => { if (!view.Button.IsKeyboardFocusWithin) DoorHover(view, false); };
        view.Button.GotFocus += (_, _) => DoorHover(view, true);
        view.Button.LostFocus += (_, _) => { if (!view.Button.IsPointerOver) DoorHover(view, false); };
        return view;
    }

    private static string SafeFormat(string format, int n)
    {
        try { return string.Format(format, n); }
        catch { return n.ToString(); }
    }

    /// <summary>Continue: a filled hue button (the depth law's raised face) that presses in.</summary>
    private Button BuildContinueButton(LauncherCard last, Color hue)
    {
        var text = Loc.GetF("launcher_door_continue", Loc.Get(last.TitleKey));
        var label = new StackPanel { Orientation = Orientation.Horizontal };
        label.Children.Add(new TextBlock { Text = "▶", FontSize = 11, Margin = new Thickness(0, 0, 9, 0), VerticalAlignment = VerticalAlignment.Center });
        label.Children.Add(new TextBlock
        {
            Text = text, FontFamily = DoorFont, FontSize = 16, FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        });
        var go = new Button
        {
            Content = label,
            Theme = (ControlTheme)this.FindResource("LauncherPlay")!,
            Height = LauncherGridLayout.PlayHeight + 6, Padding = new Thickness(18, 0, 20, 0),
            MinWidth = 220, MaxWidth = 380, HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 22, 0, 0),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Lighten(hue, 0.15), 0), new GradientStop(Darken(hue, 0.7), 1) },
            },
            BorderBrush = new SolidColorBrush(Lighten(hue, 0.3)),
            Foreground = new SolidColorBrush(Lighten(hue, 0.6)),
            Tag = "door-continue",
        };
        // The content is a glyph plus a trimmed label: name it with the whole line.
        AutomationProperties.SetName(go, text);
        HookPress(go);
        go.Click += (_, e) =>
        {
            e.Handled = true;
            // Play owns the cue (Click, or Denied with the shake) and every refusal: sign-in, lock, leash.
            if (!MainShellWindow.LockdownActive) Play(last);
        };
        return go;
    }

    // ------------------------------------------------------------------ poses

    private static bool DoorLively => Env.AllowAmbientLoops && !PerfLow;

    /// <summary>IN: this door lifts and lights, the other steps back, the backdrop takes the hue.
    /// OUT: all of it back.</summary>
    private void DoorHover(DoorView view, bool on)
    {
        try
        {
            if (_doorSwapping) return;
            if (on && _doorHover != null && !ReferenceEquals(_doorHover, view)) DoorHover(_doorHover, false);
            double secs = Env.AllowTransitions ? GentleVisuals.FadeMs((int)(DoorHoverSeconds * 1000)) / 1000.0 : 0;
            view.Hover.Go(view.Hover.Step(0), on ? 1 : 0, secs);
            // WPF LauncherDoorCard.SetHover (:361-371): the reel zooms (1.06, 1.03 without ambient loops) and the
            // title takes the hue; transitions off snaps both.
            bool anim = Env.AllowTransitions;
            view.Zoomed.Go(view.Zoomed.Step(0), on ? (DoorLively ? 1.06 : 1.03) : 1, anim ? DoorLensSeconds : 0);
            view.Ink.Go(view.Ink.Step(0), on ? 1 : 0, anim ? GentleVisuals.FadeMs(220) / 1000.0 : 0, Ease.Linear);
            // WPF LauncherWindow.Doors.cs:377-383: a sheen pass over the hovered door.
            ConditioningControlPanel.Avalonia.Controls.CardSheenAdorner.Detach(_doorSheen); _doorSheen = null;
            if (on && !PerfLow && FxLive && Env.AllowAmbientLoops) _doorSheen = ConditioningControlPanel.Avalonia.Controls.CardSheenAdorner.Attach(view.Button, DoorRadius);
            foreach (var other in _doorViews)
                if (!ReferenceEquals(other, view)) other.Back.Go(other.Back.Step(0), on ? 1 : 0, secs);
            if (on) { _doorHover = view; BleedGlow(view.Hue); }
            else
            {
                if (ReferenceEquals(_doorHover, view)) _doorHover = null;
                RestoreGlow();
            }
            if (secs <= 0) foreach (var v in _doorViews) PaintDoor(v);
            EnsureFxClock();
        }
        catch (Exception ex) { Log.Debug(ex, "[Launcher] door hover failed"); }
    }

    /// <summary>Test seam: hover (or leave) the door card <paramref name="door"/>.</summary>
    internal void DoorHover(Button door, bool on)
    {
        foreach (var v in _doorViews) if (ReferenceEquals(v.Button, door)) DoorHover(v, on);
    }

    /// <summary>Test seam: a door's title colour, reel zoom and front picture's drift scale.</summary>
    internal (Color Title, double Zoom, double Drift) DoorPose(int index)
    {
        var v = _doorViews[index];
        return (v.Title.Color, v.Lens.ScaleX, v.Zoom[v.Front].ScaleX);
    }

    /// <summary>The doors arrive: staggered, settling; the stamp pops once its door has landed.
    /// The door that was open comes back down from forward.</summary>
    private void DoorEnter(DoorView v, int index, bool fromOpen)
    {
        double secs = GentleVisuals.RiseMs((int)(DoorEnterSeconds * 1000)) / 1000.0;
        v.Wait = DoorEnterStagger * index;
        v.Scale.Go(fromOpen ? 1.08 : DoorEnterFromScale, 1, secs);
        v.X.Go(fromOpen || !DoorLively ? 0 : DoorEnterFromX, 0, secs);
        v.Y.Go(0, 0, 0);
        v.Fade.Go(0, 1, secs);
        v.Hover.Go(0, 0, 0);
        v.Back.Go(0, 0, 0);
        if (v.Stamp != null) { v.StampWait = v.Wait + secs * 0.7; v.StampScale.ScaleX = v.StampScale.ScaleY = 0; }
        v.Zoomed.Go(1, 1, 0); v.Ink.Go(0, 0, 0);
        // WPF LauncherDoorCard.cs:413: the count ticks up from 0 (MotionFx.Odometer, 0.6 s, quad out).
        v.Tally.Go(0, v.CountTo, v.CountTo >= 1 ? DoorTallySeconds : 0);
        PaintDoor(v);
    }

    private static void DoorRest(DoorView v)
    {
        v.Wait = 0;
        v.StampWait = -1;
        v.Scale.Go(1, 1, 0); v.X.Go(0, 0, 0); v.Y.Go(0, 0, 0); v.Fade.Go(1, 1, 0);
        v.Hover.Go(0, 0, 0); v.Back.Go(0, 0, 0); v.Pop.Go(1, 1, 0);
        v.Zoomed.Go(1, 1, 0); v.Ink.Go(0, 0, 0); v.Tally.Go(v.CountTo, v.CountTo, 0);
        v.StampScale.ScaleX = v.StampScale.ScaleY = 1;
        PaintDoor(v);
    }

    /// <summary>The open beat: the chosen door pushes forward, the other falls away, a ring and a
    /// burst in the door's hue. The swap's Arrive runs once the door has let go.</summary>
    private void PlayDoorOpen(LauncherDoor door)
    {
        StopDoorReels();
        var chosen = _doorViews.FirstOrDefault(v => v.Door == door);
        int at = chosen == null ? 0 : _doorViews.IndexOf(chosen);
        double secs = DoorOpenSeconds;
        double fade = GentleVisuals.FadeMs((int)(secs * 1000)) / 1000.0;
        for (int i = 0; i < _doorViews.Count; i++)
        {
            var v = _doorViews[i];
            v.Wait = 0;
            if (ReferenceEquals(v, chosen))
            {
                v.Scale.Go(v.Scale.Step(0), DoorLively ? 1.08 : 1.02, secs);
                v.Fade.Go(1, 0, fade, Ease.QuadIn);
            }
            else
            {
                v.Scale.Go(v.Scale.Step(0), 0.92, secs - 0.04);
                if (DoorLively) v.Y.Go(0, i > at ? 46 : -46, secs - 0.04);
                v.Fade.Go(1, 0, Math.Max(0.05, fade - 0.06));
            }
            v.Hover.Go(v.Hover.Step(0), 0, 0.1);
            v.Back.Go(v.Back.Step(0), 0, 0.1);
        }
        if (chosen != null)
        {
            if (DoorLively && !GentleVisuals.On) Shockwave(chosen.Button, chosen.Hue);
            BurstAt(chosen.Button, chosen.Hue, 140);
        }
        _doorThenIn = Math.Max(0.02, Math.Max(secs, fade) - 0.03);
    }

    private static void PaintDoor(DoorView v)
    {
        double h = v.Hover.Step(0), b = v.Back.Step(0);
        double s = v.Scale.Step(0) * (1 + 0.015 * h) * (1 - 0.01 * b);
        v.RootScale.ScaleX = v.RootScale.ScaleY = s;
        v.RootSlide.X = v.X.Step(0);
        v.RootSlide.Y = v.Y.Step(0);
        v.Root.Opacity = v.Fade.Step(0) * (1 - 0.2 * b);
        v.BarScale.ScaleX = 1 + 0.6 * h;
        v.ChevronSlide.X = 6 * h;
        v.Tint.Opacity = 0.45 + 0.55 * h;
        v.Mask.Opacity = 1 - 0.28 * h;
        v.Lens.ScaleX = v.Lens.ScaleY = v.Zoomed.Step(0);
        double u = v.Ink.Step(0);
        var to = Lighten(v.Hue, 0.55);
        v.Title.Color = Color.FromRgb((byte)(v.TitleRest.R + (to.R - v.TitleRest.R) * u),
            (byte)(v.TitleRest.G + (to.G - v.TitleRest.G) * u), (byte)(v.TitleRest.B + (to.B - v.TitleRest.B) * u));
        string count = SafeFormat(v.CountFormat, (int)Math.Round(v.Tally.Step(0)));
        if (v.Count.Text != count) v.Count.Text = count;
    }

    /// <summary>WPF LauncherDoorCard.Drift (:595-614): a slow push 1.0 to 1.05 with a 0..5 px pan,
    /// alternating direction, over the hold plus the drift (about 12 s).</summary>
    private static void DriftLayer(DoorView v, int layer)
    {
        v.NextLeft = !v.NextLeft;
        v.DriftLeft[layer] = v.NextLeft;
        v.Drift[layer].Go(0, 1, DoorDriftSeconds, Ease.Linear);
        PaintDrift(v, layer);
    }

    private static void PaintDrift(DoorView v, int layer)
    {
        double u = v.Drift[layer].Step(0);
        v.Zoom[layer].ScaleX = v.Zoom[layer].ScaleY = 1 + (DoorReelZoom - 1) * u;
        v.Pan[layer].X = v.DriftLeft[layer] ? DoorDriftPan * (1 - u) : DoorDriftPan * u;
    }

    /// <summary>One frame of every door tween; true while anything still moves.</summary>
    private bool StepDoors(double dt)
    {
        bool busy = false, runThen = false;
        if (_doorFade.On)
        {
            busy = true;
            GamesScroller.Opacity = _doorFade.Step(dt);
            if (!_doorFade.On) runThen = true;
        }
        if (_doorThenIn >= 0) { busy = true; if ((_doorThenIn -= dt) < 0) { _doorThenIn = -1; runThen = true; } }
        foreach (var v in _doorViews)
        {
            if (!v.Busy) continue;
            busy = true;
            if (v.Wait > 0) { v.Wait -= dt; PaintDoor(v); }
            else
            {
                v.Scale.Step(dt); v.X.Step(dt); v.Y.Step(dt); v.Fade.Step(dt); v.Hover.Step(dt); v.Back.Step(dt);
                v.Zoomed.Step(dt); v.Ink.Step(dt); v.Tally.Step(dt);
                PaintDoor(v);
            }
            if (v.StampWait >= 0 && (v.StampWait -= dt) < 0)
            {
                v.StampWait = -1;
                v.Pop.Go(0, 1, GentleVisuals.RiseMs(320) / 1000.0, Ease.Linear);
            }
            if (v.Pop.On)
            {
                double u = v.Pop.Step(dt);
                // The stamp lands with one small overshoot; Gentle visuals only grow it in.
                double k = GentleVisuals.On ? u : u < 0.6 ? 1.18 * u / 0.6 : 1.18 - 0.18 * (u - 0.6) / 0.4;
                v.StampScale.ScaleX = v.StampScale.ScaleY = k;
            }
            for (int i = 0; i < 2; i++)
                if (v.Drift[i].On) { v.Drift[i].Step(dt); PaintDrift(v, i); }
            if (v.ReelIn.On)
            {
                double u = v.ReelIn.Step(dt);
                int back = 1 - v.Front;
                v.Layers[back].Opacity = u;
                if (!v.ReelIn.On) { v.Layers[v.Front].Opacity = 0; v.Drift[v.Front].On = false; v.Front = back; }
            }
        }
        if (runThen) RunDoorThen();
        return busy;
    }

    private void RunDoorThen()
    {
        var then = _doorThen;
        _doorThen = null;
        _doorThenIn = -1;
        _doorFade.On = false;
        try { then?.Invoke(); }
        catch (Exception ex) { Log.Warning(ex, "[Launcher] step after a door swap failed"); }
    }

    /// <summary>Hidden or minimised: a pending swap lands at once, every door at rest, the reel off.</summary>
    private void ParkDoors()
    {
        StopDoorReels();
        ConditioningControlPanel.Avalonia.Controls.CardSheenAdorner.Detach(_doorSheen); _doorSheen = null;
        if (_doorThen != null) RunDoorThen();
        _doorSwapping = false;
        GamesScroller.Opacity = 1;
        foreach (var v in _doorViews) DoorRest(v);
    }

    // ------------------------------------------------------------------ the reel

    /// <summary>The reels drift and cross-fade to the next picture now and then, only with ambient loops on
    /// and never on the software floor (each frame repaints the card). WPF StartReel: the front picture
    /// drifts from its start again, the first one too; restored from minimised runs this again.</summary>
    private void StartDoorReels()
    {
        if (_door != null || !FxLive || !DoorLively || Env.SoftwareFloorHoldsIdleLoops) return;
        foreach (var v in _doorViews) if (v.Arts.Count > 0) DriftLayer(v, v.Front);
        EnsureFxClock();
        if (!_doorViews.Any(v => v.Arts.Count > 1)) return;
        if (_doorReelTimer == null)
        {
            _doorReelTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(DoorReelHoldSeconds) };
            _doorReelTimer.Tick += (_, _) => OnDoorReelTick();
        }
        _doorReelTimer.Start();
    }

    private void StopDoorReels()
    {
        _doorReelTimer?.Stop();
        foreach (var v in _doorViews)
        {
            v.Drift[0].On = v.Drift[1].On = false;   // WPF StopReel freezes the drift where it is
            if (!v.ReelIn.On) continue;
            v.ReelIn.On = false;
            int back = 1 - v.Front;
            v.Layers[back].Opacity = 1;
            v.Layers[v.Front].Opacity = 0;
            v.Front = back;
        }
    }

    private void OnDoorReelTick()
    {
        if (_door != null || !FxLive || !DoorLively || _doorSwapping) { _doorReelTimer?.Stop(); return; }
        foreach (var v in _doorViews)
        {
            if (v.Arts.Count < 2 || v.ReelIn.On) continue;
            v.ArtAt = (v.ArtAt + 1) % v.Arts.Count;
            int back = 1 - v.Front;
            v.Layers[back].Source = v.Arts[v.ArtAt];
            v.Layers[back].Opacity = 0;
            DriftLayer(v, back);
            v.ReelIn.Go(0, 1, GentleVisuals.FadeMs((int)(DoorReelFadeSeconds * 1000)) / 1000.0, Ease.Linear);
        }
        EnsureFxClock();
    }

    /// <summary>The doors leave the tree: reel off, hover dropped, nothing held.</summary>
    private void DropDoorCards()
    {
        StopDoorReels();
        ConditioningControlPanel.Avalonia.Controls.CardSheenAdorner.Detach(_doorSheen); _doorSheen = null;
        if (_doorHover != null) { _doorHover = null; RestoreGlow(); }
        _doorViews.Clear();
        DoorsHost.Children.Clear();
    }
}
