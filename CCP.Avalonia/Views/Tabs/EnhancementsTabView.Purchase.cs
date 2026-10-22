using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Serilog;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// WPF MainWindow.Enhancements.cs SkillCard_Click (:1938): a purchasable node or secret card asks, buys through
    /// Core <see cref="SkillPurchase"/> (the server call), bursts on the bought node and, when the spend crossed a
    /// hundred, on the Prestige row (MainWindow.EventFx.cs:428 CelebrateEnhancementPurchase / :444 CelebratePrestige),
    /// then repaints. trophy_case re-runs UpdateTrophyCaseColumns; pink_rush arms the rush check (ApplySkillEffects).
    /// </summary>
    public partial class EnhancementsTabView
    {
        private const int EnhancementBurstCount = 100, PrestigeBurstCount = 150;   // WPF EventFx.cs

        /// <summary>Seams (tests): the confirm, the failure message and the purchase itself.</summary>
        internal static Func<Window, string, string, Task<bool>> Confirm = (owner, title, message) => MessageDialog.ConfirmAsync(owner, title, message);
        internal static Func<Window, string, string, Task> Tell = (owner, title, message) => MessageDialog.ShowAsync(owner, title, message);
        internal static Func<string, Task<(bool Success, string? Error)>> Purchase = id => SkillPurchase.PurchaseAsync(
            id, CoreSettings.Current, new V2AuthService(), App.Achievements, SyncBeforeRetryAsync, () => CoreSettings.Save());

        private readonly EventBurstLayer _burst = new();
        private readonly EventBurstLayer _prestigeBurst = new();
        private Control? _prestigeRow;
        private bool _buying;

        /// <summary>Test hooks: purchase bursts and prestige bursts fired.</summary>
        internal int PurchaseBursts => _burst.Count;
        internal int PrestigeBursts => _prestigeBurst.Count;
        /// <summary>The last click's purchase, so a test awaits its end (P56).</summary>
        internal Task? LastPurchase { get; private set; }

        /// <summary>WPF wires MouseLeftButtonUp + a hand cursor only on purchasable nodes; Enter/Space too (P17).</summary>
        private void MakePurchasable(Control target, string skillId)
        {
            target.Cursor = new Cursor(StandardCursorType.Hand);
            target.Focusable = true;
            target.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left) LastPurchase = BuyAsync(target, skillId); };
            target.KeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Enter or Key.Space)) return;
                e.Handled = true;
                LastPurchase = BuyAsync(target, skillId);
            };
        }

        internal async Task BuyAsync(Control node, string skillId)
        {
            var skill = SkillDefinition.All.FirstOrDefault(s => s.Id == skillId);
            if (skill == null || _buying || TopLevel.GetTopLevel(this) is not Window owner) return;
            var mods = App.Mods;
            var message = Loc.GetF("msg_purchase_skill",
                mods?.MakeModAware(skill.Name) ?? skill.LocalizedName,
                skill.Cost,
                (mods?.GetPointsLabel() ?? Loc.Get("label_sparkle_points")).ToLower(),
                mods?.MakeModAware(skill.FlavorText) ?? skill.LocalizedFlavorText,
                mods?.MakeModAware(skill.Description) ?? skill.LocalizedDescription)
                + "\n\n" + Loc.Get("msg_skill_permanent_note");
            if (!await Confirm(owner, Loc.Get("dialog_purchase_enhancement"), message)) return;

            // Disabled against a double click; the tree is not repainted under the node until the burst has
            // fired (WPF: the burst must run before RefreshEnhancementsUI detaches the anchor).
            _buying = true;
            node.IsEnabled = false;
            long prestigeBefore = PrestigeRankNow();
            try
            {
                var (success, error) = await Purchase(skillId);
                if (success)
                {
                    if (skillId == "trophy_case") (owner as Windows.MainShellWindow)?.UpdateTrophyCaseColumns();
                    if (skillId == "pink_rush") Windows.PinkRushHost.Start();
                    Log.Information("Skill purchased via UI: {SkillId}", skillId);
                    CelebratePurchase(node, prestigeBefore);
                }
                else if (!string.IsNullOrEmpty(error))
                {
                    await Tell(owner, Loc.Get("dialog_purchase_failed"), error);
                }
            }
            finally
            {
                node.IsEnabled = true;
                _buying = false;
                Repaint();
            }
        }

        /// <summary>WPF PrestigeRankNow: 1 + lifetime points spent / 100.</summary>
        private static long PrestigeRankNow() => 1 + (App.Achievements?.Progress?.LifetimeSkillPointsSpent ?? 0) / 100;

        /// <summary>CelebrateEnhancementPurchase + CelebratePrestige (burst half; EventBurstLayer refuses like
        /// WPF EventFxAllowed).</summary>
        private void CelebratePurchase(Control node, long rankBefore)
        {
            try
            {
                _burst.Fire(node, EnhancementBurstCount);
                if (PrestigeRankNow() > rankBefore && _prestigeRow != null
                    && _prestigeBurst.Fire(_prestigeRow, PrestigeBurstCount, color: Env.GlowColor))
                    SweepPrestigeSheen();   // fires only where the burst was allowed (WPF EventFxAllowed)
            }
            catch (Exception ex) { Log.Debug("CelebrateEnhancementPurchase: {E}", ex.Message); }
        }

        // CelebratePrestige's one full-window sheen pass (WPF MainWindow.xaml:3001 PrestigeSheen, ChromeFx SweepSheen):
        // a 260 px band skewed -18 deg in FxGlowColor travels the window on a sine ease in 1.15 s, opacity 0 -> 0.16
        // (20%) -> hold (75%) -> 0. One BeatLoop for the pass; it stops at the end and leaves the band parked at 0.
        private const double SheenSeconds = 1.15, SheenPeak = 0.16, SheenBand = 260;
        private Border? _sheen;
        private BeatLoop? _sheenLoop;
        private long _sheenStart;
        private double _sheenWidth;

        /// <summary>True while the sheen pass runs (test hook).</summary>
        internal bool SheenRunning => _sheenLoop?.IsRunning == true;

        private void SweepPrestigeSheen()
        {
            if (global::Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(this) is not { } host || host.Bounds.Width <= 0) return;
            var glow = Env.GlowColor;
            _sheen ??= new Border
            {
                Width = SheenBand, IsHitTestVisible = false, Opacity = 0,
                RenderTransform = new global::Avalonia.Media.SkewTransform(-18, 0),
            };
            _sheen.Background = new global::Avalonia.Media.LinearGradientBrush
            {
                StartPoint = new global::Avalonia.RelativePoint(0, 0, global::Avalonia.RelativeUnit.Relative),
                EndPoint = new global::Avalonia.RelativePoint(1, 0, global::Avalonia.RelativeUnit.Relative),
                GradientStops =
                {
                    new global::Avalonia.Media.GradientStop(global::Avalonia.Media.Color.FromArgb(0, 255, 255, 255), 0),
                    new global::Avalonia.Media.GradientStop(glow, 0.5),
                    new global::Avalonia.Media.GradientStop(global::Avalonia.Media.Color.FromArgb(0, 255, 255, 255), 1),
                },
            };
            if (_sheen.Parent != host)
            {
                (_sheen.Parent as Panel)?.Children.Remove(_sheen);
                host.Children.Add(_sheen);
            }
            _sheen.Height = host.Bounds.Height;
            Canvas.SetTop(_sheen, 0);
            _sheenWidth = host.Bounds.Width;
            _sheenStart = Time.GetTimestamp();
            _sheenLoop ??= new BeatLoop(this, _ => StepSheen());
            StepSheen();
            _sheenLoop.Start();
        }

        /// <summary>WPF ParkSheen: a hidden or detached tab drops the band to 0 and stops the pass (P01, P65).</summary>
        private void ParkSheen()
        {
            _sheenLoop?.Stop();
            if (_sheen != null) _sheen.Opacity = 0;
        }

        internal void StepSheen()
        {
            if (_sheen == null) return;
            double t = Time.GetElapsedTime(_sheenStart).TotalSeconds / SheenSeconds;
            if (t >= 1)
            {
                _sheen.Opacity = 0;
                _sheenLoop?.Stop();
                return;
            }
            double eased = (1 - Math.Cos(Math.PI * t)) / 2;   // SineEase EaseInOut
            Canvas.SetLeft(_sheen, -SheenBand + (_sheenWidth + SheenBand * 1.25) * eased);
            _sheen.Opacity = t < 0.2 ? SheenPeak * t / 0.2 : t < 0.75 ? SheenPeak : SheenPeak * (1 - t) / 0.25;
        }

        /// <summary>WPF ProfileSyncService.SyncBeforeRetryAsync over this head's SyncPush: one push, and inside the
        /// 30 s cooldown (which the server enforces too) wait out the rest of it once.</summary>
        private static async Task<bool> SyncBeforeRetryAsync()
        {
            if (Platform.AccountSeed.Sync is not { } sync) return false;
            if (await sync.PushAsync("purchase retry", waitForGate: true)) return true;
            var left = sync.LastSyncTime is { } last ? SyncPush.Cooldown - (sync.UtcNow() - last) : TimeSpan.Zero;
            if (left <= TimeSpan.Zero || left > SyncPush.Cooldown) return false;
            await Task.Delay(left + TimeSpan.FromMilliseconds(250));
            return await sync.PushAsync("purchase retry", waitForGate: true);
        }
    }
}
