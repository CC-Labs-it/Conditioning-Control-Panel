// PORTED from ConditioningControlPanel/MainWindow/MainWindow.FavoritesRail.cs (495 lines).
//
// The dashboard's FAVORITES + RECENT column (SettingsTabView, col 0), which replaced the premium
// quick-toggle rail on WPF (owner call 2026-09-11). A chip IS a Ctrl+K palette row (Core
// SettingsPaletteIndex): same id, label and navigation; the list rules are Core FavoritesRailRule,
// the pictures Core FavoritesRailArt + ModArtFramingRegistry's railChip crop, the lists
// AppSettings.RailFavorites / RailRecent. Lock truth is IsNavEntryLocked (NavPremiumTags.cs), so the
// chip's padlock and the rail's gold star cannot disagree.
//
// ponytail: live prize snapshots still depend on svc-prize-ownership. Like the feature pickers,
// chips read this head's PrizeOwnership seam; settings never confer ownership.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private bool _favoritesRailHooked;
        internal PageFavoritesDrawer? FavoritesPageDrawer => Named<PageFavoritesDrawer>("PageDrawer");

        /// <summary>WPF FavoritePinMap (MainWindow.FavoritesRail.cs:63): rail/Play-wall x:Name -> palette id.</summary>
        private static readonly (string Element, string Id)[] FavoritePinMap =
        {
            ("DoorHome", "door.home"), ("DoorStudio", "door.studio"), ("DoorCompanion", "door.companion"),
            ("DoorPlay", "door.play"), ("DoorYou", "door.you"), ("DoorSocial", "door.social"), ("DoorLibrary", "door.library"),
            ("DoorSettings", "door.settings"),
            // Nav rework (2026-10-06): the door rows (BtnSettings ... BtnNavMediaLog, BtnNavJustDrop)
            // left the rail for the pages' pill strips, so their pin rows went with them (WPF :68).
            ("BtnPlayRemoteControl", "tab.remotecontrol"), ("BtnPlayBlinkTrainer", "tab.blinktrainer"),
            ("BtnPlayGradedIntake", "tab.gradedintake"), ("BtnPlayFyp", "tab.fyp"),
            ("BtnPlayLockdown", "tab.lockdown"),
        };

        /// <summary>WPF InitFavoritesRail (MainWindow.xaml.cs:3501): pin menus, first paint, and a
        /// rebuild on language change (the chips are built in code, P09). Tier changes arrive through
        /// RefreshNavPremiumTags (App.axaml.cs RepaintVeils), as on WPF.</summary>
        internal void InitFavoritesRail()
        {
            if (!_favoritesRailHooked)
            {
                _favoritesRailHooked = true;
                WireFavoritePinMenus();
                EventHandler onLanguage = (_, _) => RefreshFavoritesRail();
                Action onGrants = () => Dispatcher.UIThread.Post(RefreshFavoritesRail);
                EventHandler<ModPackage> onMod = (_, _) => Dispatcher.UIThread.Post(RefreshFavoritesRail);
                LocalizationManager.Instance.LanguageChanged += onLanguage;
                PrizeGrants.GrantsChanged += onGrants;
                CoreMods.ModChanged += onMod;
                Closed += (_, _) =>
                {
                    LocalizationManager.Instance.LanguageChanged -= onLanguage;
                    PrizeGrants.GrantsChanged -= onGrants;
                    CoreMods.ModChanged -= onMod;
                };
            }
            RefreshFavoritesRail();
            SettingsPage?.ApplyFavoritesDrawerSetting();
        }

        /// <summary>WPF RefreshFavoritesRail (:121): rebuilds both chip stacks from settings.</summary>
        internal void RefreshFavoritesRail()
        {
            var dash = SettingsPage;
            var s = CoreSettings.Current;
            if (dash?.FavoritesList == null || dash.RecentList == null || s == null) return;
            dash.RefreshClickPreference();
            try
            {
                var byId = SettingsPaletteIndex.All
                    .Where(e => FavoritesRailRule.IsDestination(e.Id))
                    .ToDictionary(e => e.Id, e => e, StringComparer.Ordinal);
                bool Known(string id) => byId.TryGetValue(id, out var e) && e.Available;

                dash.FavoritesList.Children.Clear();
                foreach (var id in s.RailFavorites.Where(Known))
                    dash.FavoritesList.Children.Add(BuildRailChip(dash, byId[id]));
                dash.FavoritesEmpty.IsVisible = dash.FavoritesList.Children.Count == 0;

                dash.RecentList.Children.Clear();
                foreach (var id in FavoritesRailRule.RecentForDisplay(s.RailRecent, s.RailFavorites, Known))
                    dash.RecentList.Children.Add(BuildRailChip(dash, byId[id]));
                dash.RecentEmpty.IsVisible = dash.RecentList.Children.Count == 0;
                if (FavoritesPageDrawer is { } drawer)
                {
                    drawer.Paint(AmbientFxCanvas.Env.GlowColor);
                    drawer.FavoritesList.Children.Clear();
                    foreach (var id in s.RailFavorites.Where(Known))
                        drawer.FavoritesList.Children.Add(BuildRailChip(dash, byId[id]));
                    drawer.RecentList.Children.Clear();
                    foreach (var id in FavoritesRailRule.RecentForDisplay(s.RailRecent, s.RailFavorites, Known))
                        drawer.RecentList.Children.Add(BuildRailChip(dash, byId[id]));
                    drawer.FavoritesEmpty.IsVisible = drawer.FavoritesList.Children.Count == 0;
                    drawer.RecentEmpty.IsVisible = drawer.RecentList.Children.Count == 0;
                }
            }
            catch (Exception ex) { Log.Debug("RefreshFavoritesRail: {E}", ex.Message); }
        }

        private Button BuildRailChip(Tabs.SettingsTabView dash, SettingsPaletteEntry entry)
        {
            bool locked = IsNavEntryLocked(entry.TabKey);
            var grid = new Grid();
            var face = BuildRailChipArt(dash, entry.Id, grid, out bool painted);
            if (!painted)
            {
                grid.Children.Add(RailChipScrim(dash));
                grid.Children.Add(new TextBlock
                {
                    Text = entry.Glyph, FontSize = 17,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            }
            // WPF RailChipCaption style, inline.
            grid.Children.Add(new TextBlock
            {
                Text = entry.Label, FontSize = 8.5, FontWeight = FontWeight.SemiBold, Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(2, 0, 2, 3),
            });
            if (locked)
            {
                grid.Children.Add(new TextBlock
                {
                    Text = "🔒", FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 3, 0),
                });
            }

            AddRailChipV2Pill(entry.Id, grid, locked);
            var chip = new Button { Content = grid, Tag = entry.Id };
            if (dash.FavoritesRail.TryFindResource("RailChipStyle", out var theme) && theme is global::Avalonia.Styling.ControlTheme ct)
                chip.Theme = ct;
            ToolTip.SetTip(chip, string.IsNullOrEmpty(entry.Context)
                ? entry.Label + "\n" + Loc.Get("rail_chip_tip")
                : entry.Label + "  ·  " + entry.Context + "\n" + Loc.Get("rail_chip_tip"));
            if (face != null) chip.Background = face;
            if (locked && this.TryFindResource("Tier1GoldBorderBrush", out var gold) && gold is IBrush g) chip.BorderBrush = g;
            chip.Click += (_, _) => OpenDestination(entry);
            AttachPinMenu(chip, entry.Id);
            return chip;
        }

        private static void AddRailChipV2Pill(string id, Grid grid, bool locked)
        {
            bool Owned(string grant) => PrizeOwnership.IsGranted(grant);
            if (!V2Badges.ChipWearsV2Pill(id,
                V2Badges.FlashRule(Owned(PrizeGrants.FlashDriftBounce), Owned(PrizeGrants.FlashPendulum), Owned(PrizeGrants.JackpotRemix)),
                V2Badges.BubbleRule(Owned(PrizeGrants.BubbleRain), Owned(PrizeGrants.BubbleSpiralIn)))) return;
            var champagne = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x8A));
            grid.Children.Add(new Border
            {
                Margin = locked ? new Thickness(3, 2, 0, 0) : new Thickness(0, 2, 3, 0),
                Padding = new Thickness(3, 0, 4, 1), CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(0xD9, 0x1A, 0x1A, 0x2E)),
                BorderBrush = champagne, BorderThickness = new Thickness(1), IsHitTestVisible = false,
                HorizontalAlignment = locked ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = Loc.Get("badge_v2"), Foreground = champagne, FontSize = 7, FontWeight = FontWeight.Bold },
            });
        }

        // WPF ActiveModFraming: hand-authored manifest keys ignore case and normalize separators.
        private static ModArtFraming? ActiveRailFraming(string resourcePath)
        {
            var map = CoreMods.ActiveModPackage?.Manifest.ArtFraming;
            if (map == null) return null;
            foreach (var entry in map)
            {
                if (entry.Value == null || !string.Equals(entry.Key.Replace('\\', '/'), resourcePath, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (var surface in entry.Value)
                    if (string.Equals(surface.Key, ModArtFramingRegistry.SurfaceRailChip, StringComparison.OrdinalIgnoreCase))
                        return surface.Value;
                return null;
            }
            return null;
        }

        /// <summary>WPF BuildRailChipArt (:253): Cover (scene art, railChip crop) or Plate (square icon).</summary>
        private static IBrush? BuildRailChipArt(Tabs.SettingsTabView dash, string paletteId, Grid grid, out bool painted)
        {
            painted = false;
            try
            {
                var art = FavoritesRailArt.For(paletteId);
                if (art == null) return null;
                var candidates = FavoritesRailArt.Candidates(art.ResourcePath, FavoritesRailArt.ThemeSuffix(CoreMods.ActiveModId));
                return art.Fit == RailArtFit.Plate
                    ? BuildPlateFace(dash, candidates, grid, out painted)
                    : BuildCoverFace(dash, art.ResourcePath, candidates, grid, out painted);
            }
            catch (Exception ex) { Log.Debug("BuildRailChipArt({Id}): {E}", paletteId, ex.Message); }
            return null;
        }

        private static IBrush? BuildCoverFace(Tabs.SettingsTabView dash, string basePath, IReadOnlyList<string> candidates,
                                              Grid grid, out bool painted)
        {
            painted = false;
            foreach (var path in candidates)
            {
                var probe = RailArt(path, FavoritesRailArt.BaseDecodeWidth);
                if (probe == null) continue;
                bool modArt = CoreModArt.HasOverride(path);
                double aspect = probe.Size.Height > 0 ? probe.Size.Width / probe.Size.Height : 0;
                var vb = ModArtFramingRegistry.ResolveViewbox(basePath, ModArtFramingRegistry.SurfaceRailChip, modArt, aspect, modArt ? ActiveRailFraming(basePath) : null);
                var width = FavoritesRailArt.DecodeWidthFor(vb.Width);
                var bitmap = width > FavoritesRailArt.BaseDecodeWidth ? RailArt(path, width) ?? probe : probe;
                grid.Children.Add(RailChipScrim(dash));
                painted = true;
                return new ImageBrush(bitmap)
                {
                    Stretch = Stretch.UniformToFill,
                    SourceRect = new RelativeRect(vb.X, vb.Y, vb.Width, vb.Height, RelativeUnit.Relative),
                };
            }
            return null;
        }

        private static IBrush? BuildPlateFace(Tabs.SettingsTabView dash, IReadOnlyList<string> candidates, Grid grid, out bool painted)
        {
            painted = false;
            foreach (var path in candidates)
            {
                var icon = RailArt(path, FavoritesRailArt.PlateIconDecodeWidth);
                if (icon == null) continue;
                var soft = RailArt(path, FavoritesRailArt.PlateBackdropDecodeWidth) ?? icon;
                grid.Children.Add(new Border
                {
                    CornerRadius = new CornerRadius(8), IsHitTestVisible = false,
                    Background = new SolidColorBrush(Color.FromArgb(0xA6, 0x0E, 0x0A, 0x1A)),
                });
                grid.Children.Add(RailChipScrim(dash));
                grid.Children.Add(new Border
                {
                    Width = FavoritesRailArt.PlateIconSize, Height = FavoritesRailArt.PlateIconSize,
                    CornerRadius = new CornerRadius(5), ClipToBounds = true,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 1, 0, 0), IsHitTestVisible = false,
                    Background = new ImageBrush(icon) { Stretch = Stretch.Uniform },
                });
                painted = true;
                return new ImageBrush(soft) { Stretch = Stretch.UniformToFill };
            }
            return null;
        }

        private static Border RailChipScrim(Tabs.SettingsTabView dash) => new()
        {
            CornerRadius = new CornerRadius(8), IsHitTestVisible = false,
            Background = dash.FavoritesRail.TryFindResource("RailChipScrim", out var b) ? b as IBrush : null,
        };

        // WPF's ModResourceResolver decode cache, keyed by what actually resolves (a mod switch
        // changes the override path, so it cannot serve the old mod's picture).
        private static readonly Dictionary<(string, int), Bitmap?> RailArtCache = new();
        private static Bitmap? RailArt(string path, int width)
        {
            var key = (CoreModArt.OverridePath(path) ?? path, width);
            lock (RailArtCache)
            {
                if (!RailArtCache.TryGetValue(key, out var bmp))
                    RailArtCache[key] = bmp = ModArt.TryLoad(path, width);
                return bmp;
            }
        }

        /// <summary>WPF OpenDestination (:357): the palette's navigation verb; the four Library
        /// launchers press their button instead.</summary>
        internal void OpenDestination(SettingsPaletteEntry entry)
        {
            try
            {
                if (entry.Id.StartsWith("launch.", StringComparison.Ordinal)
                    && entry.ElementNames.Length > 0
                    && Named<Button>(entry.ElementNames[0]) is { } launcher)
                {
                    launcher.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return;
                }
                SettingsPaletteWindow.Navigate(this, entry);
            }
            catch (Exception ex) { Log.Warning(ex, "Favorites rail: could not open {Id}", entry.Id); }
        }

        /// <summary>WPF NoteDestinationOpened (:378): RECENT's one input, called from ShowTab.</summary>
        internal void NoteDestinationOpened(string tabKey)
        {
            try
            {
                var s = CoreSettings.Current;
                if (s == null) return;
                var id = FavoritesRailRule.DestinationIdForTab(tabKey, SettingsPaletteIndex.All);
                if (id == null) return;
                var entry = SettingsPaletteIndex.All.FirstOrDefault(e => e.Id == id);
                if (entry == null || !entry.Available) return;
                if (!FavoritesRailRule.NoteOpened(s.RailRecent, id)) return;
                CoreSettings.Save();
                RefreshFavoritesRail();
            }
            catch (Exception ex) { Log.Debug("NoteDestinationOpened({Tab}): {E}", tabKey, ex.Message); }
        }

        internal void TogglePinned(string id)
        {
            var s = CoreSettings.Current;
            if (s == null) return;
            bool wasPinned = FavoritesRailRule.IsPinned(s.RailFavorites, id);
            bool changed = wasPinned
                ? FavoritesRailRule.Unpin(s.RailFavorites, id)
                : FavoritesRailRule.TryPin(s.RailFavorites, id);
            if (!changed) return;
            CoreSettings.Save();
            RefreshFavoritesRail();
            // WPF TogglePinned (11552cadf): the column is a drawer, closed by default, so a new
            // favorite slides it out for a moment.
            if (!wasPinned)
            {
                try { SettingsPage?.PeekFavoritesDrawer(id); }
                catch (Exception ex) { Log.Debug("PeekFavoritesDrawer({Id}): {E}", id, ex.Message); }
                if (FavoritesPageDrawer?.IsVisible == true) FavoritesPageDrawer.SetOpen(true, animate: true);
            }
        }

        internal void CloseFavoritesDrawersForPanic()
        {
            FavoritesPageDrawer?.SetOpen(false, animate: false);
            SettingsPage?.CloseClickChoice();
            // Home's drawer is left alone: its open state is the user's saved choice (WPF too).
        }

        /// <summary>WPF WireFavoritePinMenus (:411): a pin menu on every rail row, door header and
        /// Play card that has a palette row; an unresolved name is skipped.</summary>
        private void WireFavoritePinMenus()
        {
            var play = Named<Control>("PlayTab");
            foreach (var (element, id) in FavoritePinMap)
            {
                bool onPlayWall = element.StartsWith("BtnPlay", StringComparison.Ordinal);
                var target = onPlayWall ? play?.FindControl<Control>(element) : Named<Control>(element);
                if (target == null) { Log.Debug("Favorites rail: no element named {Name} to pin", element); continue; }
                AttachPinMenu(target, id);
            }
        }

        /// <summary>WPF AttachPinMenu (:436): a Pin/Unpin menu rebuilt on each open. No rail hold:
        /// the section rail never overlays the page, so there is nothing to keep open.</summary>
        internal void AttachPinMenu(Control target, string id)
        {
            var menu = new ContextMenu();
            target.ContextMenu = menu;
            menu.Opening += (_, _) =>
            {
                menu.Items.Clear();
                var favs = CoreSettings.Current?.RailFavorites;
                bool pinned = FavoritesRailRule.IsPinned(favs, id);
                var item = new MenuItem
                {
                    Header = pinned ? Loc.Get("rail_unpin")
                           : FavoritesRailRule.IsFull(favs) ? Loc.Get("rail_favorites_full")
                           : Loc.Get("rail_pin"),
                    IsEnabled = pinned || !FavoritesRailRule.IsFull(favs),
                };
                item.Click += (_, _) => TogglePinned(id);
                menu.Items.Add(item);
            };
        }
    }
}
