using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.Controls;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class AssetsTabView
    {
        private void InitializePackCardFx() => Unloaded += (_, _) => DetachPackCardSheen();

        private static bool MotionAllowed()
        {
            try { return AmbientFxCanvas.Env.AllowTransitions; }
            catch { return true; }
        }

        private Control? _packCardSheenHost;
        private CardSheenAdorner? _packCardSheen;
        private IDisposable? _packCardVisibility;

        // WPF MainWindow.AssetsFx.cs:OnPackCardHover. The strip stays collapsed as on WPF;
        // ponytail: its cards still use the seeded model, pending the content-pack browser lane.
        private void PackCard_PointerEntered(object? sender, PointerEventArgs e) => OnPackCardHover(sender as Control, true);
        private void PackCard_PointerExited(object? sender, PointerEventArgs e) => OnPackCardHover(sender as Control, false);

        private void OnPackCardHover(Control? card, bool on)
        {
            try
            {
                if (!on)
                {
                    if (card == null || ReferenceEquals(card, _packCardSheenHost)) DetachPackCardSheen();
                    return;
                }
                if (card == null || !card.IsEffectivelyVisible || !MotionAllowed()) return;
                if (ReferenceEquals(card, _packCardSheenHost) && _packCardSheen != null) return;
                DetachPackCardSheen();
                _packCardSheen = CardSheenAdorner.Attach(card, 8);
                if (_packCardSheen == null) return;
                _packCardSheenHost = card;
                card.DetachedFromVisualTree += OnPackCardDetached;
                _packCardVisibility = EffectiveVisibility.Watch(card, () =>
                {
                    if (!card.IsEffectivelyVisible) DetachPackCardSheen();
                });
            }
            catch (Exception ex) { Log.Debug(ex, "Pack card hover failed"); DetachPackCardSheen(); }
        }

        private void OnPackCardDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            if (ReferenceEquals(sender, _packCardSheenHost)) DetachPackCardSheen();
        }

        private void DetachPackCardSheen()
        {
            _packCardVisibility?.Dispose();
            _packCardVisibility = null;
            if (_packCardSheenHost != null) _packCardSheenHost.DetachedFromVisualTree -= OnPackCardDetached;
            _packCardSheenHost = null;
            var sheen = _packCardSheen;
            _packCardSheen = null;
            CardSheenAdorner.Detach(sheen);
        }

    }
}
