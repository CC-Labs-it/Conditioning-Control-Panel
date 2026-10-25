using System;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Prizes;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// The "Get it" row inside a v2 box (WPF <c>Controls/V2GetItRow.xaml.cs</c>). One prize, one
    /// price, one button; it draws whatever Core <see cref="V2PurchaseService"/> decides.
    /// <see cref="RowChanged"/> tells the panel to re-measure its box, because a box holding only
    /// this row must collapse the moment the row goes <see cref="V2PurchaseRowState.Hidden"/>.
    /// </summary>
    public partial class V2GetItRow : UserControl
    {
        private string _prizeId = string.Empty;
        private string _nameKey = string.Empty;
        private V2PurchaseService? _wired;

        /// <summary>Raised after the row repaints, so the panel can re-measure its box.</summary>
        public event EventHandler? RowChanged;

        /// <summary>True while the account owns the prize: the box should ignore this row entirely.</summary>
        public bool IsRowHidden { get; private set; } = true;

        /// <summary>The row the last <see cref="Apply"/> drew (tests read it).</summary>
        internal V2PurchaseRow Row { get; private set; }

        /// <summary>Opens the login dialog on a SignIn press. Defaults to the shell's own (WPF
        /// MainWindowRef.OpenUnifiedLoginDialog); tests replace it.</summary>
        internal Func<Task>? OpenLogin { get; set; }

        /// <summary>The spend confirm (WPF MessageBox YesNo). Args: title, body. Tests replace it.</summary>
        internal Func<string, string, Task<bool>>? Confirm { get; set; }

        /// <summary>The task of the last buy the button started (tests await it, P56).</summary>
        internal Task<bool>? LastBuy { get; private set; }

        public V2GetItRow()
        {
            InitializeComponent();
            BtnGet.Click += BtnGet_Click;
        }

        private IDisposable? _shown;

        /// <summary>WPF IsVisibleChanged -> EnsureState: the counter is read the first time the row is
        /// genuinely on screen (the whole ancestor chain), never at startup.</summary>
        private void EnsureStateIfShown()
        {
            if (IsEffectivelyVisible) _wired?.EnsureState();
        }

        /// <summary>Name the prize this row sells (WPF Configure; nameKey names it in the confirm and toast).</summary>
        public void Configure(string prizeId, string blurbKey, string nameKey)
        {
            _prizeId = prizeId;
            _nameKey = nameKey;
            TxtBlurb.Text = Loc.Get(blurbKey);
            Apply();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            // WPF repaints on V2PurchaseService.Changed and PrizeGrants.GrantsChanged; a sign-in or
            // sign-out moves the row too (P41: subscribed on attach, dropped on detach).
            PrizeOwnership.Changed += OnServiceChanged;
            CoreAccount.UnifiedIdentityChanged += OnIdentityChanged;
            if ((_wired = V2PurchaseHead.Service) is { } svc)
            {
                svc.Changed += OnServiceChanged;
                svc.Bought += OnBought;
                _shown = ConditioningControlPanel.Avalonia.Controls.EffectiveVisibility.Watch(this, EnsureStateIfShown);
                EnsureStateIfShown();
            }
            Apply();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            PrizeOwnership.Changed -= OnServiceChanged;
            CoreAccount.UnifiedIdentityChanged -= OnIdentityChanged;
            if (_wired is { } svc)
            {
                svc.Changed -= OnServiceChanged;
                svc.Bought -= OnBought;
                _wired = null;
                _shown?.Dispose();
                _shown = null;
            }
            base.OnDetachedFromVisualTree(e);
        }

        private void OnIdentityChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
        {
            Apply();
            EnsureStateIfShown();
        });

        // The service finishes its work off the UI thread, so every repaint is marshalled.
        private void OnServiceChanged() => Dispatcher.UIThread.Post(Apply);

        private void OnBought(string prizeId) => Dispatcher.UIThread.Post(() =>
        {
            if (prizeId != _prizeId) return;
            App.Notifications.Show(Loc.GetF("v2_get_toast_done", Loc.Get(_nameKey)), Helpers.NotificationType.Success);
        });

        /// <summary>The row without a counter (sandbox without a loopback proxy): a signed-in account
        /// reads as a shut door - "not available", no button - never a forever "Checking…".</summary>
        internal static V2PurchaseRow Decide(string prizeId, bool signedIn, Func<string, bool> isGranted)
            => V2PurchaseRule.Decide(V2PurchaseRule.OwnsPrize(prizeId, isGranted), signedIn,
                stateKnown: true, doorOpen: false, onSale: false, priceSp: 0, sp: 0, busy: false, failureKey: null);

        /// <summary>Reads the rule and dresses the row (WPF Apply, same statements). UI thread.</summary>
        public void Apply()
        {
            if (string.IsNullOrEmpty(_prizeId)) return;
            var row = Row = V2PurchaseHead.Service?.RowFor(_prizeId)
                ?? Decide(_prizeId, FriendsHead.Identity() != null, PrizeOwnership.IsGranted);

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

        /// <summary>WPF BtnGet_Click, same order: SignIn opens the login; a priced offer asks to
        /// confirm the spend and buys at the price it showed.</summary>
        private async void BtnGet_Click(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_prizeId)) return;
            var svc = V2PurchaseHead.Service;
            var row = svc?.RowFor(_prizeId) ?? Row;

            if (row.State == V2PurchaseRowState.SignIn)
            {
                var open = OpenLogin ?? (() => (TopLevel.GetTopLevel(this) as MainShellWindow)?.OpenUnifiedLoginDialog() ?? Task.CompletedTask);
                await open();
                return;
            }
            // No price on the row means no press: a confirm reading "Spend 0 Sparkle Points" must never
            // be shown, and the buy that followed it would charge whatever the counter says.
            if (svc == null || !row.BuyEnabled || !row.ShowsPrice) return;

            var body = Loc.GetF("v2_get_confirm_body", row.PriceSp, Loc.Get(_nameKey));
            var title = Loc.Get("v2_get_confirm_title");
            // WPF MessageBox YesNo, owned by the row's window or (as WPF's ownerless box) the main window.
            var confirm = Confirm ?? ((t, b) => (TopLevel.GetTopLevel(this) as Window
                    ?? (Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow) is { } owner
                ? MessageDialog.ConfirmAsync(owner, t, b, okText: Loc.Get("btn_yes"), cancelText: Loc.Get("btn_no"))
                : Task.FromResult(false));
            if (!await confirm(title, body)) return;

            // The price that was confirmed travels with the request: if the counter has repriced in
            // between, the service refuses rather than charging a number the player never saw.
            await (LastBuy = svc.BuyAsync(_prizeId, row.PriceSp));
        }
    }
}
