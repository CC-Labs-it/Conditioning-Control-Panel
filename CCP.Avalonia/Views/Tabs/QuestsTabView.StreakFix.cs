using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The manual streak fix, PORTED from MainWindow.QuestsTab.cs:740-963: the "Fix Day (N)" button state, fix mode
    /// (pulsing rings on missed days), the confirmation, and the server-authoritative spend through
    /// <see cref="CoreAccount.UseStreakFixProvider"/> with WPF's one-spend-at-a-time guard and cancel-while-in-flight rule.
    /// </summary>
    public partial class QuestsTabView
    {
        private static readonly IBrush FixError = new SolidColorBrush(Color.Parse("#FF5252"));
        private static readonly IBrush FixSuccess = new SolidColorBrush(Color.Parse("#00E676"));
        private readonly List<Border> _fixHighlights = new();
        private bool _isStreakFixMode, _streakFixInFlight;
        private IDisposable? _fixStatusTimer;

        /// <summary>Test hooks: fix mode, the clickable missed-day rings, and the confirmation (null = the real dialog).</summary>
        internal bool IsStreakFixMode => _isStreakFixMode;
        internal IReadOnlyList<Border> FixHighlights => _fixHighlights;
        internal Func<string, string, Task<bool>>? ConfirmFix { get; set; }

        private static IBrush AccentBrush()
        {
            try { return new SolidColorBrush(Color.Parse(CoreMods.AccentColorHex)); }
            catch { return new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)); }
        }

        /// <summary>Any day of this month before today without a completed daily (WPF :779).</summary>
        private static bool HasMissedDays(QuestService quests)
        {
            var today = DateTime.Today;
            var completed = quests.Progress.DailyQuestCompletionDates.Select(d => d.Date).ToHashSet();
            for (int d = 1; d < today.Day; d++)
                if (!completed.Contains(new DateTime(today.Year, today.Month, d))) return true;
            return false;
        }

        /// <summary>WPF :740-766. Visible to everyone; Cancel stays clickable at 0 charges so fix mode has an exit.</summary>
        private void UpdateFixButton(QuestService quests)
        {
            var s = CoreSettings.Current;
            int charges = s.StreakFixCharges;
            bool signedIn = !string.IsNullOrEmpty(s.UnifiedId), missed = HasMissedDays(quests);
            BtnFixStreak.IsVisible = true;
            BtnFixStreak.IsEnabled = _isStreakFixMode || (signedIn && charges > 0 && missed);
            FixStreakLabel.Args = _isStreakFixMode ? null : new object[] { charges };
            FixStreakLabel.Key = _isStreakFixMode ? "btn_cancel_2" : "btn_fix_day_with_count";
            ToolTip.SetTip(BtnFixStreak, !signedIn ? Loc.Get("tooltip_streak_fixes_need_account")
                : charges <= 0 ? Loc.Get("tooltip_no_streak_fixes_left")
                : !missed ? Loc.Get("tooltip_no_missed_days_your_streak_is_perfect")
                : Loc.GetF("tooltip_use_streak_fix", charges));
        }

        /// <summary>WPF :696-726: an accent ring over a missed day, pulsing 1 -> 0.3 in 600 ms (on the ambient clock).</summary>
        private void AddFixHighlight(DateTime day, double x, double centerY, double size, IBrush accent)
        {
            double hs = size + 4;
            var ring = new Border
            {
                ZIndex = 3, // WPF: above the day nodes, below shield markers
                Width = hs, Height = hs, CornerRadius = new CornerRadius(hs / 2),
                Background = Brushes.Transparent, BorderBrush = accent, BorderThickness = new Thickness(2),
                Cursor = new Cursor(StandardCursorType.Hand), Focusable = true, Tag = day,
            };
            ring.PointerPressed += (_, e) =>
            {
                if (e.GetCurrentPoint(ring).Properties.IsLeftButtonPressed) _ = SpendStreakFixAsync(day);
            };
            ring.KeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Space) { e.Handled = true; _ = SpendStreakFixAsync(day); }
            };
            Canvas.SetLeft(ring, x - hs / 2);
            Canvas.SetTop(ring, centerY - hs / 2);
            StreakCalendarCanvas.Children.Add(ring);
            _fixHighlights.Add(ring);
        }

        private void PulseFixHighlights(double t)
        {
            foreach (var ring in _fixHighlights) ring.Opacity = 1 - 0.7 * Tri(t % 1.2 / 0.6);
        }

        /// <summary>WPF BtnFixStreak_Click (:772).</summary>
        private void BtnFixStreak_Click()
        {
            if (_isStreakFixMode) { ExitStreakFixMode(); return; }
            if (App.Quests is not { } quests) return;
            if (CoreSettings.Current.StreakFixCharges < 1) { ShowFixStatus("label_no_streak_fixes_left"); return; }
            if (!HasMissedDays(quests)) { ShowFixStatus("label_no_broken_streak_you_re_doing_great_sweetie"); return; }
            _isStreakFixMode = true;
            ShowFixStatus("label_click_a_missed_day_to_fix_it_free");
            PaintStreakCalendar();
        }

        private void ExitStreakFixMode()
        {
            _isStreakFixMode = false;
            TxtFixStreakStatus.IsVisible = false;
            TxtFixStreakStatus.Key = null;
            PaintStreakCalendar();
        }

        private void ShowFixStatus(string key, params object[] args)
        {
            TxtFixStreakStatus.Kind = null;
            TxtFixStreakStatus.Text = null;
            TxtFixStreakStatus.Args = args.Length > 0 ? args : null;
            TxtFixStreakStatus.Key = key;
            TxtFixStreakStatus.IsVisible = true;
        }

        private void SetFixStatusBrush(IBrush brush) => TxtFixStreakStatus.SetValue(TextElement.ForegroundProperty, brush);

        /// <summary>WPF StreakFixDay_Click (:826): one spend at a time, balance re-checked, confirmed, spent on the
        /// server, then applied locally even if fix mode was cancelled meanwhile. XP is deliberately not touched.</summary>
        internal async Task SpendStreakFixAsync(DateTime fixDate)
        {
            if (_streakFixInFlight) return;
            _streakFixInFlight = true;
            try
            {
                var s = CoreSettings.Current;
                if (s.StreakFixCharges < 1) { ShowFixStatus("label_no_streak_fixes_left"); return; }

                var title = Loc.Get("label_fix_day_confirm_title");
                var body = Loc.GetF("label_fix_day_confirm_body", fixDate.ToString("MMMM d"), s.StreakFixCharges);
                bool yes = ConfirmFix != null ? await ConfirmFix(title, body)
                    : TopLevel.GetTopLevel(this) is Window owner
                      && await Dialogs.MessageDialog.ConfirmAsync(owner, title, body, okText: Loc.Get("label_yes"));
                if (!yes) return;

                // Invariant: a Buddhist/Umm al-Qura calendar would otherwise put year 2569 on the wire.
                var day = fixDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var spend = string.IsNullOrEmpty(s.UnifiedId) ? null : CoreAccount.UseStreakFixProvider?.Invoke(day);
                if (spend == null)
                {
                    ShowFixStatus("label_oopsie_insurance_requires_a_cloud_account_ple");
                    SetFixStatusBrush(FixError);
                    return;
                }

                ShowFixStatus("label_processing");
                var (success, error, credits) = await spend;
                if (!success)
                {
                    TxtFixStreakStatus.Key = null;
                    TxtFixStreakStatus.Args = null;
                    TxtFixStreakStatus.Kind = IconKind.DismissCircle;
                    TxtFixStreakStatus.Text = error ?? Loc.Get("label_streak_fix_failed");
                    TxtFixStreakStatus.IsVisible = true;
                    SetFixStatusBrush(FixError);
                    return;
                }

                // The server's balance is authoritative; the blind decrement is for an older server that omits it.
                s.StreakFixCharges = credits ?? Math.Max(0, s.StreakFixCharges - 1);
                s.SeasonalStreakRecoveryUsed = true; // back-compat flag, no longer a gate
                if (App.Quests is { } quests)
                {
                    if (!quests.Progress.DailyQuestCompletionDates.Any(d => d.Date == fixDate.Date))
                    {
                        quests.Progress.DailyQuestCompletionDates.Add(fixDate);
                        quests.Save();
                    }
                    quests.RecalculateStreak();
                }
                CoreSettings.Save();
                Serilog.Log.Information("Streak fix used on {Date} (server-validated), {Remaining} charge(s) left",
                    fixDate, s.StreakFixCharges);

                // Cancelled while in flight: no success banner over the torn-down mode, but the tile still repaints.
                bool stillFixing = _isStreakFixMode;
                if (stillFixing)
                {
                    _isStreakFixMode = false;
                    ShowFixStatus("label_streak_fixed_success", fixDate.ToString("MMMM d"));
                    SetFixStatusBrush(FixSuccess);
                }
                RefreshQuestUI();
                if (!stillFixing) return;

                _fixStatusTimer?.Dispose();
                _fixStatusTimer = DispatcherTimer.RunOnce(() =>
                {
                    if (_isStreakFixMode) return;
                    TxtFixStreakStatus.IsVisible = false;
                    SetFixStatusBrush(AccentBrush());
                }, TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning("Streak fix failed: {Error}", ex.Message);
            }
            finally
            {
                _streakFixInFlight = false;
            }
        }
    }
}
