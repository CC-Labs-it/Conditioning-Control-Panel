using System;
using System.Windows;
using ConditioningControlPanel.Models;
#if DEBUG
using ConditioningControlPanel.Services.PrizeCounter;
#endif

namespace ConditioningControlPanel.Services
{
    /// <summary>Who answers the DEBUG shop preview's own frame and picture. Only the preview sets one.</summary>
    internal interface IPreviewProfileLook
    {
        (string? Frame, string? Avatar) Worn();
        bool Owns(string itemId);
        bool Wear(string itemId);
        void Set(string? frameId, string? avatarId);
        event Action? Changed;
    }

#if DEBUG
    /// <summary>The DEBUG shop preview's frame and picture: the same <c>shop-preview.json</c> the sheet buys into.</summary>
    internal sealed class PreviewProfileLook : IPreviewProfileLook
    {
        private readonly ShopPreviewBackend _backend;
        public PreviewProfileLook(ShopPreviewBackend backend) { _backend = backend; }
        public (string? Frame, string? Avatar) Worn() => _backend.ProfileWorn();
        public bool Owns(string itemId) => _backend.Owns(itemId);
        public bool Wear(string itemId) => _backend.WearProfile(itemId);
        public void Set(string? frameId, string? avatarId) => _backend.SetProfile(frameId, avatarId);
        public event Action? Changed { add => _backend.ProfileChanged += value; remove => _backend.ProfileChanged -= value; }
    }
#endif

    /// <summary>
    /// THE OWN LOOK: the one seam the user's own avatar surfaces read their frame and picture from (the profile hero,
    /// the account chip top right, the friends chip and the friends drawer's own row, the Customize dialog), and the
    /// counter's Wear on a profile row writes through.
    /// <para>Real account: the sanitized <c>ProfileCosmetics</c> in settings (ProfileEquipSync keeps it with the server).
    /// DEBUG shop preview: the preview's own worn frame and picture, DISPLAY ONLY. Preview state never reaches the
    /// account, the settings' equip fields or the server; leaving preview mode shows the real frame again, because
    /// nothing real was changed.</para>
    /// </summary>
    public static class OwnProfileLook
    {
        private static IPreviewProfileLook? _preview;

        /// <summary>The preview's look (ShopPreviewBackend.Wire, DEBUG only); null = the real account.</summary>
        internal static IPreviewProfileLook? Preview
        {
            get => _preview;
            set
            {
                if (ReferenceEquals(_preview, value)) return;
                if (_preview != null) _preview.Changed -= Raise;
                _preview = value;
                if (_preview != null) _preview.Changed += Raise;
                Raise();
            }
        }

        /// <summary>True while the own surfaces show the DEBUG preview's look.</summary>
        public static bool InPreview => _preview != null;

        /// <summary>The real account's own loadout, sanitized. Settable for tests.</summary>
        internal static Func<ProfileCosmetics> RealOwn { get; set; } = () => CosmeticsCatalog.SanitizeOwn(App.Settings?.Current?.ProfileCosmetics);
        /// <summary>The real account's counter Wear (settings first, PUT behind). Settable for tests.</summary>
        internal static Func<string, bool> RealWear { get; set; } = id => App.MainWindowRef?.WearProfileItemFromCounter(id) == true;
        /// <summary>The real account holds a grant. Settable for tests.</summary>
        internal static Func<string, bool> RealGranted { get; set; } = grant => App.Ownership?.IsGranted(grant) == true;

        /// <summary>The frame and picture the own surfaces wear now. Never throws.</summary>
        public static ProfileSlots Current
        {
            get
            {
                try
                {
                    var p = _preview;
                    if (p == null) return ProfileSlots.Of(RealOwn());
                    var (frame, avatar) = p.Worn();
                    return new ProfileSlots(Known(p, frame, frame: true), Known(p, avatar, frame: false));
                }
                catch { return default; }
            }
        }

        /// <summary><see cref="Current"/> as a <see cref="PlayerLook"/> for the friends drawer's own row and chip.</summary>
        public static PlayerLook Look { get { var c = Current; return new PlayerLook(c.AvatarId, c.FrameId); } }

        private static string? Known(IPreviewProfileLook p, string? id, bool frame)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (ShopCosmetics.Find(id) is not { } item || (frame ? !item.IsFrame : !item.IsAvatar)) return null;
            return p.Owns(id) ? id : null;
        }

        /// <summary>The own loadout to paint: in preview a COPY with the preview's frame and picture over the real
        /// ones (everything else stays the account's); otherwise <paramref name="own"/> as it is.</summary>
        public static ProfileCosmetics Overlay(ProfileCosmetics own)
        {
            if (_preview == null) return own;
            var copy = own.Clone();
            var c = Current;
            copy.FrameId = c.FrameId;
            copy.AvatarId = c.AvatarId;
            return copy;
        }

        /// <summary>Whether the own account owns a shop frame or picture: the preview's owned marks in preview, the
        /// account's grants otherwise (the Customize dialog's lock).</summary>
        public static bool Owns(ShopCosmeticItem? item)
        {
            if (item == null) return false;
            try { return _preview != null ? _preview.Owns(item.Id) : RealGranted(item.Grant); }
            catch { return false; }
        }

        /// <summary>The counter's Wear on an owned profile frame or picture. Preview: the preview file only.</summary>
        public static bool WearFromCounter(string itemId)
        {
            var p = _preview;
            if (p != null)
            {
                var item = ShopCosmetics.Find(itemId);
                if (item == null || (!item.IsFrame && !item.IsAvatar)) return false;
                try { return p.Wear(itemId); } catch { return false; }
            }
#if DEBUG
            if (ShopPreviewBackend.Enabled) return false;   // a preview never writes the real account, wired or not
#endif
            try { return RealWear(itemId); } catch { return false; }
        }

        /// <summary>The Customize dialog's save of frame and picture while in preview (the preview file only).
        /// False when not in preview: the caller saves the real loadout as always.</summary>
        public static bool SaveToPreview(string? frameId, string? avatarId)
        {
            var p = _preview;
            if (p == null) return false;
            try { p.Set(frameId, avatarId); } catch (Exception ex) { App.Logger?.Debug("OwnProfileLook: preview save failed: {E}", ex.Message); }
            return true;
        }

        /// <summary>The own frame or picture changed (a counter Wear, the Customize dialog, a pick on another
        /// device). Always raised on the UI thread when there is one.</summary>
        public static event Action? Changed;

        /// <summary>Say the own look changed. Any thread.</summary>
        public static void Raise()
        {
            try
            {
                var d = Application.Current?.Dispatcher;
                if (d == null || d.CheckAccess()) Changed?.Invoke();
                else d.BeginInvoke(new Action(() => Changed?.Invoke()));
            }
            catch (Exception ex) { App.Logger?.Debug("OwnProfileLook: change listeners failed: {E}", ex.Message); }
        }
    }
}
