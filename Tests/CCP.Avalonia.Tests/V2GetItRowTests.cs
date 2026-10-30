using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Get it row (WPF Controls/V2GetItRow) on the Flashes card: Core V2PurchaseRule over this head's
/// inputs. Owned hides it, signed out offers the login, signed in reads "not available" (no counter here).</summary>
public sealed class V2GetItRowTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    [Fact]
    public void Decide_maps_ownership_and_sign_in_to_the_rule_states()
    {
        Assert.Equal(V2PurchaseRowState.Hidden,
            V2GetItRow.Decide(V2PurchaseRule.FlashesPrizeId, true, id => id == "fx.flash.pendulum").State);
        Assert.Equal(V2PurchaseRowState.Hidden,
            V2GetItRow.Decide(V2PurchaseRule.BubblesPrizeId, false, id => id == "fx.bubble.spiral_in").State);
        // Jackpot Remix is its own prize: owning it keeps the offer up (V2PurchaseRule.OwnsPrize).
        var signIn = V2GetItRow.Decide(V2PurchaseRule.FlashesPrizeId, false, id => id == "fx.flash.jackpot_remix");
        Assert.Equal(V2PurchaseRowState.SignIn, signIn.State);
        Assert.True(signIn.ShowsBuyButton);
        var signedIn = V2GetItRow.Decide(V2PurchaseRule.FlashesPrizeId, true, _ => false);
        Assert.Equal(V2PurchaseRowState.Unavailable, signedIn.State);
        Assert.False(signedIn.ShowsBuyButton || signedIn.ShowsPrice || signedIn.BuyEnabled);
    }

    [Fact]
    public async Task Flash_card_row_offers_sign_in_then_unavailable_and_hides_when_owned()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var (id, token, offline, seam) = (s.UnifiedId, s.AuthToken, s.OfflineMode, PrizeOwnership.IsGranted);
            var shatter = s.FlashShatterEnabled;
            var unified = CoreAccount.UnifiedUserId;
            var (get, put) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
            var secrets = new System.Collections.Generic.Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            Window? host = null;
            try
            {
                (s.UnifiedId, s.AuthToken, s.OfflineMode) = (null, null, false);
                PrizeOwnership.IsGranted = PrizeOwnership.Held;
                PrizeOwnership.ResetHeld();
                s.FlashShatterEnabled = true;
                var card = new FlashFeatureControl();
                host = new Window { Content = card };
                host.Show();
                var row = card.FindControl<V2GetItRow>("RowGetFlashesV2")!;
                var btn = row.FindControl<Button>("BtnGet")!;
                var status = row.FindControl<TextBlock>("TxtStatus")!;
                var exit = card.FindControl<ComboBox>("CmbExit")!;
                var note = card.FindControl<TextBlock>("TxtExitShatterNote")!;
                Assert.True(exit.IsEnabled);
                Assert.False(note.IsVisible);
                Assert.True(card.FindControl<Border>("BoxFlashV2")!.IsVisible);
                Assert.True(row.IsVisible);
                Assert.Equal(Loc.Get("v2_get_flash_blurb"), row.FindControl<TextBlock>("TxtBlurb")!.Text);
                Assert.Equal(Loc.Get("v2_get_signin"), status.Text);
                Assert.Equal(Loc.Get("v2_get_signin_button"), row.FindControl<TextBlock>("TxtGet")!.Text);
                Assert.True(btn.IsVisible && btn.IsEnabled);
                Assert.False(row.FindControl<StackPanel>("PricePlate")!.IsVisible);

                int opened = 0;
                row.OpenLogin = () => { opened++; return Task.CompletedTask; };
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, opened);

                // The live trigger on this head: a sign-in moves CoreAccount.UnifiedUserId (no direct Apply).
                (s.UnifiedId, s.AuthToken) = ("u-test", "t-test");
                Assert.Equal(Loc.Get("v2_get_signin"), status.Text);
                CoreAccount.UnifiedUserId = "u-test";
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Loc.Get("v2_get_unavailable"), status.Text);
                Assert.False(btn.IsVisible);
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, opened);

                PrizeOwnership.ApplySnapshot("u-test", 1, new[] { PrizeOwnership.FlashDriftBounce });
                Dispatcher.UIThread.RunJobs();
                Assert.False(row.IsVisible);
                Assert.True(row.IsRowHidden);
                Assert.True(card.FindControl<Control>("RowMotion")!.IsVisible);
                // WPF FlashFeatureControl.xaml.cs:75,191-196 refreshes exit state on grants.
                Assert.False(exit.IsEnabled);
                Assert.True(note.IsVisible);
                PrizeOwnership.ApplySnapshot("u-test", 2, Array.Empty<string>());
                Dispatcher.UIThread.RunJobs();
                Assert.True(row.IsVisible);
                Assert.False(card.FindControl<Control>("RowMotion")!.IsVisible);
                Assert.True(exit.IsEnabled);
                Assert.False(note.IsVisible);
                // The 7.2 base also gates Shatter on either motion grant, including Pendulum.
                PrizeOwnership.ApplySnapshot("u-test", 3, new[] { PrizeOwnership.FlashPendulum });
                Dispatcher.UIThread.RunJobs();
                Assert.True(card.FindControl<Control>("RowShatter")!.IsVisible);
                Assert.False(exit.IsEnabled);
                Assert.True(note.IsVisible);
                Assert.False(row.IsVisible);
                PrizeOwnership.ApplySnapshot("u-test", 4, Array.Empty<string>());
                Dispatcher.UIThread.RunJobs();
                Assert.False(card.FindControl<Control>("RowShatter")!.IsVisible);
                Assert.True(exit.IsEnabled);
                Assert.False(note.IsVisible);
                Assert.True(s.FlashShatterEnabled);
                Assert.True(row.IsVisible);
            }
            finally
            {
                host?.Close();
                PrizeOwnership.ResetHeld();
                CoreAccount.UnifiedUserId = unified;
                s.FlashShatterEnabled = shatter;
                (s.UnifiedId, s.AuthToken, s.OfflineMode, PrizeOwnership.IsGranted) = (id, token, offline, seam);
                (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (get, put);
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>Row feat-backroom-counter-buy: the startup-built service over a fake proxy. The row reads
    /// the counter when shown, Get it confirms at the shown price, buys, toasts, adopts the receipt's
    /// SP and hides once the reply's grants land; a refusal shows WPF's key. Never reaches a server.</summary>
    [Fact]
    public async Task Get_it_reads_the_counter_confirms_buys_toasts_and_hides()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var (id, token, offline, sp, seam) = (s.UnifiedId, s.AuthToken, s.OfflineMode, s.SkillPoints, PrizeOwnership.IsGranted);
            var (get, put) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
            var secrets = new System.Collections.Generic.Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            var h = new FakeProxy();
            Window? host = null;
            try
            {
                (s.UnifiedId, s.AuthToken, s.OfflineMode, s.SkillPoints) = ("u-buy", "t-buy", false, 50);
                PrizeOwnership.IsGranted = g => PrizeOwnership.Held(g);
                PrizeOwnership.ResetHeld();
                var svc = V2PurchaseHead.Create(null, "http://127.0.0.1:9", new System.Net.Http.HttpClient(h))!;
                V2PurchaseHead.Service = svc;
                h.Reply("{\"ok\":true,\"open\":true,\"catalogVersion\":4,\"sp\":50,\"catalog\":[{\"id\":\"flashes_v2\",\"priceSp\":30,\"sale\":\"on\"}]}");
                var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                // Posted, so it queues behind the row's own repaint (the row subscribes on attach, below).
                svc.Changed += () => Dispatcher.UIThread.Post(() => read.TrySetResult());
                var toasts = new StackPanel();
                global::ConditioningControlPanel.Avalonia.App.Notifications.AttachHost(toasts);

                var card = new FlashFeatureControl();
                host = new Window { Content = card };
                host.Show();
                await read.Task.WaitAsync(TimeSpan.FromSeconds(10));   // the read the shown row started
                Dispatcher.UIThread.RunJobs();
                var row = card.FindControl<V2GetItRow>("RowGetFlashesV2")!;
                var btn = row.FindControl<Button>("BtnGet")!;
                Assert.Equal(V2PurchaseRowState.Offer, row.Row.State);
                Assert.True(row.FindControl<StackPanel>("PricePlate")!.IsVisible);
                Assert.Equal(30.ToString(System.Globalization.CultureInfo.CurrentCulture), row.FindControl<TextBlock>("TxtPrice")!.Text);
                Assert.Equal(Loc.Get("v2_get_button"), row.FindControl<TextBlock>("TxtGet")!.Text);
                Assert.True(btn.IsVisible && btn.IsEnabled);

                // A refusal: insufficient -> WPF's key, the row stays and offers again.
                string? asked = null;
                row.Confirm = (t, b) => { asked = t + "|" + b; return Task.FromResult(true); };
                h.Reply("{\"ok\":false,\"reason\":\"insufficient\"}", System.Net.HttpStatusCode.PaymentRequired);
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await row.LastBuy!.WaitAsync(TimeSpan.FromSeconds(10)) == false);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(Loc.Get("v2_get_confirm_title") + "|" + Loc.GetF("v2_get_confirm_body", 30, Loc.Get("section_flash_v2")), asked);
                Assert.Equal(Loc.Get("v2_get_error_sp"), row.FindControl<TextBlock>("TxtStatus")!.Text);
                Assert.True(btn.IsEnabled);

                // Declining the confirm sends nothing.
                row.Confirm = (_, _) => Task.FromResult(false);
                var sent = h.Seen.Count;
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(sent, h.Seen.Count);

                row.Confirm = (_, _) => Task.FromResult(true);
                h.Reply("{\"ok\":true,\"sp\":20,\"prizes\":{\"revision\":7,\"grants\":[\"fx.flash.drift_bounce\"]}}");
                h.Reply("{\"ok\":true,\"open\":true,\"catalogVersion\":4,\"catalog\":[]}");   // the post-buy refresh
                btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(await row.LastBuy!.WaitAsync(TimeSpan.FromSeconds(10)));
                Dispatcher.UIThread.RunJobs();
                Assert.False(row.IsVisible);
                // WPF rebuilds the motion picker on GrantsChanged: the bought Drift & Bounce shows at once.
                Assert.True(card.FindControl<Border>("BoxFlashV2")!.IsVisible);
                Assert.True(card.FindControl<Control>("RowMotion")!.IsVisible);
                Assert.Contains(card.FindControl<ComboBox>("CmbMotion")!.Items.OfType<ComboBoxItem>(),
                    i => i.Tag is ConditioningControlPanel.Models.FlashMotionStyle.DriftBounce);
                Assert.Equal(20, s.SkillPoints);
                Assert.Contains(toasts.GetLogicalDescendants().OfType<TextBlock>(),
                    t => t.Text == Loc.GetF("v2_get_toast_done", Loc.Get("section_flash_v2")));
            }
            finally
            {
                host?.Close();
                V2PurchaseHead.Service = null;
                PrizeOwnership.ResetHeld();
                (s.UnifiedId, s.AuthToken, s.OfflineMode, s.SkillPoints, PrizeOwnership.IsGranted) = (id, token, offline, sp, seam);
                (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (get, put);
            }
        });
    }

    [Fact]
    public async Task Snapshot_repaints_without_a_counter_and_detach_unsubscribes()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var (id, token, offline, seam) = (s.UnifiedId, s.AuthToken, s.OfflineMode, PrizeOwnership.IsGranted);
            var (get, put) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
            var secrets = new System.Collections.Generic.Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = n => secrets.TryGetValue(n, out var v) ? v : null;
            CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
            Window? host = null;
            try
            {
                (s.UnifiedId, s.AuthToken, s.OfflineMode) = (null, null, false);
                PrizeOwnership.IsGranted = PrizeOwnership.Held;
                PrizeOwnership.ResetHeld();
                var row = new V2GetItRow();
                row.Configure(V2PurchaseRule.BubblesPrizeId, "v2_get_bubble_blurb", "label_bubbles_v2_box");
                host = new Window { Content = row };
                host.Show();
                Assert.True(row.IsVisible);
                Assert.Equal(Loc.Get("v2_get_bubble_blurb"), row.FindControl<TextBlock>("TxtBlurb")!.Text);
                Assert.Equal(V2PurchaseRowState.SignIn, row.Row.State);
                int opened = 0;
                row.OpenLogin = () => { opened++; return Task.CompletedTask; };
                row.FindControl<Button>("BtnGet")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, opened);

                (s.UnifiedId, s.AuthToken) = ("u-bubbles", "t-bubbles");
                row.Apply();
                Assert.Equal(V2PurchaseRowState.Unavailable, row.Row.State);
                PrizeOwnership.ApplySnapshot("u-bubbles", 1, new[] { PrizeOwnership.BubbleRain });
                Dispatcher.UIThread.RunJobs();
                Assert.True(row.IsRowHidden);
                Assert.False(row.IsVisible);
                // A revoke restores the offer, without a purchase service or direct Apply.
                PrizeOwnership.ApplySnapshot("u-bubbles", 2, Array.Empty<string>());
                Dispatcher.UIThread.RunJobs();
                Assert.False(row.IsRowHidden);
                Assert.True(row.IsVisible);
                host.Content = null;
                var state = row.Row;
                PrizeOwnership.ApplySnapshot("u-bubbles", 3, new[] { PrizeOwnership.BubbleRain });
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(state, row.Row); // detached rows no longer listen
            }
            finally
            {
                host?.Close();
                PrizeOwnership.ResetHeld();
                (s.UnifiedId, s.AuthToken, s.OfflineMode, PrizeOwnership.IsGranted) = (id, token, offline, seam);
                (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (get, put);
            }
            return Task.CompletedTask;
        });
    }

    /// <summary>Scripted proxy: one reply per request, in order; never a socket.</summary>
    private sealed class FakeProxy : System.Net.Http.HttpMessageHandler
    {
        public readonly System.Collections.Concurrent.ConcurrentQueue<string> Seen = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<(string Json, System.Net.HttpStatusCode Code)> _replies = new();
        public void Reply(string json, System.Net.HttpStatusCode code = System.Net.HttpStatusCode.OK) => _replies.Enqueue((json, code));
        protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage r, System.Threading.CancellationToken ct)
        {
            Seen.Enqueue(r.RequestUri!.AbsolutePath);
            if (!_replies.TryDequeue(out var next)) throw new System.Net.Http.HttpRequestException("no scripted reply");
            return Task.FromResult(new System.Net.Http.HttpResponseMessage(next.Code)
                { Content = new System.Net.Http.StringContent(next.Json, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
