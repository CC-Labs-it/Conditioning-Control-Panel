using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/MainWindow/MainWindow.AssetsFx.cs:137-178 (OnPackCardHover /
    /// DetachPackCardSheen) and AssetsTabView.xaml.cs:71-81 (PackCard_MouseEnter/Leave): the hovered
    /// pack card catches the shared sheen (8 px corners) and loses it on leave, on tab hide and on detach.
    /// Gated on the INTERACTION clock (AllowTransitions), one card at a time. Like WPF the packs strip
    /// ships hidden (PacksSectionEnabled is const false there), so this is wired and dormant.
    /// </summary>
    public partial class AssetsTabView
    {
        private const double PackCardCornerRadius = 8;   // MainWindow.AssetsFx.cs:38

        internal CardSheenAdorner? PackCardSheen { get; private set; }
        private Control? _packCardSheenHost;

        private void PackCard_PointerEntered(object? sender, PointerEventArgs e) => OnPackCardHover(sender as Control, true);
        private void PackCard_PointerExited(object? sender, PointerEventArgs e) => OnPackCardHover(sender as Control, false);

        internal void OnPackCardHover(Control? card, bool on)
        {
            if (!on)
            {
                if (card == null || ReferenceEquals(card, _packCardSheenHost)) DetachPackCardSheen();
                return;
            }
            if (card == null || !card.IsEffectivelyVisible) return;
            if (!MotionAllowed()) return;
            if (ReferenceEquals(card, _packCardSheenHost) && PackCardSheen != null) return;

            DetachPackCardSheen();
            PackCardSheen = CardSheenAdorner.Attach(card, PackCardCornerRadius);
            _packCardSheenHost = PackCardSheen == null ? null : card;
        }

        private void DetachPackCardSheen()
        {
            CardSheenAdorner.Detach(PackCardSheen);
            PackCardSheen = null;
            _packCardSheenHost = null;
        }
    }
}
