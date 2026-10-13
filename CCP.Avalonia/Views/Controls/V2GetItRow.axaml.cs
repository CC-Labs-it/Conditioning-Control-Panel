using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// The "Get it" row inside a v2 box (WPF <c>Controls/V2GetItRow.xaml.cs</c>). One prize, one
    /// price, one button; it draws whatever Core <see cref="V2PurchaseRule"/> decides.
    /// <see cref="RowChanged"/> tells the panel to re-measure its box, because a box holding only
    /// this row must collapse the moment the row goes <see cref="V2PurchaseRowState.Hidden"/>.
    /// </summary>
    public partial class V2GetItRow : UserControl
    {
        private string _prizeId = string.Empty;

        /// <summary>Raised after the row repaints, so the panel can re-measure its box.</summary>
        public event EventHandler? RowChanged;

        /// <summary>True while the account owns the prize: the box should ignore this row entirely.</summary>
        public bool IsRowHidden { get; private set; } = true;

        /// <summary>The row the last <see cref="Apply"/> drew (tests read it).</summary>
        internal V2PurchaseRow Row { get; private set; }

        /// <summary>Opens the login dialog on a SignIn press. Defaults to the shell's own (WPF
        /// MainWindowRef.OpenUnifiedLoginDialog); tests replace it.</summary>
        internal Func<Task>? OpenLogin { get; set; }

        public V2GetItRow()
        {
            InitializeComponent();
            BtnGet.Click += BtnGet_Click;
        }

        /// <summary>Name the prize this row sells (WPF Configure; nameKey is only the confirm's,
        /// which this head never reaches - see the Unavailable branch in <see cref="Decide"/>).</summary>
        public void Configure(string prizeId, string blurbKey)
        {
            _prizeId = prizeId;
            TxtBlurb.Text = Loc.Get(blurbKey);
            Apply();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            // WPF repaints on V2PurchaseService.Changed and PrizeGrants.GrantsChanged; here a sign-in
            // or sign-out is the only thing that moves the row (ownership is fixed per run, P41).
            CoreAccount.UnifiedIdentityChanged += OnIdentityChanged;
            Apply();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            CoreAccount.UnifiedIdentityChanged -= OnIdentityChanged;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnIdentityChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Apply);

        /// <summary>The row for one prize on this head: Core's rule over this head's inputs.</summary>
        internal static V2PurchaseRow Decide(string prizeId, bool signedIn, Func<string, bool> isGranted)
            // ponytail: no counter on this head (row feat-backroom-counter-buy: WPF V2PurchaseService
            // + BackRoomApi counter/state + counter/buy), so a signed-in account reads as a shut door:
            // "not available", no button - never a forever "Checking…".
            => V2PurchaseRule.Decide(V2PurchaseRule.OwnsPrize(prizeId, isGranted), signedIn,
                stateKnown: true, doorOpen: false, onSale: false, priceSp: 0, sp: 0, busy: false, failureKey: null);

        /// <summary>Reads the rule and dresses the row (WPF Apply, same statements). UI thread.</summary>
        public void Apply()
        {
            if (string.IsNullOrEmpty(_prizeId)) return;
            var row = Row = Decide(_prizeId, Platform.FriendsHead.Identity() != null, Platform.PrizeOwnership.IsGranted);

            IsRowHidden = row.State == V2PurchaseRowState.Hidden;
            IsVisible = !IsRowHidden;

            PricePlate.IsVisible = row.ShowsPrice;
            TxtPrice.Text = row.PriceSp.ToString(CultureInfo.CurrentCulture);

            BtnGet.IsVisible = row.ShowsBuyButton;
            BtnGet.IsEnabled = row.State == V2PurchaseRowState.SignIn || row.BuyEnabled;
            BtnGet.Opacity = BtnGet.IsEnabled ? 1.0 : 0.5;
            TxtGet.Text = Loc.Get(row.State == V2PurchaseRowState.SignIn ? "v2_get_signin_button" : "v2_get_button");

            TxtStatus.Text = row.State switch
            {
                V2PurchaseRowState.SignIn => Loc.Get("v2_get_signin"),
                V2PurchaseRowState.Loading => Loc.Get("v2_get_checking"),
                V2PurchaseRowState.Unavailable => Loc.Get("v2_get_unavailable"),
                V2PurchaseRowState.Busy => Loc.Get("v2_get_working"),
                V2PurchaseRowState.CannotAfford => Loc.GetF("v2_get_short", row.ShortBy),
                V2PurchaseRowState.Failed => Loc.Get(row.MessageKey ?? "v2_get_error_generic"),
                _ => string.Empty,
            };

            RowChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>WPF BtnGet_Click: SignIn opens the login; every other state is dead here (no
        /// price ever reaches this head, and WPF refuses a press without one).</summary>
        private async void BtnGet_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_prizeId) || Row.State != V2PurchaseRowState.SignIn) return;
            var open = OpenLogin ?? (() => (TopLevel.GetTopLevel(this) as MainShellWindow)?.OpenUnifiedLoginDialog() ?? Task.CompletedTask);
            await open();
        }
    }
}
