// PORTED-IN-PART from ConditioningControlPanel/MainWindow/MainWindow.Exclusives.cs (1296 lines).
//
// Restored in ExclusivesTabView (rows-exclusives): BuildComingSoonCard, ApplyVeilLockBreath, ApplyFreeTodayPulse,
// RefreshExclusiveTierPlates, Start/StopExclusivesMotion, Restart/AttachExclusiveSheens, VaultLivery.Apply.
// wire-shell-exclusives adds mod-aware accent edges and badge/pill gradients in that same view;
// the HSV hue rotation is shared with WPF through Core Services/UI/VaultColor.cs.
// ponytail: stub except RefreshExclusivesTab (the roster/gate repaint now lives in ExclusivesTabView). Every member below reaches App.*, a service, a device, a
// WebView2 or Win32 - none of which this head may touch (see the layer rules: "Do not
// move services"). The file exists and each member is NAMED so nothing disappears
// silently; the bodies come back when the services move to Core.
//
// Members dropped (41; the accent retint - VaultPartner, ShiftHue, RetintVaultChrome, TintShadow/TintGradient - is in ExclusivesTabView + Core VaultColor):
//   private sealed class ExclusiveCardUi
//   private readonly List<ExclusiveCardUi> _exclusiveCards
//   private readonly List<TextBlock> _exclusiveTeaserMarks
//   private readonly List<Border> _exclusiveTeaserCards
//   private readonly List<(…)
//   private readonly List<DropShadowEffect> _exclusiveAccentShadows
//   private bool _exclusivesBuilt
//   private bool _exclusivesSheenRetryQueued
//   private Storyboard? _spotFreeFx
//   private static readonly FontFamily FredokaFont
//   private static readonly Color FreeTodayGold
//   private static SolidColorBrush Freeze(…)
//   private static Color VaultAccent(…)
//   private const byte TeaserMarkAlpha
//   private static SolidColorBrush ExclusiveEdgeDefault(…)
//   private static SolidColorBrush SpotlightEdgeDefault(…)
//   private LinearGradientBrush AccentGradient(…)
//   private DropShadowEffect AccentTitleShadow(…)
//   private static bool IsExclusiveFreeToday(…)
//   internal void OpenExclusiveSpotlight(…)
//   private void EnsureExclusivesBuilt(…)
//   private void ApplySpotlightArt(…)
//   private Border BuildExclusiveCard(…)
//   private Border BuildComingSoonCard(…)
//   private static string TeaserEmoji(…)
//   private static void OnExclusiveCardHover(…)
//   internal void RefreshExclusivesTab(…)
//   private void ApplyExclusiveCardState(…)
//   private static void ApplyVeilLockBreath(…)
//   private static Storyboard? ApplyFreeTodayPulse(…)
//   private void RefreshExclusiveTierPlates(…)
//   private void StartExclusivesMotion(…)
//   private void StopExclusivesMotion(…)
//   private int _exclusivesSheenRetries
//   private void RestartExclusiveSheens(…)
//   private void AttachExclusiveSheens(…)
//   private static string ExclusiveTitle(…)
//   private static string ExclusiveArtPath(…)
//   private const int ExclusiveCardDecodeWidth
//   private const int ExclusiveHeroDecodeWidth
//   private static ImageSource? LoadPackImage(…)

using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.

        /// <summary>WPF RefreshExclusivesTab, for the entitlement events (tier change, day rollover).
        /// The body lives in ExclusivesTabView.RefreshVault.</summary>
        internal void RefreshExclusivesTab()
        {
            Named<Tabs.ExclusivesTabView>("ExclusivesTab")?.RefreshVault();
            AppSettingsPage?.SectionAccount.PlansView.RefreshVault();   // WPF MainWindow/MainWindow.Exclusives.cs:960 repaints every built view, the Plans copy too (and its throttled invites read)
        }

        /// <summary>WPF OpenExclusiveFeature (main 2e9080399): each card opens the door the launcher or the Play
        /// wall uses, so the card never decides access; the door's own gate refuses. Doors with no host on this
        /// head never get here (ExclusivesTabView.IsOnThisBuild).</summary>
        internal void OpenExclusiveFeature(string key)
        {
            if (LockdownActive) return;   // PLAYBOOK P05, like OpenInvitesCard
            switch (key)
            {
                case "gazeminigame": Named<Tabs.PlayTabView>("PlayTab")?.OpenGazeMinigame(); break;
                case "breakout": LaunchPlayBreakout(); break;                          // WPF :205, the Play wall's door
                case "dtrh": LaunchPlayLauncherGame(Games.GameWindow.DtrhId); break;   // WPF :208 LaunchExclusiveGame
                case "focusgaze": OpenFocusGazeSwitch(); break;                       // WPF :242
                default: ShowTab(key); break;
            }
        }

        /// <summary>WPF OpenFocusGazeSwitch (MainWindow.Exclusives.cs:257): Focus Gaze is a switch on the Play
        /// wall, not a window, so go there and show it. The switch's own handler asks the tier gate;
        /// navigating never turns it on.</summary>
        private void OpenFocusGazeSwitch()
        {
            ShowTab("play");
            var slot = Named<Tabs.PlayTabView>("PlayTab")?.FindControl<Control>("SlotFocusGaze");
            if (slot == null) return;
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                try { slot.BringIntoView(); }
                catch (System.Exception ex) { Serilog.Log.Debug("Focus Gaze bring-into-view: {E}", ex.Message); }
            }, global::Avalonia.Threading.DispatcherPriority.Background);   // after the Play tab's first layout
        }
    }
}
