using System;
using System.Windows;
using System.Windows.Input;
using ConditioningControlPanel.Services.Super;

namespace ConditioningControlPanel.Controls
{
    /// <summary>
    /// How a gold box's option rows show (owner rule: what Basic buys stays visible, locked).
    /// V2 lit: the rows are live. Free account: the rows show, faded, and take no input, so the
    /// box says what the tier buys. Unlocked but switched off: the rows fold away.
    /// </summary>
    public static class SuperRows
    {
        public const double LockedOpacity = 0.45;

        public static void Apply(FrameworkElement rows, SuperEffect effect)
        {
            bool lit = false, unlocked = true;
            try
            {
                lit = SuperAccess.IsSelected(effect);
                unlocked = SuperAccess.IsUnlocked(effect);
            }
            catch (Exception ex) { App.Logger?.Debug("SuperRows: {E}", ex.Message); }

            rows.Visibility = lit || !unlocked ? Visibility.Visible : Visibility.Collapsed;
            rows.Opacity = lit ? 1 : LockedOpacity;
            rows.IsHitTestVisible = lit;
            KeyboardNavigation.SetTabNavigation(rows, lit ? KeyboardNavigationMode.Continue : KeyboardNavigationMode.None);
        }
    }
}
