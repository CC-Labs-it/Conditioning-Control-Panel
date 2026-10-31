using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// The Assets tab's "Online media today" card, the one-time official note and the Scrolller credit (WPF
    /// MainWindow.OnlineMeter.cs PaintOnlineMeterCard / PaintOnlineOfficialNote + AssetsTabView.xaml:273-465). What
    /// shows is Core <see cref="OnlineMeterRules"/> through <see cref="OnlineMeterHead"/>; this file only paints it.
    /// <para>ponytail: not on this head yet - the "See plans" link (no vault gate card here), the spent corner notice,
    /// the licence card on the startup ladder.</para>
    /// </summary>
    public partial class AssetsTabView
    {
        private bool _meterWired;

        /// <summary>WPF ScrolllerCredit.HomeLink.</summary>
        internal const string ScrolllerHome = "https://scrolller.com";

        /// <summary>Hooks the meter's clock, "Nice", the credit link and the "?" help. Idempotent.</summary>
        internal void WireOnlineMeter()
        {
            if (_meterWired) return;
            _meterWired = true;
            InitializeOfficialNoteLanding();
            try
            {
                OnlineMeterHead.Start();
                OnlineMeterHead.Refresh += RefreshOnlineMeter;
                DetachedFromVisualTree += (_, _) => OnlineMeterHead.Refresh -= RefreshOnlineMeter;
                AttachedToVisualTree += (_, _) => { OnlineMeterHead.Refresh -= RefreshOnlineMeter; OnlineMeterHead.Refresh += RefreshOnlineMeter; };
                BtnOnlineOfficialNice.Click += (_, _) => OnlineOfficialNice();
                // The credit is the shared ScrolllerCreditLine: its own link opens scrolller.com and refuses under
                // Strict Lock, Lockdown or a Chaos run (a second click handler here would open it twice).
                ToolTip.SetTip(OnlineMeterHelp, HelpText());
            }
            catch (Exception ex) { Log.Warning(ex, "Online meter wiring failed"); }
            RefreshOnlineMeter();
        }

        /// <summary>The "?" slip as one tooltip: the title, then every paragraph (OnlineMeterRules.HelpParagraphKeys).</summary>
        internal static string HelpText() =>
            string.Join("\n\n", new[] { Loc.Get(OnlineMeterRules.HelpTitleKey) }.Concat(OnlineMeterRules.HelpParagraphs(Loc.Get)));

        /// <summary>Repaints the card and the note from the live allowance. UI thread.</summary>
        internal void RefreshOnlineMeter()
        {
            try { PaintOnlineMeter(OnlineMeterHead.Now(), DateTime.Now, ScrolllerAllowance.NextResetLocal); }
            catch (Exception ex) { Log.Debug("Online meter refresh failed: {E}", ex.Message); }
        }

        internal void PaintOnlineMeter(OnlineMeterState st, DateTime now, DateTime reset)
        {
            OnlineMeterCard.IsVisible = st.Show;
            OnlineMeterStamped.IsVisible = st.ShowStampedLine;
            OnlineOfficialNote.IsVisible = st.ShowOfficialNote;
            if (st.ShowOfficialNote) TxtOnlineOfficialPlan.Text = Loc.Get(OnlineMeterRules.OfficialPlanKey(st.Plan));
            if (!st.Show) return;
            var tone = new SolidColorBrush(ToneColor(st.Tone));
            TxtOnlineMeterPct.Text = string.Format(Loc.Get("online_meter_pct"), st.Percent);
            TxtOnlineMeterPct.Foreground = st.Spent ? new SolidColorBrush(Color.FromRgb(0xA7, 0xA7, 0xC1)) : tone;
            OnlineMeterBar.ColumnDefinitions[0].Width = new GridLength(st.Percent, GridUnitType.Star);
            OnlineMeterBar.ColumnDefinitions[1].Width = new GridLength(100 - st.Percent, GridUnitType.Star);
            OnlineMeterBarFill.Background = tone;
            TxtOnlineMeterLine.Text = OnlineMeterHead.CardLine(st, now, reset);
        }

        /// <summary>WPF OnlineMeterRing.ColorFor: pink, amber from 80%, grey once used up.</summary>
        internal static Color ToneColor(OnlineMeterTone tone) => tone switch
        {
            OnlineMeterTone.Spent => Color.FromRgb(0xA7, 0xA7, 0xC1),
            OnlineMeterTone.Amber => Color.FromRgb(0xFF, 0xB3, 0x47),
            _ => Color.FromRgb(0xFF, 0x69, 0xB4),
        };

        /// <summary>"Nice": the note folds away for good on this PC; the stamped line stays (WPF OnlineOfficialNice).</summary>
        internal void OnlineOfficialNice()
        {
            try
            {
                var s = CoreSettings.Current;
                if (s != null) { s.ScrolllerOfficialNoteSeen = true; CoreSettings.Save(); }
                OnlineMeterHead.Poke();
                RefreshOnlineMeter();
            }
            catch (Exception ex) { Log.Debug("Official note Nice: {E}", ex.Message); }
        }

        /// <summary>WPF ScrolllerCredit.OpenHome: refused under Strict Lock, a Lockdown or a Chaos run (a browser
        /// coming up over them would be a way out).</summary>
        internal static bool OpenScrolllerHome()
        {
            try
            {
                if (!CanOpenScrolllerHome(CoreSettings.Current?.StrictLockEnabled == true,
                        ConditioningControlPanel.Services.LockdownService.Current?.IsActive == true,
                        ConditioningControlPanel.Avalonia.Views.Chaos.ChaosRunHost.IsActive)) return false;
                return ExternalOpener.Open(ScrolllerHome);
            }
            catch (Exception ex) { Log.Debug("Scrolller home open failed: {E}", ex.Message); return false; }
        }

        /// <summary>WPF ScrolllerCredit.CanOpenLink.</summary>
        internal static bool CanOpenScrolllerHome(bool strict, bool lockdown, bool chaos) => !strict && !lockdown && !chaos;
    }
}
