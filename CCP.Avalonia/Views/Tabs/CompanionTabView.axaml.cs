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
    /// <para><b>What is stubbed, and why it is not a dropped feature.</b> The WPF file's other two
    /// jobs are the runtime viewmodel and the compat seam: it news up a
    /// <c>CompanionRoomRuntimeVm</c>, hands it to <c>Room.ViewModel</c>, and re-publishes ~110
    /// control names (<c>TxtDetachStatusCompanion</c>, the sixteen AiPermissionsGrid names,
    /// <c>_vm.Shelf.Roster.*</c>, <c>_vm.Shelf.Behavior.*</c>, …) so the seven MainWindow partials
    /// keep the accessor path they always had. None of that has anywhere to land here: the ported
    /// <see cref="Controls.Companion.CompanionRoomView"/> has no <c>ViewModel</c> property and no
    /// <c>Shelf</c> zone viewmodels, and the partials that consume the names are WPF-side. Writing
    /// 110 properties that return null would be a seam in name only — it would compile, satisfy a
    /// reader, and break at the first use — so they are deliberately absent rather than faked.
    /// ponytail: needs CompanionRoomRuntimeVm + the zone viewmodels (ICompanionRoomVm's eight zone
    /// interfaces), wired when they move to Core; each passthrough is then one line.</para>
    /// </summary>
    public partial class CompanionTabView : UserControl
    {
        public CompanionTabView()
        {
            InitializeComponent();

            // ponytail: `_vm = new CompanionRoomRuntimeVm(() => Window.GetWindow(this) as
            // MainWindow); Room.ViewModel = _vm;` — needs CompanionRoomRuntimeVm, wired when it
            // moves to Core. The room seats its own per-zone sample data until then.

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
                Conversation.Resume();
            }
            else Conversation.Park();
            (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.OnCompanionTabVisibilityChanged(shown, Room);
        }
    }
}
