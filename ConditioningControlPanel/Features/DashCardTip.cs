using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Features
{
    /// <summary>
    /// The hover nudge on the dashboard's feature tiles (owner, 2026-11-15): which click opens the
    /// settings and which one switches the feature. It reads the "Invert clicks" setting when it
    /// opens, so it always says what the next click will do.
    /// </summary>
    internal static class DashCardTip
    {
        /// <param name="card">The tile.</param>
        /// <param name="applies">False = no nudge (a tile with no on/off of its own).</param>
        /// <param name="quiet">Children with their own hover card (the "?" buttons, the V2 corner).</param>
        public static void Attach(FrameworkElement card, Func<bool> applies, Func<UIElement?[]> quiet)
        {
            card.ToolTip = "";
            ToolTipService.SetInitialShowDelay(card, 900);
            ToolTipService.SetPlacement(card, System.Windows.Controls.Primitives.PlacementMode.Mouse);
            card.ToolTipOpening += (_, e) =>
            {
                if (!applies() || OverAny(quiet())) { e.Handled = true; return; }
                card.ToolTip = Loc.Get(Inverted ? "dash_card_tip_inverted" : "dash_card_tip");
            };
        }

        public static bool Inverted => App.Settings?.Current?.DashboardInvertClicks == true;

        private static bool OverAny(UIElement?[] elements)
        {
            if (Mouse.DirectlyOver is not DependencyObject node) return false;
            while (node != null)
            {
                foreach (var e in elements) if (e != null && ReferenceEquals(e, node)) return true;
                node = node is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(node)
                    : LogicalTreeHelper.GetParent(node);
            }
            return false;
        }
    }
}
