// THE PRIZE COUNTER (7.2 shop) on the shared GameWindow: WPF Services/PrizeCounter/PrizeCounterHostService.cs.
// Requests go through the Core PrizeCounterRelay pinned to the opener. Panic closes it (PanicSurfaces "games",
// WPF GameSurfaces.cs:66), app exit closes it (WPF App.xaml.cs:6433), a first Escape is the page's back
// (PanicPolicy.KeptEscapePage.Counter, WPF MainWindow.xaml.cs:1531). Openers hear the close once, with its reason.
// ponytail: part B (row game-prize-counter): wear routing (init.wears stays empty, so no Wear), the doors that
// call LaunchPrizeCounter, Rain price cache, boot wallet read, SeasonRecap e_shop.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.PrizeCounter;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        internal const string PrizeCounterId = PrizeCounterRules.GameId;

        /// <summary>Test seams: the relay's transport (null = shared client) and server (null = sandbox: offline).</summary>
        internal static Func<HttpClient?> CounterHttp { get; set; } = () => null;
        internal static Func<string?> CounterServerBase { get; set; } = () => PbpServerBase();
        /// <summary>WPF RequestSignIn: the panel's account sign-in over the counter.</summary>
        internal static Func<Window, Task> CounterSignIn { get; set; } = owner =>
            Windows.MainShellWindow.Current?.OpenUnifiedLoginDialog(owner) ?? Task.CompletedTask;

        private string _counterFrom = "wallet";
        private string? _counterTab, _counterOpener, _closeReason;
        private PrizeCounterRelay? _counterRelay;
        private int _counterGeneration; private bool _counterSigningIn;
        private readonly List<Action<string>> _counterOnClosed = new();

        /// <summary>WPF Launch (:311): open the counter or bring it forward; a door naming another tab reloads it there.</summary>
        internal static GameWindow? LaunchPrizeCounter(string from = "wallet", string? tab = null, Action<string>? onClosed = null)
        {
            tab = PrizeCounterRules.CounterTab(tab);
            var w = OpenCounterWindow();
            if (w != null)
            {
                if (onClosed != null) w._counterOnClosed.Add(onClosed);
                if (PrizeCounterRules.ReopensOnTab(w._counterTab, tab))
                {
                    (w._counterTab, w._counterFrom) = (tab, string.IsNullOrWhiteSpace(from) ? w._counterFrom : from);   // WPF :320
                    w.ReloadCounter(w._counterOpener);
                }
                w.Activate(); return w;
            }
            try
            {
                w = new GameWindow(Games[PrizeCounterId]) { _counterFrom = string.IsNullOrWhiteSpace(from) ? "wallet" : from, _counterTab = tab };
                if (onClosed != null) w._counterOnClosed.Add(onClosed);
                w.Load(Server()); w.Show(); return w;
            }
            catch (Exception ex)
            {
                // A window that never opened raises no Closed: its openers hear the failure here.
                Log.Error(ex, "PrizeCounter: launch failed");
                PrizeCounterRules.NotifyOpeners(onClosed == null ? Array.Empty<Action<string>>() : new[] { onClosed }, PrizeCounterRules.CloseFailed);
                return null;
            }
        }

        /// <summary>WPF App.OnExit (:6433) CloseFor(CloseAppExit).</summary>
        internal static void CloseCounterForExit() => OpenCounterWindow()?.CloseFor(PrizeCounterRules.CloseAppExit);
        private static GameWindow? OpenCounterWindow() { lock (Open) return Open.Find(x => x.Spec.Id == PrizeCounterId); }

        /// <summary>WPF CloseFor (:412): the first reason named wins.</summary>
        internal void CloseFor(string reason) { _closeReason ??= reason; Close(); }
        private void WireCounter()
        {
            // The lifetime's own close at shutdown is an app exit, never a back to the game under it.
            Closing += (_, e) => { if (e.CloseReason == WindowCloseReason.ApplicationShutdown) _closeReason ??= PrizeCounterRules.CloseAppExit; };
            Closed += (_, _) =>
            {
                ++_counterGeneration;
                (_counterRelay, _counterOpener) = (null, null);
                var openers = _counterOnClosed.ToArray();
                _counterOnClosed.Clear();
                PrizeCounterRules.NotifyOpeners(openers, _closeReason ?? PrizeCounterRules.CloseBack);
            };
        }

        /// <summary>WPF :335 identity pin: a fresh relay for the opener and a new generation (a late reply reaches no new page).</summary>
        private void StartCounter(string? opener)
        {
            _counterOpener = opener; ++_counterGeneration;
            var baseUrl = CounterServerBase();
            _counterRelay = new PrizeCounterRelay(CounterHttp(),
                () => FriendsHead.Identity() is { } id && id.UnifiedId == _counterOpener ? id : null,
                () => baseUrl == null || CoreSettings.Current.OfflineMode,
                (account, fromBuy, sp) => Dispatcher.UIThread.Post(() => V2PurchaseHead.AdoptSp(account, fromBuy, sp)),
                PrizeOwnership.ApplySnapshot, baseUrl);
        }

        /// <summary>WPF Reload (:376): the page again in the same window, with a fresh init.</summary>
        private void ReloadCounter(string? opener)
        {
            StartCounter(opener); IsReady = false;
            if (PageUrl != null) Web.Navigate(PageUrl);
        }

        /// <summary>WPF OnPageMessage (:441).</summary>
        private void HandleCounter(JObject o)
        {
            switch ((string?)o["type"])
            {
                case "ready":
                    IsReady = true;
                    Post(CounterInit());
                    try { Web.Focus(); } catch (Exception ex) { Log.Debug("PrizeCounter: focus: {E}", ex.Message); }
                    break;
                case "request":
                    var relay = _counterRelay;
                    var reqId = (string?)o["reqId"];
                    if (relay == null || string.IsNullOrEmpty(reqId)) return;
                    var (op, body, generation) = ((string?)o["op"], o["body"] as JObject, _counterGeneration);
                    var idem = o["idem"]?.Type == JTokenType.String ? (string?)o["idem"] : null;
                    _ = Task.Run(async () =>
                    {
                        var rainOn = RainV2Gate.On;
                        var reply = PrizeCounterRules.RefusesBuy(op, body, rainOn) ? PrizeCounterRelay.Refusal("unavailable")
                            : PrizeCounterRules.HideRain(await relay.RelayAsync(op, body, idem).ConfigureAwait(false), rainOn);
                        Dispatcher.UIThread.Post(() => { if (generation == _counterGeneration && !_closing) Post(new { type = "reply", reqId, body = reply }); });
                    });
                    break;
                case "wallet":
                    if (o["sp"] is JValue { Type: JTokenType.Integer } sp && _counterRelay?.HandleWalletFrame(sp.Value<int>()) != true)
                        Log.Debug("PrizeCounter: wallet frame {Sp} names no receipt, ignored", sp);
                    break;
                case "close": CloseFor(PrizeCounterRules.PageCloseReason((string?)o["reason"])); break;
                case "signin": Dispatcher.UIThread.Post(() => _ = SignInFromCounterPage()); break;   // never inside the page's message callback
                default:
                    Log.Debug("PrizeCounter: page message '{Type}' {Msg}", (string?)o["type"], (string?)o["msg"]);
                    break;
            }
        }

        /// <summary>WPF SignInFromPage (:501): somebody new signed in is pinned and the page reloads; cancelled or closed: nothing.</summary>
        private async Task SignInFromCounterPage()
        {
            if (_counterSigningIn || _closing) return;
            if (!PrizeCounterRules.SignedInSince(_counterOpener, FriendsHead.Identity()?.UnifiedId))
            {
                _counterSigningIn = true;
                try { await CounterSignIn(this); }
                catch (Exception ex) { Log.Warning(ex, "PrizeCounter: sign-in failed"); }
                finally { _counterSigningIn = false; }
            }
            var now = FriendsHead.Identity()?.UnifiedId;
            if (!_closing && PrizeCounterRules.SignedInSince(_counterOpener, now)) ReloadCounter(now);
        }

        /// <summary>WPF BuildInit (:531). Never the token.</summary>
        private object CounterInit()
        {
            var s = CoreSettings.Current;
            var id = FriendsHead.Identity();
            return new
            {
                type = "init", protocol = 1, sp = s.SkillPoints, locale = LocalizationManager.Instance.CurrentLanguage,
                identity = id == null ? null : new { unifiedId = id.Value.UnifiedId },
                lexicon = LocalizationManager.Instance.KeysWithPrefix("shop_").Distinct(StringComparer.Ordinal)
                    .ToDictionary(k => k, k => Loc.Get(k), StringComparer.Ordinal),
                motion = PrizeCounterRules.MotionWire(s.MotionLevel, GentleVisuals.On),
                gentle = GentleVisuals.On, reducedMotion = !UiMotionPreference.AnimationsEnabled,   // WPF :535 ClientAreaAnimation
                tab = !RainV2Gate.On && PrizeCounterRules.IsRainId(_counterTab) ? null : _counterTab,
                from = _counterFrom, wears = Array.Empty<string>(), platform = "desktop",
                companion = CompanionName.ForActiveMod(), rainV2 = RainV2Gate.On,
            };
        }
    }
}
