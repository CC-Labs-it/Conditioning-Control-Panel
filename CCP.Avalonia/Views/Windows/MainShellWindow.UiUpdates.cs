using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private void BtnAccountChip_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!CoreAccount.IsLoggedIn) { _ = OpenUnifiedLoginDialog(); return; }
            OpenAppSettingsSection("account");
        }

        internal void UpdateXPBarLoginState()
        {
            var loggedIn = CoreAccount.IsLoggedIn;
            if (Named<Border>("XPBarLoginOverlay") is { } overlay) overlay.IsVisible = !loggedIn;
            if (Named<Grid>("XPBarContent") is { } content) content.Opacity = loggedIn ? 1 : 0.3;
            if (Named<Button>("BtnAccountChip") is not { } button
                || Named<TextBlock>("TxtAccountChipName") is not { } name
                || Named<global::ConditioningControlPanel.Avalonia.Controls.IconGlyph>("TxtAccountChipBadge") is not { } badge) return;
            var displayName = CoreAccount.DisplayName;
            name.Text = !loggedIn ? Loc.Get("account_chip_sign_in") : string.IsNullOrWhiteSpace(displayName)
                ? Loc.Get("account_chip_signed_in") : displayName;
            var tier = !loggedIn ? 0 : CoreAccount.HasLabAccess ? 2 : CoreAccount.HasPremiumAccess ? 1 : 0;
            badge.IsVisible = tier > 0;
            badge.Kind = tier == 2 ? IconKind.Beaker : IconKind.LockClosed;
            // WPF's coloured emoji: the tier's semantic icon brush, not the chip's inherited foreground.
            badge.Bind(global::ConditioningControlPanel.Avalonia.Controls.IconGlyph.ForegroundProperty,
                badge.GetResourceObservable(tier == 2 ? global::ConditioningControlPanel.Avalonia.Controls.IconBrushes.Tier2
                    : global::ConditioningControlPanel.Avalonia.Controls.IconBrushes.Tier1));
            // Keep the signed-out CTA bound to the mod accent, as WPF SetResourceReference does.
            button.Bind(Button.BorderBrushProperty, new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension(
                !loggedIn ? "PinkBrush" : tier == 2 ? "AccountChipTier2Brush" : tier == 1 ? "AccountChipTier1Brush" : "AccountChipNeutralBrush"));
        }

        // ---- header stat pills (WPF MainWindow.UiUpdates.cs:530-641 UpdateStatPills, :717 timer) ----
        private global::Avalonia.Threading.DispatcherTimer? _statPillTimer;
        private static int _lbOnline, _lbPercentile, _lbEntries;

        /// <summary>The last leaderboard page the pills read (WPF App.Leaderboard OnlineUsers/YourRank/
        /// YourTotal/Entries, LeaderboardService.cs:106-108). LeaderboardTabView calls it when a page lands
        /// and repaints its host, as WPF's post-refresh UpdateStatPills does (MainWindow.xaml.cs:3721).</summary>
        internal static void NoteLeaderboardPage<T>(LeaderboardPage<T> page) where T : LeaderboardEntryData
        {
            _lbOnline = page.OnlineUsers;
            _lbEntries = page.Entries?.Count ?? 0;
            _lbPercentile = LeaderboardClient.PlayerPercentile(page.YourRank, page.YourTotal,
                (System.Collections.Generic.IReadOnlyList<T>?)page.Entries ?? Array.Empty<T>(), page.TotalUsers,
                CoreAccount.UnifiedUserId, Platform.AccountSeed.Discord?.UserId, CoreAccount.DisplayName);
        }

        internal async Task RefreshStatPillLeaderboardAsync()
        {
            if (CoreSettings.Current.OfflineMode || !CoreAccount.IsLoggedIn) return;
            try
            {
                var (page, _) = await Tabs.LeaderboardTabView.NewClient().FetchAsync<LeaderboardEntryData>(
                    "all-time", CoreAccount.UnifiedUserId, DateTime.UtcNow);
                if (page != null && !_statPillsClosed) { NoteLeaderboardPage(page); UpdateStatPills(); }
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Initial stat pill leaderboard fetch failed"); }
        }

        private bool _statPillsClosed;

        /// <summary>The signed-in startup fetch started on Opened; tests await it and zero the delay.</summary>
        internal Task StartupPillFetch { get; private set; } = Task.CompletedTask;
        internal static TimeSpan StartupPillFetchDelay = TimeSpan.FromMilliseconds(500);

        private async Task StartupPillFetchAsync()
        {
            if (!CoreAccount.IsLoggedIn || CoreSettings.Current.OfflineMode) return;
            await Task.Delay(StartupPillFetchDelay);
            if (!_statPillsClosed) await RefreshStatPillLeaderboardAsync();
        }

        /// <summary>WPF StartStatPillUpdateTimer (MainWindow.xaml.cs:3726): paint now, then every 30 s.</summary>
        private void InitializeStatPills()
        {
            UpdateStatPills();
            // WPF MainWindow.xaml.cs:3716: signed-in startup fetch, after the window has loaded.
            // WPF runs both once from Loaded (MainWindow.xaml.cs:3716/3794). Opened fires again on every
            // tray restore (Hide/Show), so the handler removes itself.
            void OnFirstOpened(object? _, EventArgs __)
            {
                Opened -= OnFirstOpened;
                StartupPillFetch = StartupPillFetchAsync();
                StartupVideo = PlayStartupVideoAsync();
            }
            Opened += OnFirstOpened;
            _statPillTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = System.TimeSpan.FromSeconds(30) };
            _statPillTimer.Tick += (_, _) => UpdateStatPills();
            _statPillTimer.Start();
            // The tracker too: App's Closed handler drops the engine-stopped hook before it stops the engine.
            Closed += (_, _) => { _statPillsClosed = true; _statPillTimer?.Stop(); StopConditioningTimeTracker(); };
        }

        internal Task StartupVideo { get; private set; } = Task.CompletedTask;
        internal static TimeSpan StartupVideoDelay = TimeSpan.FromMilliseconds(200);
        private async Task PlayStartupVideoAsync()
        {
            if (!CoreSettings.Current.ForceVideoOnLaunch) return;
            await Task.Delay(StartupVideoDelay);
            if (!_statPillsClosed) CoreEngine.Video?.TriggerStartup();
        }

        // ---- conditioning-time tracker (WPF MainWindow.UiUpdates.cs:746-842) ----
        internal ConditioningControlPanel.Services.ConditioningTimeTracker ConditioningTime = new();   // tests swap the clock
        private global::Avalonia.Threading.DispatcherTimer? _conditioningTimeTimer;
        private global::Avalonia.Threading.DispatcherTimer? _conditioningTimeSyncTimer;
        internal TimeSpan? ConditioningSyncInterval => _conditioningTimeSyncTimer?.Interval;
        internal Task LastConditioningSync { get; private set; } = Task.CompletedTask;
        internal Func<Task> PushConditioningProfile = async () =>
        {
            if (Platform.AccountSeed.Sync is { } sync) await sync.PushAsync("conditioning-time");
        };

        internal async Task SyncConditioningTimeToServerAsync()
        {
            if (CoreSettings.Current.OfflineMode || !CoreAccount.IsLoggedIn) return;
            try { await PushConditioningProfile(); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to sync conditioning time"); }
        }

        internal bool ConditioningTimerRunning => _conditioningTimeTimer?.IsEnabled == true;

        /// <summary>WPF StartConditioningTimeTracker: called where WPF's StartEngine is (engine start, session
        /// start). A 1 s tick repaints the pill live and writes a minute every 60th tick.</summary>
        internal void StartConditioningTimeTracker()
        {
            if (_conditioningTimeTimer != null) return;   // already started
            ConditioningTime.Start(CoreSettings.Current);
            _conditioningTimeTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = System.TimeSpan.FromSeconds(1) };
            _conditioningTimeTimer.Tick += (_, _) => ConditioningTimeTick();
            _conditioningTimeTimer.Start();
            _conditioningTimeSyncTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
            _conditioningTimeSyncTimer.Tick += (_, _) => LastConditioningSync = SyncConditioningTimeToServerAsync();
            _conditioningTimeSyncTimer.Start();
        }

        internal void ConditioningTimeTick()
        {
            try
            {
                UpdateStatPills();
                ConditioningTime.Tick(CoreSettings.Current);
            }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "Error tracking conditioning time"); }
        }

        /// <summary>WPF StopConditioningTimeTracker: every engine stop (button, panic, tray, shell close) adds
        /// the partial minute the ticks have not written yet.</summary>
        internal void StopConditioningTimeTracker()
        {
            if (_conditioningTimeTimer == null) return;
            _conditioningTimeTimer.Stop();
            _conditioningTimeTimer = null;
            _conditioningTimeSyncTimer?.Stop();
            _conditioningTimeSyncTimer = null;
            try
            {
                ConditioningTime.Stop(CoreSettings.Current);
                UpdateStatPills();
            }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "Error finalizing conditioning time"); }
            LastConditioningSync = SyncConditioningTimeToServerAsync();
        }

        private void RefreshXPBarBonuses()
        {
            if (Named<StackPanel>("XPBarBonusList") is not { } list) return;
            list.Children.Clear();
            // ponytail: this head has no armed LiveEventService; use the same zero event boost as EnhancementsTabView.
            foreach (var (source, value) in SkillTreeRules.GetMultiplierBreakdown(CoreSettings.Current, DateTime.Now.Hour, 0.0).Skip(1))
            {
                var chip = new Border
                {
                    Background = SolidColorBrush.Parse("#2A2A4A"), CornerRadius = new global::Avalonia.CornerRadius(10),
                    Padding = new global::Avalonia.Thickness(6, 3, 6, 3), Margin = new global::Avalonia.Thickness(0, 0, 8, 0),
                    Child = new TextBlock
                    {
                        Text = $"+{value:P0} {CoreMods.MakeModAware(source)}", FontSize = 10,
                        Foreground = SolidColorBrush.Parse(App.Mods?.GetAccentLightColorHex() ?? "#FFB6C1"),
                        VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center
                    }
                };
                ToolTip.SetTip(chip, SkillTreeRules.GetBonusChipTooltip(source, id => App.Mods?.GetBoostTooltip(id)));
                list.Children.Add(chip);
            }
        }

        /// <summary>WPF UpdateStatPills: each pill shows only with the skill that buys it. The conditioning
        /// figure is live while the tracker runs (baseline + elapsed). Also repairs the mic pill and
        /// rebuilds the active XP chips on each refresh, including the 30 s backstop.</summary>
        internal void UpdateStatPills()
        {
            UpdateMicPill(); // Privacy backstop on the same 30 s timer as WPF.
            RefreshXPBarBonuses();
            var s = CoreSettings.Current;
            bool Has(string skill) => ConditioningControlPanel.Models.SkillTreeRules.HasSkill(s, skill);
            if (Named<Border>("PillConditioningTime") is { } time && (time.IsVisible = Has("pink_hours"))
                && Named<TextBlock>("TxtPillConditioningTime") is { } tt)
            {
                double secs = ConditioningTime.LiveMinutes(s) * 60;
                tt.Text = $"{(int)(secs / 3600)}h {(int)(secs % 3600 / 60)}m {(int)(secs % 60)}s";
            }
            if (Named<Border>("PillOnlineUsers") is { } online && (online.IsVisible = Has("hive_mind"))
                && Named<TextBlock>("TxtPillOnlineUsers") is { } ot)
                ot.Text = _lbOnline.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (Named<Border>("PillRankPercentile") is { } rank && (rank.IsVisible = Has("popular_girl"))
                && Named<TextBlock>("TxtPillRankPercentile") is { } rt)
            {
                // Shared WPF percentile rule: server rank or the fetched-board identity fallback.
                int pct = _lbPercentile;
                rt.Text = pct > 0 ? $"Top {pct}%" : Loc.Get(_lbEntries > 0 ? "label_unranked" : "label_loading_2");
            }
            if (Named<Border>("StreakFirePill") is { } fire && (fire.IsVisible = Has("good_girl_streak")))
            {
                if (Named<TextBlock>("TxtStreakFireCount") is { } count)
                    count.Text = (global::ConditioningControlPanel.Avalonia.App.Achievements?.Progress?.ConsecutiveDays ?? 0).ToString();
                if (Named<Control>("TxtStreakShieldIcon") is { } shield) shield.IsVisible = s.StreakShieldsRemaining > 0;
            }
        }
    }
}
