using System;
using Avalonia;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/CompanionTabView.xaml.cs. The tab is a host
    /// for one control — <see cref="Controls.Companion.CompanionRoomView"/> — plus the hidden
    /// HelpBtnAiChat compat element. That much crosses verbatim.
    ///
    /// <para>The room owns live zone viewmodels and Workshop cells. Roster card clicks forward
    /// to the shell's Core switch; visibility forwards the room's effective edge. ponytail:
    /// WPF's aggregate runtime viewmodel, v2 ConversationPage/PersonalityStudio mount and ~110
    /// compatibility accessors still need a coordinated port with their shell consumers in
    /// wire-views-tab-companion-b. Do not publish null compatibility controls.</para>
    /// </summary>
    public partial class CompanionTabView : UserControl
    {
        public CompanionTabView()
        {
            InitializeComponent();

            // ponytail: WPF's aggregate runtime viewmodel/compat surface remains in
            // wire-views-tab-companion-b; the room already owns live per-zone viewmodels.

            // WorkshopAccordion owns the single roster-click subscription and forwards to the
            // shell's Core switch, room refresh and tube avatar-set update when hosted here.

            // WPF CompanionTabView.xaml.cs:36-40: the room is collapsed (XAML) and the page reads its zones.
            Conversation.Room = Room;
        }

        // WPF hooks IsVisibleChanged here (MainWindow.OnCompanionTabVisibilityChanged) and the
        // ConversationPage hooks its own (Vm.Resume / Detach). Avalonia's IsVisible is local and
        // the room is collapsed, so the tab watches its own effective visibility for both.
        private IDisposable? _watch;
        private bool _shown;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _watch?.Dispose();
            _watch = EffectiveVisibility.Watch(this, VisibilityEdge);
            VisibilityEdge();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _watch?.Dispose();
            _watch = null;
            base.OnDetachedFromVisualTree(e);
        }

        /// <summary>Raised on a real edge only (the chain reports every ancestor's flip).</summary>
        private void VisibilityEdge()
        {
            bool shown = IsEffectivelyVisible;
            if (shown == _shown) return;
            _shown = shown;
            if (shown)
            {
                Room.HeroZone.ApplyAvatarArt();   // the hero re-read + portrait the page header shows (WPF Room.Sync)
                // WPF MainWindow.TabNavigation.cs:505 ShowTab("companion") -> InitializePhrasePresets, v2 or not:
                // the Triggers cell's combo (on Companion > AI under v2) re-reads presets saved elsewhere.
                Room.WorkshopZone.RefreshPhrasePresets();
                Conversation.Resume();
            }
            else Conversation.Park();
            (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.OnCompanionTabVisibilityChanged(shown, Room);
        }
    }
}
