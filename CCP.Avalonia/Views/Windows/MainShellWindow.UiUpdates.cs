// PARTIALLY PORTED from ConditioningControlPanel/MainWindow/MainWindow.UiUpdates.cs (2904 lines).
// Sorted member by member against the fifteen Core seams. One member is real (plus the stat pills); the blanket claim
// was wrong for it and is right for most of the rest, with three exceptions worth naming.
//
// WHAT IS REAL: BtnAccountChip_Click. WPF's body is ShowTab("appsettings") +
// AppSettingsTab.FocusSection("account"), and BOTH halves already ship here as
// OpenAppSettingsSection (MainShellWindow.Settings.cs:65) - "account" is one of the nine keys in
// AppSettingsTabView.SectionKeys. It routes through that helper rather than repeating the pair,
// which also means it gets the helper's Named<T> lookup: the generated AppSettingsTab field on
// this window is permanently null (AvaloniaXamlLoader.Load), so AppSettingsTab?.FocusSection(...)
// transcribed from WPF would have compiled, rendered, reviewed clean and done nothing.
//
// NOT BLOCKED, MOVED - the settings toggles. SliderMaster_Changed, SliderVideoVolume_Changed,
// SliderDuck_Changed, ChkAudioDuck_Changed, ChkExcludeBambiCloudDucking_Changed,
// CmbAudioOutputDevice_SelectionChanged, BtnAudioOutputRefresh_Click, BtnTestAudio_Click,
// PopulateAudioOutputDevices, ChkPerformanceMode_Changed, ChkAutoPerformance_Changed,
// CmbMotionLevel_SelectionChanged, ChkVideoHwDecode_Changed, ChkUnifiedOverlay_Changed,
// ChkPanicOverridesAll_Changed, ChkNoPanic_Changed, ChkWinStart_Click, ChkStartHidden_Click,
// BtnPauseKey_Click, BtnPanicKey_Click, BtnSelectStartupVideo_Click, BtnClearStartupVideo_Click and
// ChkOfflineMode_Changed were all "MainWindow owns the handler, the section relays to it" on WPF.
// On this head each section owns its own handler and its own _isLoading guard:
// Views/Controls/AppSettings/{Audio,Performance,General,Devices,Data}SettingsSection.axaml.cs.
// Adding any of them back here would be a SECOND writer for one setting, not a restoration.
//
// PURE AND PORTABLE, LEFT OUT ANYWAY because they are orphans on this head:
//   ModAwareLabel(english, locKey) - CoreMods.MakeModAware answers it, so it compiles today. Its
//     consumer, StudioTabView, already carries its own inlined twin
//     (Views/Tabs/StudioTabView.axaml.cs:1105-1131) precisely because a UserControl cannot reach a
//     private on the shell. A second public copy with no caller is duplication, not progress.
//   StripLeadingGlyph - same story, same file, already inlined there.
//   FormatFileSize - its only callers are the assets-folder rows, which are not ported.
//
// STILL OUT, by blocker:
//   (UpdateLevelDisplay's header half is live in MainShellWindow.HeroFx.cs.)
//   (UpdateStatPills + StartStatPillUpdateTimer, startup leaderboard read, mic backstop and XP bonus chips
//    are live below; the Quests view applies the shared journal mod policy on attach and mod change.)
//   App.Progression / the XP bar - UpdateXPBarLoginState,
//     XPBarTrack_ToolTipOpening,
//     RefreshAccountChip and the three AccountChip* brushes. CoreProgression is a seam, but the
//     chip and the pills read App.Account / App.Patreon tier state on top of it, and a chip that
//     paints the logged-out tier unconditionally is the same failure recorded for
//     UpdateSubscribeStarUI in MainShellWindow.SubscribeStar.cs.
//   (StartConditioningTimeTracker / StopConditioningTimeTracker are live below, on Core's ConditioningTimeTracker.)
//   SyncConditioningTimeToServerAsync - the 15-minute and on-stop ProfileSync push. Core's SyncPush does not
//     send total_conditioning_minutes yet, so there is nothing for the timer to call.
//   UpdateUI / QueueModAwareSurfaceSweep / RefreshModAwareSurfaces / SweepStep /
//     EnsureModSweepWatchers / ApplyModFeatureNames - the sweep
//     walks named controls across every tab, most of which are unported; it is a pass over the
//     finished UI, so it lands last, not first.
//   UpdateUnlockablesVisibility / SetFeatureImageBlur - App.Unlockables plus a WPF BlurEffect.
//   (The Intake Pass tile is ported: MainShellWindow.IntakePassTile.cs.)
//   ImgLogo_MouseLeftButtonDown / ShowEasterEgg / TriggerStartupVideo - a media window each.
//   BtnManageAttention_Click, BtnAttentionStyle_Click, BtnSubliminalSettings_Click,
//     BtnManageMessages_Click, BtnPrevImage_Click, BtnNextImage_Click,
//     BtnRefreshAssets_Click - each opens an unported window or drives the unported asset tree.
//   SetOfflineDisabled / UpdateOfflineModeUI / DisconnectNetworkServices - the toggle itself moved
//     (DataSettingsSection.axaml.cs:67, which persists OfflineMode and asks for the offline name),
//     but the TEARDOWN did not: App.Account, App.RemoteControl and the update checker are the
//     things a disconnect disconnects, and none of them exists here to disconnect. The greying
//     pass, SetOfflineDisabled, walks named controls on unported tabs.
//   BtnPickAssetsFolder_Click / CopyDirectoryRecursive - the picker itself already ships as
//     MainShellWindow.Settings.cs RequestPickAssetsFolder, called from AssetsTabView (Library > Folders). What
//     is missing here is only the WPF handler shell around it and the copy-the-old-library
//     migration, which needs the assets tree to say what it copied.

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
        /// <summary>
        /// The header's account chip. Signed out, opens the unified sign-in dialog. Signed in,
        /// opens the Settings door and scrolls to Account, as WPF. The scroll deliberately
        /// happens after the tab is shown, because a section can only be measured once visible;
        /// OpenAppSettingsSection keeps that order and swallows a failed scroll.
        /// </summary>
        private async void BtnAccountChip_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!CoreAccount.IsLoggedIn) { await OpenUnifiedLoginDialog(); return; }
            OpenAppSettingsSection("account");
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
            Opened += (_, _) => StartupPillFetch = StartupPillFetchAsync();
            _statPillTimer = new global::Avalonia.Threading.DispatcherTimer { Interval = System.TimeSpan.FromSeconds(30) };
            _statPillTimer.Tick += (_, _) => UpdateStatPills();
            _statPillTimer.Start();
            // The tracker too: App's Closed handler drops the engine-stopped hook before it stops the engine.
            Closed += (_, _) => { _statPillsClosed = true; _statPillTimer?.Stop(); StopConditioningTimeTracker(); };
        }

        // ---- conditioning-time tracker (WPF MainWindow.UiUpdates.cs:746-842) ----
        internal ConditioningControlPanel.Services.ConditioningTimeTracker ConditioningTime = new();   // tests swap the clock
        private global::Avalonia.Threading.DispatcherTimer? _conditioningTimeTimer;
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
            try
            {
                ConditioningTime.Stop(CoreSettings.Current);
                UpdateStatPills();
            }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "Error finalizing conditioning time"); }
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
