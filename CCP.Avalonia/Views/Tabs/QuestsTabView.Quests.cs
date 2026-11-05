using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The quest half of the tab, painted off the Core <see cref="QuestService"/> (App.Quests).
    /// PORTED from MainWindow.QuestsTab.cs (RefreshQuestUI, BuildDailyCardModel,
    /// ComputeQuestXpDisplay) and MainWindow.Quests.cs (OnQuestCompleted / OnQuestProgressChanged),
    /// plus WPF's QuestsRefreshed subscription (MainWindow.xaml.cs:452-454). Lives in the view, not
    /// the shell partials: every control it paints is this view's own x:Name field.
    /// </summary>
    public partial class QuestsTabView
    {
        private static readonly IBrush Gold = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
        private static readonly IBrush Grey = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x60));

        private Windows.QuestCompletePopup? _questCompletePopup;
        private double _weeklyFraction;
        private IDisposable? _bannerTimer;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            UpdateAmbient(attached: true);
            CoreMods.ModChanged += OnJournalModChanged;
            ApplyBimboJournalModVisibility();
            if (App.Quests is not { } quests) return;
            WatchStreakFixCharges(attached: true);
            quests.QuestCompleted += OnQuestCompleted;
            quests.QuestProgressChanged += OnQuestProgressChanged;
            quests.QuestsRefreshed += OnQuestsRefreshed;
            WeeklyProgressTrack.SizeChanged += OnWeeklyTrackSizeChanged;
            RefreshQuestUI();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            UpdateAmbient(attached: false);
            CoreMods.ModChanged -= OnJournalModChanged;
            WatchStreakFixCharges(attached: false);
            if (App.Quests is not { } quests) return;
            quests.QuestCompleted -= OnQuestCompleted;
            quests.QuestProgressChanged -= OnQuestProgressChanged;
            quests.QuestsRefreshed -= OnQuestsRefreshed;
            WeeklyProgressTrack.SizeChanged -= OnWeeklyTrackSizeChanged;
        }

        private void OnWeeklyTrackSizeChanged(object? sender, SizeChangedEventArgs e) => ApplyWeeklyBar(animate: false);

        // ShowTab calls RefreshQuestUI on every entry in WPF; IsVisible flipping true is that entry.
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property != IsVisibleProperty) return;
            UpdateAmbient(TopLevel.GetTopLevel(this) != null);
            if (IsVisible && App.Quests != null) RefreshQuestUI();
        }

        private void OnQuestsRefreshed(object? sender, EventArgs e) => Dispatcher.UIThread.Post(RefreshQuestUI);

        // WPF refreshes only while the tab is on screen.
        private void OnQuestProgressChanged(object? sender, QuestProgressEventArgs e) =>
            Dispatcher.UIThread.Post(() => { if (IsVisible) RefreshQuestUI(); });

        /// <summary>WPF MainWindow.Quests.cs:40-62: announcements, banner and sync.</summary>
        private void OnQuestCompleted(object? sender, QuestCompletedEventArgs e) => Dispatcher.UIThread.Post(() =>
        {
            // Perk-announcement opt-out: the popup goes, the in-tab banner stays (WPF :47).
            bool announce = !CoreSettings.Current.SuppressPerkNotifications;
            if (announce) Services.Flash.FlashCelebrationVoice.Play(CoreSettings.Current);
            try { _questCompletePopup?.Close(); } catch { }
            _questCompletePopup = null;
            if (announce)
            {
                _questCompletePopup = new Windows.QuestCompletePopup(e.QuestDefinition.Name, e.XPAwarded);
                _questCompletePopup.Show();
            }

            QuestCompleteBanner.IsVisible = true;
            TxtQuestComplete.Text = $"{e.QuestDefinition.Name} COMPLETE! +{e.XPAwarded} XP";
            RefreshQuestUI();
            CelebrateQuestComplete(e.QuestType, e.QuestDefinition.Id);

            _bannerTimer?.Dispose();
            _bannerTimer = DispatcherTimer.RunOnce(() => QuestCompleteBanner.IsVisible = false, TimeSpan.FromSeconds(5));
            Serilog.Log.Information("Quest completed: {Name} (+{XP} XP)", e.QuestDefinition.Name, e.XPAwarded);

            // Sync quest streak data to the server (WPF: ProfileSync.SyncProfileAsync when enabled).
            if (Platform.AccountSeed.Sync is { Loaded: true } sync) _ = sync.PushAsync("quest-complete");
        });

        /// <summary>MainWindow.QuestsTab.cs:115; the XP is QuestService.ScaledQuestXp, what CompleteQuest pays.</summary>
        internal static (int Xp, int StreakPct, int RerollPct) ComputeQuestXpDisplay(QuestDefinition def, AppSettings s)
        {
            var rerollMult = SkillTreeRules.GetRerollBonusMultiplier(s);
            // The streak this completion pays at (the day's first daily advances it first).
            var streak = App.Quests?.StreakPaidOn(def.Type, s) ?? s.DailyQuestStreak;
            var xp = QuestService.ScaledQuestXp(def.XPReward, s, streak);
            // WPF "+N%🔥 +M%🔃": the glyphs are drawn by DailyQuestCard.SetBonus.
            return (xp, streak > 0 ? streak * 3 : 0, rerollMult > 1.0 ? (int)((rerollMult - 1.0) * 100) : 0);
        }

        // Server-rolled quests have no loc key, so the lookup returns the raw key (WPF :150).
        private static string Localized(string localized, string fallback) =>
            QuestStampRules.Text(localized, fallback);

        private static DailyQuestCardModel BuildDailyCardModel(int slot, ActiveQuest quest, QuestDefinition def, int rerollsLeft, AppSettings s)
        {
            var (xp, streakPct, rerollPct) = ComputeQuestXpDisplay(def, s);
            bool canReroll = !quest.IsCompleted && rerollsLeft > 0;
            return new DailyQuestCardModel
            {
                Slot = slot,
                Icon = def.Icon,
                Name = CoreMods.MakeModAware(Localized(def.LocalizedName, def.Name)),
                Description = CoreMods.MakeModAware(Localized(def.LocalizedDescription, def.Description)),
                Current = quest.CurrentProgress,
                Target = def.TargetValue,
                IsCompleted = quest.IsCompleted,
                XpText = $"{xp} XP",
                StreakBonusPct = streakPct,
                RerollBonusPct = rerollPct,
                Art = GetQuestArt(def),
                CanReroll = canReroll,
                RerollKey = canReroll ? "btn_reroll_with_count" : "btn_reroll_none",
                RerollCount = rerollsLeft,
                RerollTooltip = canReroll ? Loc.Get("quest_card_reroll_tip") : Loc.Get("quest_card_reroll_tip_none"),
            };
        }

        /// <summary>MainWindow.QuestsTab.cs:188 RefreshQuestUI. The punch card stays collapsed, as
        /// WPF's does while IntakePunchCardService.UiEnabled is false (its prize is TBD).</summary>
        internal void RefreshQuestUI()
        {
            var quests = App.Quests;
            if (quests == null) return;
            var s = CoreSettings.Current;
            quests.RecalculateStreak();

            // The server's season title, else the local month name (QuestDefinitionService.SeasonTitle).
            var seasonTitle = App.QuestDefinitions?.SeasonTitle;
            if (!string.IsNullOrEmpty(seasonTitle)) TxtSeasonTitle.Text = seasonTitle;

            int dailyCompleted = quests.GetDailyQuestsCompletedToday();
            TxtDailyQuestCounter.Text = $"{dailyCompleted}/{QuestService.MaxDailyQuestsPerDay}";
            bool allDailyDone = quests.AreAllDailyQuestsCompleted();
            DailySegment1.Background = dailyCompleted >= 1 ? Gold : Grey;
            DailySegment2.Background = dailyCompleted >= 2 ? Gold : Grey;
            DailySegment3.Background = dailyCompleted >= 3 ? Gold : Grey;
            DailyAllCompletedMessage.IsVisible = allDailyDone;

            var board = quests.GetDailySlots();
            int rerollsLeft = quests.GetRemainingDailyRerolls();
            for (int i = 0; i < _dailyCards.Length; i++)
            {
                var quest = i < board.Count ? board[i].Quest : null;
                var def = i < board.Count ? board[i].Definition : null;
                if (quest == null || def == null)
                    _dailyCards[i].ShowEmpty(i, Loc.Get("quest_card_empty_title"), Loc.Get("quest_card_empty_body"));
                else
                    _dailyCards[i].Apply(BuildDailyCardModel(i, quest, def, rerollsLeft, s));
            }

            TxtDailyRerollPool.IsVisible = !allDailyDone;
            TxtDailyRerollPool.Args = rerollsLeft > 1 ? new object[] { rerollsLeft } : null;
            TxtDailyRerollPool.Key = rerollsLeft > 1 ? "quest_daily_pool_many"
                : rerollsLeft == 1 ? "quest_daily_pool_one" : "quest_daily_pool_none";

            var weeklyDef = quests.GetCurrentWeeklyDefinition();
            var weekly = quests.Progress.WeeklyQuest;
            var rerollText = TxtWeeklyReroll;
            if (weeklyDef != null && weekly != null)
            {
                ImgWeeklyQuest.Source = GetQuestArt(weeklyDef);
                TxtWeeklyQuestIcon.Emoji = weeklyDef.Icon;   // collapsed, as in WPF
                if (GetQuestArt(weeklyDef) is { } weeklyArt) ImgWeeklyQuest.Source = weeklyArt;
                TxtWeeklyQuestName.Text = CoreMods.MakeModAware(weeklyDef.Name);
                TxtWeeklyQuestDesc.Text = CoreMods.MakeModAware(weeklyDef.Description);
                TxtWeeklyProgress.Text = $"{weekly.CurrentProgress} / {weeklyDef.TargetValue}";
                var (xp, _, _) = ComputeQuestXpDisplay(weeklyDef, s);
                TxtWeeklyXP.Text = $"{xp} XP";
                WeeklyXpIcon.IsVisible = true;
                var rerollMult = SkillTreeRules.GetRerollBonusMultiplier(s);
                TxtWeeklyStreakBonus.IsVisible = DailyQuestCard.SetBonus(TxtWeeklyStreakBonus, s.DailyQuestStreak * 3, 0, "(", ")");
                TxtWeeklyRerollBonus.IsVisible = DailyQuestCard.SetBonus(TxtWeeklyRerollBonus, 0, rerollMult > 1.0 ? (int)((rerollMult - 1.0) * 100) : 0, "(", ")");
                _weeklyFraction = weeklyDef.TargetValue > 0
                    ? Math.Min(1.0, (double)weekly.CurrentProgress / weeklyDef.TargetValue) : 0;
                ApplyWeeklyBar();

                WeeklyCompletedOverlay.IsVisible = weekly.IsCompleted;
                int weeklyRerolls = quests.GetRemainingWeeklyRerolls();
                BtnRerollWeekly.IsEnabled = !weekly.IsCompleted && weeklyRerolls > 0;
                // btn_completed carries its own icon; the English reroll strings get ArrowSync (WPF "🔄 ...").
                rerollText.Kind = weekly.IsCompleted ? null : IconKind.ArrowSync;
                rerollText.Text = weekly.IsCompleted ? null
                    : weeklyRerolls > 0 ? $"Reroll ({weeklyRerolls} left)" : "No rerolls left";
                rerollText.Key = "btn_completed";
            }
            else if (weekly is { IsCompleted: true })
            {
                // Rotated-away definition of a finished weekly (#496): a "done for the week" card.
                TxtWeeklyQuestIcon.Emoji = null;   // WPF wrote "✅" into the collapsed block
                TxtWeeklyQuestName.Text = Loc.Get("quest_weekly_done_name");
                TxtWeeklyQuestDesc.Text = Loc.Get("quest_weekly_done_desc");
                TxtWeeklyProgress.Text = TxtWeeklyXP.Text = "";
                WeeklyXpIcon.IsVisible = false;
                TxtWeeklyStreakBonus.IsVisible = TxtWeeklyRerollBonus.IsVisible = false;
                WeeklyCompletedOverlay.IsVisible = true;
                BtnRerollWeekly.IsEnabled = false;
                rerollText.Kind = null;
                rerollText.Text = null;
                rerollText.Key = "btn_completed";
            }

            // Desktop totals + the adopted read-only mobile ledger (WPF :318).
            TxtTotalDailyCompleted.Text = (quests.Progress.TotalDailyQuestsCompleted + s.MobileQuestDailyCompleted).ToString();
            TxtTotalWeeklyCompleted.Text = (quests.Progress.TotalWeeklyQuestsCompleted + s.MobileQuestWeeklyCompleted).ToString();
            TxtTotalQuestXP.Text = (quests.Progress.TotalXPFromQuests + s.MobileQuestXP).ToString();
            TxtStreakFixCharges.Text = s.StreakFixCharges.ToString();
            TxtQuestStats.Text = $"{dailyCompleted + (weekly?.IsCompleted == true ? 1 : 0)} completed today";
            // The bonus the next daily pays at, as the daily cards quote it.
            TxtQuestStreakCount.Inlines = s.DailyQuestStreak > 0
                ? new InlineCollection
                {
                    new InlineUIContainer(IconGlyph.Of(IconKind.Fire, IconVariant.Filled, IconBrushes.Fire, 13)) { BaselineAlignment = BaselineAlignment.Center },
                    new Run($" {s.DailyQuestStreak} day streak (+{quests.StreakPaidOn(QuestType.Daily, s) * 3}% XP)"),
                }
                : new InlineCollection();

            PaintStreakCalendar();
        }

        private void ApplyWeeklyBar(bool animate = true)
        {
            var width = WeeklyProgressTrack.Bounds.Width;
            if (width <= 0) return;
            var target = width * _weeklyFraction;
            var current = WeeklyProgressFill.Width;
            if (animate && Math.Abs(target - current) < 0.5) return;
            // WPF SetQuestProgress / MotionFx.BarFill: progress takes 0.6 s QuadraticEaseOut;
            // a resize is layout, so it cancels the tween and seats the stored fraction now.
            WeeklyProgressFill.Transitions = null;
            WeeklyProgressFill.Width = double.IsNaN(current) ? 0 : current;
            WeeklyProgressFill.Transitions = animate && AmbientFxCanvas.Env.AllowTransitions
                ? new Transitions { new DoubleTransition { Property = WidthProperty,
                    Duration = TimeSpan.FromSeconds(0.6), Easing = new QuadraticEaseOut() } }
                : null;
            WeeklyProgressFill.Width = target;
        }

        private void PaintMonth(QuestService quests)
        {
            _fixRings.Clear();   // the canvas was just cleared: never pulse rings no longer on screen
            double width = StreakCalendarCanvas.Bounds.Width;
            if (width <= 0) { PaintFixButton(StreakFix.HasMissedDays(quests.Progress, Today())); return; }   // not laid out yet
            var completed = new HashSet<DateTime>(quests.Progress.DailyQuestCompletionDates.Select(d => d.Date));
            var shielded = new HashSet<DateTime>(CoreSettings.Current.StreakShieldUsedDates.Select(d => d.Date));
            IBrush accent;
            try { accent = new SolidColorBrush(Color.Parse(CoreMods.AccentColorHex)); }
            catch { accent = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)); }
            var panel = this.TryFindResource("PanelBgBrush", out var p) && p is IBrush pb ? pb : Brushes.Transparent;

            var today = Today();
            int days = DateTime.DaysInMonth(today.Year, today.Month);
            double spacing = width / days, centerY = 25, prevX = 0;
            bool prevDone = false, hasMissedDays = false;
            const string letters = "SMTWTFS";
            for (int i = 0; i < days; i++)
            {
                var day = new DateTime(today.Year, today.Month, i + 1);
                bool done = completed.Contains(day), future = day > today, isToday = day == today;
                bool missed = !done && day < today;
                hasMissedDays |= missed;
                double size = day.DayOfWeek == DayOfWeek.Sunday ? 26 : 20, x = spacing * i + spacing / 2;
                if (i > 0)
                    StreakCalendarCanvas.Children.Add(new Line
                    {
                        StartPoint = new Point(prevX, centerY), EndPoint = new Point(x, centerY),
                        StrokeThickness = 2, Stroke = done && prevDone ? accent : Grey,
                    });
                var node = new Border
                {
                    Width = size, Height = size, CornerRadius = new CornerRadius(size / 2),
                    Background = done ? accent : panel,
                    BorderBrush = isToday ? Gold : Grey, BorderThickness = new Thickness(isToday ? 2 : 1),
                    Child = new TextBlock
                    {
                        Text = $"{letters[(int)day.DayOfWeek]}{day.Day}", FontSize = 7, FontWeight = FontWeight.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                        Foreground = done ? Brushes.White : new SolidColorBrush(future ? Color.Parse("#444444") : Color.Parse("#888888")),
                    },
                };
                Canvas.SetLeft(node, x - size / 2);
                Canvas.SetTop(node, centerY - size / 2);
                StreakCalendarCanvas.Children.Add(node);
                if (shielded.Contains(day))
                {
                    var shield = IconGlyph.Of(IconKind.Shield, IconVariant.Filled, null, 10);
                    Canvas.SetLeft(shield, x - 6);
                    Canvas.SetTop(shield, centerY - size / 2 - 12);
                    StreakCalendarCanvas.Children.Add(shield);
                }
                if (_isStreakFixMode && missed) AddFixRing(day, x, centerY, size, accent);   // WPF :694
                prevX = x;
                prevDone = done;
            }
            PaintFixButton(hasMissedDays);
        }
    }
}
