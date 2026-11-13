// PORTED from ConditioningControlPanel/Views/Tabs/ChasterTabView.xaml.cs: RefreshNumbers (:223),
// RefreshDay (:848), LayoutCap (:868), RefreshRun (:886), StatRun_Click + BuildBill (:893-903).
// The tab's own receipt: the third number opens and closes the bill on paper under the numbers.
// ponytail: FxBill's print-down (height grow/fade/settle, Fx.cs:810) and the cap bloom are not ported;
// the receipt opens and closes at once, as WPF does under reduced motion.
using System;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Chaster;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ChasterTabView
    {
        private bool _billOpen;
        private double _capFraction;

        private void NumbersInit()
        {
            StatRun.PointerReleased += (_, _) => { ToggleBill(); FxTagTug(); };
            CapTrack.SizeChanged += (_, _) => LayoutCap(animate: false);
        }

        internal void RefreshNumbers(bool animate = true)
        {
            var chaster = ChasterHead.Service;
            if (chaster == null) return;
            var balance = chaster.BalanceSeconds;
            RefreshTag(balance); // WPF ChasterTabView.xaml.cs:231
            TxtBalance.Text = balance == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(balance);
            TxtBalance.Foreground = FigureBrush(balance);
            TxtBalanceCaption.Text = Loc.Get(balance < 0 ? "chaster_credit_caption" : "chaster_balance_caption");

            var today = chaster.TodayAddedSeconds;
            var capSeconds = chaster.Caps.DailySeconds;
            var cap = CircesTab.Format(capSeconds, signed: false);
            TxtToday.Text = CircesTab.Format(today, signed: false);
            TxtTodayCap.Text = "/ " + cap;
            TxtTodaySub.Text = Loc.GetF("chaster_stat_today_sub", cap);
            _capFraction = TabPageText.CapFraction(today, capSeconds);
            LayoutCap(animate);

            var net = chaster.Bill().NetSeconds;
            TxtRun.Text = net == 0 ? CircesTab.Format(0, signed: false) : CircesTab.Format(net);
            TxtRun.Foreground = FigureBrush(net);
            if (_billOpen) Receipt.Show(chaster.Bill());
        }

        internal void LayoutCap(bool animate = true)
        {
            var width = Math.Max(0, CapTrack.Bounds.Width * _capFraction);
            var current = CapFill.Width;
            if (animate && Math.Abs(width - current) <= 0.5) return;
            CapFill.Transitions = null;
            CapFill.Width = double.IsNaN(current) ? 0 : current;
            CapFill.Transitions = animate && AmbientFxCanvas.Env.AllowTransitions
                ? new Transitions { new DoubleTransition { Property = Control.WidthProperty,
                    Duration = TimeSpan.FromSeconds(0.6), Easing = new QuadraticEaseOut() } }
                : null;
            CapFill.Width = width;
        }

        internal void ToggleBill()
        {
            _billOpen = !_billOpen;
            if (_billOpen) Receipt.Show(ChasterHead.Service?.Bill());
            ReceiptHost.IsVisible = _billOpen;
            TxtRunChevron.Kind = _billOpen ? IconKind.ChevronUp : IconKind.ChevronDown;
        }

        private IBrush FigureBrush(int seconds) =>
            seconds > 0 ? new SolidColorBrush(CostColour) : seconds < 0 ? new SolidColorBrush(EarnColour) : Brush("TextLightBrush");
    }
}
