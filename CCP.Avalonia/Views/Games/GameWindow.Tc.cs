// PORTED from ConditioningControlPanel/Services/Tc/TcHostService.cs (main 21899cde85, 876b22252d, f1b7924f4d, 15d5a5e50a):
// Terms & Conditioning (Assets/web/fuzz/index.html, wire fuzz/CONTRACT.md + Contract v2). One tile for everyone: a
// subscriber gets the full game, a free account the small one (Core TcRules), so the tile is never locked. Every page
// frame passes Core TcFrameGate first. Every ready gets a fresh init (the pause card's Main menu reloads the page and
// waits for init with no timeout). Hard exits (panic, title-bar X, a dead page, the watchdog) close the window at once;
// the page's own exit ends on its exit-done or at 800 ms. Escape is its pause (GameWindow.Escape.cs); panic closes it
// with every other game (PanicSurfaces "games"). Not on this head yet, so the init does not offer them: pictures and
// the Sparkles medal (s20-tc-media), toy pulses and live settings (s20-tc-feel) and the save carry (s20-tc-save).
// Their frames are refused here with a log line. The Llama Expo download is GameWindow.Tc.Expo.cs.
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Games
{
    internal sealed partial class GameWindow
    {
        /// <summary>Launcher id, panic id (WPF TcHostService.GameId).</summary>
        internal const string TcId = "tc";

        /// <summary>WPF TcHostService.TreePresent: the tile opens only when the game's page is on disk.</summary>
        internal static Func<bool> TcTreePresent { get; set; } = () =>
        {
            try { return File.Exists(Path.Combine(AppContext.BaseDirectory, "Resources", "web", "fuzz", "index.html")); }
            catch (Exception ex) { Diag.Swallowed(ex); return false; }
        };

        /// <summary>The Games gate: never a tier refusal, only a missing tree (WPF Launch logs and opens nothing).</summary>
        internal static bool TcGate()
        {
            if (TcTreePresent()) return true;
            Log.Warning("TcHost: no game tree on disk, nothing to open");
            return false;
        }

        /// <summary>WPF ReadEdition: the app's Basic bar with the invite week rule (TcRules.IsSubscriber). Fails to small.</summary>
        internal static Func<string> TcReadEdition { get; set; } = () =>
        {
            try
            {
                return TcRules.EditionFor(TcRules.IsSubscriber(CoreAccount.HasPremiumAccess,
                    ProviderSubscription.IsInviteWeekOnly(AccountSeed.Patreon, AccountSeed.SubscribeStar, CoreSettings.Current)));
            }
            catch (Exception ex) { Diag.Swallowed(ex); return TcRules.EditionSmall; }
        };

        /// <summary>Test seam for the page's allow-listed links (Core TcFrameGate.OpenUrl already checked them).</summary>
        internal static Func<string, bool> TcOpenUrl { get; set; } = ExternalOpener.Open;

        internal bool IsTc => string.Equals(Spec.Id, TcId, StringComparison.OrdinalIgnoreCase);

        private bool _tcOpen, _tcFocusHeld;
        private string _tcEdition = TcRules.EditionSmall;
        private DateTime _tcLastAnyUtc, _tcLastBeatUtc;
        private DateTime? _tcCloseAskedUtc;
        private DispatcherTimer? _tcWatch, _tcCloseClock;

        /// <summary>The edition this window was opened with (read once at launch, as WPF).</summary>
        internal string TcEdition => _tcEdition;
        internal bool TcWatchRunning => _tcWatch?.IsEnabled == true;
        internal bool TcCloseClockRunning => _tcCloseClock?.IsEnabled == true;

        /// <summary>WPF Launch after the window exists: the edition, the clocks, the focus hold.</summary>
        private void OpenTc()
        {
            if (_tcOpen) return;
            _tcOpen = true;
            _tcEdition = TcReadEdition();
            TcExpoOnLaunch();
            _tcFocusHeld = false;
            _tcCloseAskedUtc = null;
            _tcLastAnyUtc = _tcLastBeatUtc = DateTime.UtcNow;
            Activated += OnTcActivated;
            Closing += (_, _) => CloseTc();
            Closed += (_, _) => CloseTc();
            _tcWatch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _tcWatch.Tick += (_, _) => CheckTcWatch(DateTime.UtcNow);
            _tcWatch.Start();
            Log.Information("TcHost: launched ({Edition})", _tcEdition);
        }

        /// <summary>WPF Close: the hard funnel (panic, title-bar X, dead page, watchdog). Idempotent and immediate:
        /// the clocks stop first, then the page gets suspend(panic) so it silences audio and fx on this frame.</summary>
        private void CloseTc()
        {
            if (!_tcOpen) return;
            _tcOpen = false;
            try { _tcWatch?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
            try { _tcCloseClock?.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _tcWatch = _tcCloseClock = null;
            CloseTcExpo();   // the partial stays for the next press
            _tcCloseAskedUtc = null;
            Activated -= OnTcActivated;
            // Post() drops frames once closing; this one goes straight to the page, as WPF posts it before Dispose.
            try
            {
                var json = JsonConvert.SerializeObject(TcHostRules.Suspend(true, TcHostRules.ReasonPanic));
                Posted?.Invoke(json);
                _ = Web.PostJsonAsync(json);
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
            Log.Information("TcHost: closed");
        }

        /// <summary>WPF PostInit: Contract v2 caps from Core TcRules, the v1 boot frame from Core TcHostRules.</summary>
        internal void PostTcInit()
        {
            try
            {
                var s = CoreSettings.Current;
                var caps = TcRules.Caps(_tcEdition, saveSlot: false, TcExpoCap());   // no save carry on this head yet (s20-tc-save)
                Post(TcHostRules.InitFrame(LocalizationManager.Instance.CurrentLanguage, s.MotionLevel, GentleVisuals.On, CoreMods.ActiveModId, caps,
                    picturesOn: false, sparkles: false, toys: false, toysConnected: false, ownMedia: false));
            }
            catch (Exception ex) { Log.Warning("Tc: init post failed: {E}", ex.Message); }
            try { Web.Focus(); } catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>WPF OnPageMessage. Claims every frame: the shell's generic exit/close never applies to this page.</summary>
        private bool HandleTc(JObject o)
        {
            if (_closing) return true;
            if (!_tcOpen) OpenTc();   // a ready that beat Opened still gets its init
            _tcLastAnyUtc = DateTime.UtcNow;
            var gate = TcFrameGate.Check(o);
            if (!gate.Accepted || gate.Frame is not { } f)
            {
                Log.Debug("Tc: page frame refused ({Type}: {Why})", (string?)o["type"], gate.Refusal);
                return true;
            }
            try
            {
                switch (f.Type)
                {
                    case "ready":
                        // Every ready, the first and each Main-menu reload, gets a fresh init (15d5a5e50a).
                        IsReady = true;
                        _tcLastBeatUtc = _tcLastAnyUtc;
                        // WPF OnPageReady: with no Expo size yet, wait for the manifest read, at most 1.5 s from open.
                        if (TcExpoInitHold() is var hold && hold > TimeSpan.Zero && _tcExpoSizeRead is { } read)
                        {
                            _ = Task.WhenAny(read, Task.Delay(hold)).ContinueWith(_ => Dispatcher.UIThread.Post(() =>
                            {
                                if (_tcOpen && !_closing) PostTcInit();
                            }), TaskScheduler.Default);
                            break;
                        }
                        PostTcInit();
                        break;
                    case "heartbeat":
                        _tcLastBeatUtc = _tcLastAnyUtc;
                        break;
                    case "boot-error":
                        Log.Warning("Tc: page boot-error: {Msg}", f.Save);
                        Close();
                        break;
                    case "exit":
                        // The page's own Leave: it winds itself down and says exit-done. Escape never sends this.
                        _shellExiting = true;
                        StartTcCloseClock();
                        break;
                    case "exit-done":
                        if (_tcCloseAskedUtc != null) Close();
                        break;
                    case "open-url":
                        if (!TcOpenUrl(f.Url!)) Log.Debug("Tc: open-url not opened");
                        break;
                    case "expo-fetch":
                        OnTcExpoFetch();   // the page sends it only from a press
                        break;
                    case "expo-cancel":
                        StopTcExpo();
                        break;
                    case "nudge":
                        OpenTcTierCard();   // desktop: every nudge is the tier compare card (Contract v2)
                        break;
                    default:
                        Log.Debug("Tc: {Type} not handled by this head yet", f.Type);
                        break;
                }
            }
            catch (Exception ex) { Log.Warning("Tc: message handler threw ({Type}): {E}", f.Type, ex.Message); }
            return true;
        }

        /// <summary>WPF RequestClose: <c>close {reason}</c>, then the hard close on exit-done or at 800 ms.</summary>
        internal void RequestTcClose(string reason)
        {
            if (!_tcOpen || _closing) return;
            Post(TcHostRules.Close(reason));
            StartTcCloseClock();
        }

        /// <summary>WPF OpenTierCard: the run holds (suspend focus) until this window is active again. A patron whose
        /// grant died on this PC gets Reconnect instead. Lockdown refuses the door before the run is held (PLAYBOOK P05,
        /// as every other door on this head).</summary>
        private void OpenTcTierCard()
        {
            if (MainShellWindow.LockdownActive) { Log.Information("Tc: tier card refused during Lockdown"); return; }
            if (MainShellWindow.Current is not { } shell) return;
            Post(TcHostRules.Suspend(true, TcHostRules.ReasonFocus));
            _tcFocusHeld = true;
            shell.OpenPricedCard(null, 1);
        }

        private void OnTcActivated(object? sender, EventArgs e)
        {
            if (!_tcFocusHeld || !_tcOpen || _closing) return;
            _tcFocusHeld = false;
            Post(TcHostRules.Suspend(false, TcHostRules.ReasonFocus));
        }

        private void StartTcCloseClock()
        {
            if (!_tcOpen || _tcCloseAskedUtc != null) return;
            _tcCloseAskedUtc = DateTime.UtcNow;
            _tcCloseClock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _tcCloseClock.Tick += (_, _) => CheckTcCloseClock(DateTime.UtcNow);
            _tcCloseClock.Start();
        }

        /// <summary>One close-clock tick: true when it closed the window (TcHostRules.CloseNow at 800 ms).</summary>
        internal bool CheckTcCloseClock(DateTime nowUtc)
        {
            if (!_tcOpen || _closing || _tcCloseAskedUtc is not { } asked) return false;
            if (!TcHostRules.CloseNow(false, nowUtc - asked)) return false;
            Close();
            return true;
        }

        /// <summary>One 5 s watchdog tick (WPF StartWatch): 45 s with nothing before ready, 90 s without a heartbeat after.</summary>
        internal string CheckTcWatch(DateTime nowUtc)
        {
            if (!_tcOpen || _closing) return "idle";
            if (TcHostRules.Watch(IsReady, nowUtc - _tcLastAnyUtc, nowUtc - _tcLastBeatUtc) == TcWatchVerdict.Keep) return "ok";
            Log.Warning("Tc: page silent (ready={Ready}) - closing", IsReady);
            Close();
            return "closed";
        }
    }
}
