// WPF MainWindow.ProfileCosmetics: paint every equipped and cleared state, the preset avatar's
// share of the picture slot (#847), click-to-pin with its cap notice, and the mod-switch pin repaint.
using System;
using Avalonia;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // ponytail: retain the existing Core sanitization and ModArt decoders. This painter
        // handles banner/accent/title/pins beside the already wired wardrobe apply path.
        private void ApplyProfileCosmeticsPaint(ProfileCosmetics cosmetics)
        {
            var page = ProfilePage;
            if (page == null) return;
            // WPF wraps each ApplyProfileX in its own try: one failing step must not skip the rest.
            Step("banner", () =>
            {
                if (page.FindControl<Border>("ProfileHeroBanner") is { } banner)
                    banner.Background = ModArt.Banner(cosmetics.BannerId, 1024, AlignmentY.Top);
            });
            Step("avatar", () => ApplyProfileAvatarPreset(cosmetics.AvatarId));
            Step("accent", () => PaintAccent(page, cosmetics));
            Step("title", () => PaintTitle(page, cosmetics));
            Step("pins", () => PaintPins(page, cosmetics.PinnedAchievements));
            if (!_pinModHooked)
            {
                // WPF UiUpdates sweep: "showcase pins" re-resolve on a mod switch. Per-window (P41/P68).
                _pinModHooked = true;
                CoreMods.ModChanged += OnModChangedPins;
                Closed += (_, _) => CoreMods.ModChanged -= OnModChangedPins;
            }
        }

        private bool _pinModHooked;
        private void OnModChangedPins(object? sender, ModPackage e) => Dispatcher.UIThread.Post(RefreshShowcasePinArt);

        /// <summary>WPF RefreshShowcasePinArt: re-derive the ids from the tiles already on the shelf (the card on
        /// screen may be someone else's) and repaint their art and mod-aware titles; a no-op on an empty shelf.</summary>
        internal void RefreshShowcasePinArt()
        {
            try
            {
                var page = ProfilePage;
                if (page?.FindControl<ItemsControl>("ProfilePinnedShowcase")?.ItemsSource is not System.Collections.IEnumerable source) return;
                var ids = source.OfType<ProfileAchievementTile>().Select(t => t.Id).ToList();
                if (ids.Count > 0) PaintPins(page, ids);
            }
            catch (Exception ex) { Log.Debug("RefreshShowcasePinArt: {E}", ex.Message); }
        }

        // ---- the avatar slot (WPF _appliedPresetAvatar / _profilePictureLoad, bug #847) ----

        private IImageBrushSource? _appliedPresetAvatar;
        private ProfilePictureLoad _profilePictureLoad = ProfilePictureLoad.None;
        private string? _lastPresetAvatarId;
        private int _profilePictureGen;

        /// <summary>Test seam: fetch + decode a profile picture; null when there is none.</summary>
        internal static Func<string, Task<Bitmap?>> FetchProfilePhoto = async url =>
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var bytes = await http.GetByteArrayAsync(url);
            return await Task.Run(() => { using var ms = new MemoryStream(bytes); return new Bitmap(ms); });
        };

        /// <summary>WPF SetProfilePictureLoad: the load path owns the slot the moment it writes it.</summary>
        internal void SetProfilePictureLoad(ProfilePictureLoad load)
        {
            _appliedPresetAvatar = null;
            _profilePictureLoad = load;
        }

        /// <summary>The card's picture load (WPF Browser.cs:1890-1926 / 2152 / 2413-2459). Empties the slot,
        /// then: no url -> None (final; the preset may claim it); a url -> Pending until the bytes land
        /// (Loaded) or fail (None, and the preset gets the slot it was kept out of).</summary>
        internal void LoadProfilePicture(string? url, bool pending = false)
        {
            var avatar = ProfilePage?.ProfileHeroAvatar;
            if (avatar == null) return;
            var gen = ++_profilePictureGen;
            avatar.AvatarImage = null;
            if (string.IsNullOrEmpty(url)) { SetProfilePictureLoad(pending ? ProfilePictureLoad.Pending : ProfilePictureLoad.None); return; }
            SetProfilePictureLoad(ProfilePictureLoad.Pending);
            _ = FetchProfilePictureAsync(url, gen);
        }

        private async Task FetchProfilePictureAsync(string url, int gen)
        {
            Bitmap? photo = null;
            try { photo = await FetchProfilePhoto(url); }
            catch (Exception ex) { Log.Warning("Failed to load profile avatar from {Host}: {E}", ConditioningControlPanel.Services.Logging.UrlLog.Host(url), ex.Message); }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (gen != _profilePictureGen || ProfilePage?.ProfileHeroAvatar is not { } avatar) return;  // the card moved on
                if (photo != null) { avatar.AvatarImage = photo; SetProfilePictureLoad(ProfilePictureLoad.Loaded); }
                else ResolveProfilePictureUnavailable();
            });
        }

        /// <summary>WPF ResolveProfilePictureUnavailable: the load came back with nothing, so the slot is free
        /// and gets what it should have had all along.</summary>
        internal void ResolveProfilePictureUnavailable()
        {
            _profilePictureGen++;
            if (ProfilePage?.ProfileHeroAvatar is { } avatar) avatar.AvatarImage = null;
            SetProfilePictureLoad(ProfilePictureLoad.None);
            Step("avatar", () => ApplyProfileAvatarPreset(_lastPresetAvatarId));
        }

        /// <summary>WPF ApplyProfileAvatarPreset: our previous preset is ours to swap or clear, a real picture is
        /// left alone, and an empty slot is ours only once the picture load has finished with nothing.</summary>
        private void ApplyProfileAvatarPreset(string? avatarId)
        {
            _lastPresetAvatarId = avatarId;
            var avatar = ProfilePage?.ProfileHeroAvatar;
            if (avatar == null) return;
            var current = avatar.AvatarImage;
            var ours = _appliedPresetAvatar != null && ReferenceEquals(current, _appliedPresetAvatar);
            if (!ProfileAvatarSlot.PresetMayClaim(ours, current != null, _profilePictureLoad)) return;
            var art = ModArt.AvatarPreset(avatarId);
            avatar.AvatarImage = art;
            _appliedPresetAvatar = art;
        }

        private static void Step(string name, Action paint)
        {
            try { paint(); }
            catch (Exception ex) { Log.Debug("ApplyProfileCosmeticsPaint {Step}: {E}", name, ex.Message); }
        }

        private static void PaintAccent(DiscordTabView page, ProfileCosmetics cosmetics)
        {
            var hasAccent = cosmetics.Accent != null;
            var accent = Color.Parse(cosmetics.Accent ?? "#FF69B4");
            page.ProfileHeroCard.BorderBrush = new SolidColorBrush(Color.FromArgb(0x99, accent.R, accent.G, accent.B));
            if (page.FindControl<Border>("ProfileAccentGlow") is { } glow)
                glow.BoxShadow = hasAccent
                    ? new BoxShadows(new BoxShadow { Color = Color.FromArgb(0x80, accent.R, accent.G, accent.B), Blur = 22 })
                    : default;
            var headerBrush = new SolidColorBrush(hasAccent ? accent : Colors.White);
            foreach (var name in new[] { "TxtProfileRecordHeader", "TxtProfileShowcaseHeader", "TxtProfileCommunityHeader" })
                if (page.FindControl<TextBlock>(name) is { } header) header.Foreground = headerBrush;
        }

        private static void PaintTitle(DiscordTabView page, ProfileCosmetics cosmetics)
        {
            if (page.FindControl<TextBlock>("TxtProfileEquippedTitle") is { } title)
            {
                title.Text = cosmetics.TitleId != null && Achievement.All.TryGetValue(cosmetics.TitleId, out var achievement)
                    ? CoreMods.MakeModAware(achievement.TitleName) : string.Empty;
                title.IsVisible = !string.IsNullOrEmpty(title.Text);
            }
        }

        private void PaintPins(DiscordTabView page, List<string> pinnedIds)
        {
            var tiles = new List<ProfileAchievementTile>();
            foreach (var id in pinnedIds)
            {
                if (!Achievement.All.TryGetValue(id, out var achievement)) continue;
                var art = ModArt.TryLoad($"achievements/{achievement.ImageName}", 176);
                if (art != null) tiles.Add(new ProfileAchievementTile(id, CoreMods.MakeModAware(achievement.TitleName), art));
            }
            if (page.FindControl<ItemsControl>("ProfilePinnedShowcase") is { } pins)
                pins.ItemsSource = tiles.Count > 0 ? tiles : null;
            if (page.FindControl<StackPanel>("ProfilePinnedPlaceholders") is { } placeholders)
                placeholders.IsVisible = PinPlaceholdersVisible(tiles.Count > 0);
        }

        // ---- click-to-pin (WPF ToggleOwnAchievementPin / FlashPinCapNotice) ----

        /// <summary>Left-click on a Showcase tile toggles that achievement's pin. Own card, unlocked only.</summary>
        internal void ToggleOwnAchievementPin(string? achievementId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(achievementId) || !_profileViewingSelf) return;
                var unlocked = App.Achievements?.Progress?.UnlockedAchievements;
                if (unlocked == null || !unlocked.Contains(achievementId)) return;
                var current = SanitizeOwnWardrobe(CoreSettings.Current.ProfileCosmetics);
                if (!current.PinnedAchievements.Remove(achievementId))
                {
                    if (current.PinnedAchievements.Count >= ProfileCosmetics.MaxPinnedAchievements) { FlashPinCapNotice(); return; }
                    current.PinnedAchievements.Add(achievementId);
                }
                PersistOwnCosmetics(current);
            }
            catch (Exception ex) { Log.Debug("ToggleOwnAchievementPin: {E}", ex.Message); }
        }

        internal DispatcherTimer? _pinCapNoticeTimer;

        /// <summary>A dead click at the cap reads as a broken tile, so the unlock summary says why for 2.5 s.</summary>
        private void FlashPinCapNotice()
        {
            if (ProfilePage?.FindControl<TextBlock>("TxtProfileUnlockSummary") is not { } summary) return;
            _pinCapNoticeTimer?.Stop();
            var original = summary.Text;
            summary.Text = Loc.GetF("profile_customize_pins_full", ProfileCosmetics.MaxPinnedAchievements);
            summary.Foreground = SolidColorBrush.Parse("#FF5C7A");
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                summary.Text = original;
                summary.Bind(TextBlock.ForegroundProperty, summary.GetResourceObservable("TextSecondaryBrush"));
            };
            _pinCapNoticeTimer = timer;
            timer.Start();
        }
    }
}
