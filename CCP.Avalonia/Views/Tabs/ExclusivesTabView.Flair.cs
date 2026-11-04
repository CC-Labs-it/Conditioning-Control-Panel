using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ExclusivesTabView
    {
        // WPF MainWindow.Exclusives.Flair.cs:242: delay the door only when a burst is visible.
        internal void OnVaultCardClicked(Control card, ExclusiveFeature feature, Action open)
        {
            var at = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), _ambientFx);
            if (!IsVisible || !AmbientFxCanvas.Env.AllowParticles || card.Bounds.Width <= 0 ||
                _ambientFx.Bounds.Width <= 1 || _ambientFx.Bounds.Height <= 1 || at == null)
            {
                open();
                return;
            }

            var color = PremiumShelfOrder.GroupOf(feature.Key, feature.Tier) switch
            {
                PremiumGroup.Basic => Color.FromRgb(0xFF, 0xC8, 0x5A),
                PremiumGroup.Prime => Color.FromRgb(0x6F, 0xE8, 0xFF),
                _ => Color.FromRgb(0xB7, 0x9C, 0xFF),
            };
            _ambientFx.Burst(at.Value.X, at.Value.Y, color, 90);
            DispatcherTimer.RunOnce(open, TimeSpan.FromMilliseconds(140));
        }
    }
}
