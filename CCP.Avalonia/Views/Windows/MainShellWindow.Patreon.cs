// WPF MainWindow.Patreon.cs: subscription gates, live tier rises and voice audio controls.
// Account and companion editors live in their owning controls.
// Community prompt transport and checkout/conversion follow-up remain unported.

using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // WPF MainWindow.Patreon.cs:1396-1406: refresh after the modal editor closes.
        internal async void BtnManagePhrases_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            await new Dialogs.CompanionPhraseEditorDialog().ShowDialogSafe(this);
            foreach (var cell in this.GetLogicalDescendants().OfType<Controls.Companion.Runtime.WorkshopTriggersCell>())
                cell.RefreshPhraseCount();
        }

        /// <summary>WPF MainWindow.Patreon.cs RefreshEntitlementVeils. Called on every navigation, on
        /// sign-in/out (UpdateQuickLoginUI), on either provider's TierChanged and on the ? box's
        /// TodayChanged (App.axaml.cs), as WPF MainWindow.xaml.cs:484 and OnPatreonTierChanged do.</summary>
        internal void RefreshEntitlementVeils() => RefreshEntitlementVeils(persist: false);

        /// <param name="persist">True only for an entitlement EVENT (tier change, day change, sign-in/out),
        /// which saves as WPF does. Startup and navigation clear the lapsed flags in memory only; see
        /// avalonia-decisions.html "Entitlement lapse: startup write deferred".</param>
        internal void RefreshEntitlementVeils(bool persist)
        {
            EnforceEntitlementLapse(persist);
            Named<Tabs.AvailableSubjectsTabView>("AvailableSubjectsTab")?.Repaint();
            RefreshTierDependentViews();
            var haptics = StudioRack?.HapticsPanel;
            var hapticsOpen = Services.Haptics.HapticsAccess.IsOpen;   // toys are free (WPF MainWindow.Patreon.cs, main f54203401b)
            if (haptics?.FindControl<Control>("HapticsContentGrid") is { } grid)
            {
                grid.Opacity = hapticsOpen ? 1.0 : 0.3;
                grid.IsHitTestVisible = hapticsOpen;
            }
            foreach (var box in new[] { "HapticsConnectionBox", "HapticsFeatureBox" })
                if (haptics?.FindControl<Control>(box) is { } b) b.IsEnabled = hapticsOpen;

            RefreshPremiumGate(Named<Control>("BambiTakeoverTab"), "BambiTakeoverGate", "takeover");
            if (haptics?.FindControl<Control>("HapticsGate") is { } hapticsGate) hapticsGate.IsVisible = !hapticsOpen;
            RefreshPremiumGate(Named<Control>("RemoteControlTab"), "RemoteControlGate", "remote");
            RefreshPremiumGate(Named<Control>("AwarenessTab"), "AwarenessGate", "awareness");
            RefreshLockdownGate();
            RefreshPremiumGate(Named<Control>("SheListeningTab"), "SheListeningGate", "voice");
            try { Named<Tabs.PlayTabView>("PlayTab")?.RefreshPlayCards(); }
            catch (System.Exception ex) { Serilog.Log.Debug("RefreshPlayCards: {E}", ex.Message); } // WPF MainWindow.PlayTab.cs:121
        }

        /// <summary>WPF MainWindow.Patreon.cs:92, over the shared Core flag pass. Skipped while the seam is
        /// unseeded: WPF never runs it before App.Patreon exists, and a head that cannot read the account
        /// must not wipe a patron's settings. Keyword and window awareness reconcile below and the lapse
        /// stops mantra chant and Takeover; OCR and the haptics mixer remain follow-ups here.</summary>
        private bool _lapseUnsaved;   // cleared in memory, not yet written by an entitlement event

        private void EnforceEntitlementLapse(bool persist)
        {
            try
            {
                if (CoreEntitlement.HasPremiumProvider is null) return;
                if (NoteDailyGiftUse(CoreSettings.Current)) CoreSettings.Save();
                var cleared = Services.EntitlementLapse.Enforce(CoreSettings.Current);
                if (MaybePostRelockRow(CoreSettings.Current)) CoreSettings.Save();
                foreach (var f in cleared) Serilog.Log.Information("Entitlement lapsed: {Feature} switched off", f);
                _lapseUnsaved |= cleared.Count > 0;
                if (persist && _lapseUnsaved)
                {
                    CoreSettings.Save();
                    _lapseUnsaved = false;
                }
                // WPF MainWindow.Patreon.cs:108 - the lapse stops the engine, not just the flag.
                if (cleared.Contains("mantra-chant")) App.MantraChant.Stop(); // WPF MainWindow.Patreon.cs:196
                if (cleared.Contains("awareness-mode")) App.WindowAwareness.Stop();
                if (cleared.Contains("takeover"))
                {
                    Autonomy.Stop();
                    TakeoverAnnouncerOverlay.CloseActive();
                    FollowTakeoverQuestTick(false);
                    UpdateAutonomyButtonState(false);
                }
                // WPF Patreon.cs:152 - the mirror case: entitlement resolving late re-arms what the
                // settings (and consent) already ask for. Start() re-checks everything itself.
                var s = CoreSettings.Current;
                if (s.AwarenessModeEnabled && s.AwarenessConsentGiven && ConditioningControlPanel.Services.WindowAwarenessService.HasEntitlement
                    && !App.WindowAwareness.IsRunning)
                    App.WindowAwareness.Start();
                // WPF MainWindow.Patreon.cs:187-189,452-456: stop on lapse, re-arm saved master on access.
                // Do not re-attach a user-disabled keyboard source on ordinary tab refreshes.
                if (!Platform.KeywordTriggerHead.Engine.IsActive || !Platform.KeywordAudioRuntime.HasAccess)
                    Platform.KeywordTriggerHead.Sync();
                // WPF MainWindow.Patreon.cs:326: the day it closes is no longer silent (proposal 4b), the closed row once.
                MaybePostLapseRow();
                if (cleared.Count == 0) return;
                SyncAwareness();
                SyncHero();
                Named<Tabs.BambiTakeoverTabView>("BambiTakeoverTab")?.SyncFromSettings();
                Named<Tabs.AwarenessTabView>("AwarenessTab")?.SyncAwarenessTabUi();
                StudioRack?.HapticsPanel.LoadHapticsSettingsToUi();   // haptics Enabled may just have been cleared
                RefreshSheListeningTab();   // "voice" / "voice-mic" may just have been cleared
            }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "EnforceEntitlementLapse failed"); }
        }

        /// <summary>WPF RefreshLockdownGate (main 605c451ae / 2b3e44432): the Lockdown page's padlock veil is never
        /// drawn over a RUNNING lockdown (Core LockdownExitRule.ShowsPremiumVeil). It covers the timer's five-click
        /// handle and the Emergency Exit, so a sign-out mid-lockdown left no way out but the clock. Repainted on
        /// every entitlement pass and on each Lockdown start/end (InitializeLockdownGreys), so it returns after.</summary>
        internal void RefreshLockdownGate()
        {
            var tab = Named<Control>("LockdownTab");
            RefreshPremiumGate(tab, "LockdownGate");
            if (tab?.FindControl<Control>("LockdownGate") is { } g
                && !ConditioningControlPanel.Services.LockdownExitRule.ShowsPremiumVeil(CoreEntitlement.HasPremium, LockdownActive))
                g.IsVisible = false;
        }

        /// <summary>WPF MainWindow.Patreon.cs:144, with its PremiumGateFx decoration (fog, padlock
        /// glow, CTA sheen) - idempotent, never touches visibility or entitlement.</summary>
        private static void RefreshPremiumGate(Control? tab, string gate, string? dailyKey = null)
        {
            if (tab?.FindControl<Control>(gate) is not { } g) return;
            g.IsVisible = !(CoreEntitlement.HasPremium || (dailyKey != null && CoreEntitlement.IsFreeToday(dailyKey)));
            global::ConditioningControlPanel.Avalonia.Controls.PremiumGateFx.Attach(g as Border);
        }

        /// <summary>WPF MainWindow.Patreon.cs:48 (main fbe161de2): every padlock opens the vault gate card for the
        /// feature behind it, found from the tab view the button sits in. Graded Intake keeps Account; a patron
        /// whose grant died here is sent to reconnect (ReconnectFromGate) instead.</summary>
        internal void BtnGateUnlock_Click(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (LockdownActive) return;   // PLAYBOOK P05: no veil on this head, so the door refuses itself
            if (TierGate.ReconnectIsTheAnswer()) { _ = ReconnectFromGate(); return; }
            var view = (sender as global::Avalonia.LogicalTree.ILogical)?.FindLogicalAncestorOfType<UserControl>(includeSelf: true)?.GetType().Name;
            if (Services.Vault.VaultOffer.KeepsAccountRoute(view)) { OpenAppSettingsSection("account"); return; }
            ShowVaultGate(Services.Vault.VaultOffer.FeatureKeyForView(view), 1);
        }

        /// <summary>WPF MainWindow.QuestsTab.cs:96 OpenPricedCard: a priced CTA sends a patron whose grant died
        /// to Reconnect instead of the price card.</summary>
        internal void OpenPricedCard(string featureKey, int tier)
        {
            if (LockdownActive) return;
            if (TierGate.ReconnectIsTheAnswer()) { _ = ReconnectFromGate(); return; }
            ShowVaultGate(featureKey, tier);
        }

        /// <summary>WPF App.xaml.cs:505, seeded into TierGate.ReconnectIsTheAnswerProvider: a patron linked server-side
        /// whose grant on this PC is gone (or dead) and who has no premium now is told to reconnect, not sold a tier.</summary>
        internal static bool ReconnectIsTheAnswerNow()
        {
            var s = CoreSettings.Current;
            var p = Platform.AccountSeed.Patreon;
            return PatreonReconnectRule.Decide(
                hasUnifiedId: !string.IsNullOrEmpty(s.UnifiedId),
                linkedServerSide: s.HasLinkedPatreon,
                desktopAuthenticated: p?.IsAuthenticated == true && !p.GrantLooksDead,
                hasPremiumNow: CoreAccount.HasPremiumAccess,
                whitelisted: p?.IsWhitelisted == true).Prominent;
        }

        /// <summary>WPF StartPatreonReconnectFromGate: open Account and link Patreon.</summary>
        internal System.Func<System.Threading.Tasks.Task>? ReconnectFromGateOverride;
        private System.Threading.Tasks.Task ReconnectFromGate()
        {
            if (ReconnectFromGateOverride != null) return ReconnectFromGateOverride();
            var account = AppSettingsPage?.SectionAccount;
            var button = account?.FindControl<Button>("BtnLinkPatreon");
            if (button == null || !button.IsEnabled) return System.Threading.Tasks.Task.CompletedTask;
            OpenAppSettingsSection("account");
            return account.ReconnectPatreonAsync();
        }

        /// <summary>WPF App.xaml.cs:512 ShowDeniedHandler: the Reconnect toast for a patron whose grant died here,
        /// otherwise the refusal with "See tiers" opening the vault card at the tier this door needs.</summary>
        internal void ShowTierDenied(TierVerdict verdict, Helpers.NotificationService toasts)
        {
            if (TierGate.ReconnectIsTheAnswer())
                toasts.Show(Loc.Get("tiergate_denied_reconnect"), Helpers.NotificationType.Warning, System.TimeSpan.FromSeconds(10),
                    Loc.Get("tiergate_reconnect_action"), () => { if (!LockdownActive) _ = ReconnectFromGate(); });
            else
                toasts.Show(verdict.Reason, Helpers.NotificationType.Warning, System.TimeSpan.FromSeconds(8),
                    Loc.Get("tiergate_see_tiers"), () => ShowVaultGate(null, verdict.Required >= Models.PatreonTier.Level2 ? 2 : 1));
        }

        /// <summary>WPF ShowVaultGate. Null feature = the tier as a whole (the TierGate toast's "See tiers").</summary>
        internal void ShowVaultGate(string? featureKey, int tier)
        {
            if (LockdownActive) return;   // P05: the toast's "See tiers" is a door too
            try { Dialogs.VaultGateDialog.ShowOffer(this, featureKey, tier, () => OpenAppSettingsSection("account")); }
            catch (System.Exception ex)
            {
                Serilog.Log.Warning(ex, "[VaultGate] card failed; falling back to Account");
                OpenAppSettingsSection("account");
            }
        }

        /// <summary>WPF MainWindow.xaml.cs:3590 + Patreon.cs:1209: the invite week's last-day card, at launch and on focus.</summary>
        private void InitializeInviteEnding()
        {
            Opened += (_, _) => MaybeShowInviteEnding();
            Activated += (_, _) => MaybeShowInviteEnding();
            Activated += (_, _) => Platform.FunnelPageMessage.OnAppActivated();   // WPF MainWindow.Patreon.cs:1275
        }

        private string? _inviteEndingPosted;

        /// <summary>WPF MaybeShowInviteEnding: once per week, through the presenter (an Inbox row when quiet).</summary>
        internal void MaybeShowInviteEnding()
        {
            try
            {
                var s = CoreSettings.Current;
                var key = Services.Vault.VaultOffer.InviteEndingOwed(s.InviteGrantUntil, System.DateTime.UtcNow,
                    ProviderSubscription.IsInviteWeekOnly(Platform.AccountSeed.Patreon, Platform.AccountSeed.SubscribeStar, s), s.SeenFeatureIntros);
                if (key == null || _inviteEndingPosted == key) return;
                _inviteEndingPosted = key;
                var until = s.InviteGrantUntil!.Value;
                Platform.StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
                {
                    Key = "intro:" + key,
                    Glyph = "⏳",
                    Title = Loc.Get("vaultgate_ending_inbox"),
                    Summary = Loc.GetF("vaultgate_ending_until", until.ToLocalTime().ToString("ddd d MMM, HH:mm")),
                    Open = () => { MarkIntroSeen(key); Dialogs.VaultGateDialog.ShowEnding(this, until, () => OpenAppSettingsSection("account")); },
                    Dismiss = () => MarkIntroSeen(key),
                });
            }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "[VaultGate] invite-ending check failed"); }
        }

        /// <summary>WPF InitializePatreonTab/InitializeSubscribeStarTab (TierChanged -> OnPatreonTierChanged /
        /// OnSubscribeStarTierChanged) and MainWindow_Loaded's launch re-check (MainWindow.xaml.cs:3559), for the
        /// celebration half; the veils already repaint from App.axaml.cs. Unsubscribed when the shell closes (P41).</summary>
        private void InitializePremiumCelebration()
        {
            Opened += (_, _) => { MaybeShowPremiumCelebration(); TierOnNoticePass(); };
            // The one-time tier-on notice follows each entitlement reading and each return to the panel (#3820).
            Platform.EntitlementRefresh.Changed += TierOnNoticePass;
            Activated += (_, _) => TierOnNoticePass();
            Closed += (_, _) => Platform.EntitlementRefresh.Changed -= TierOnNoticePass;
            var patreon = Platform.AccountSeed.Patreon;
            var substar = Platform.AccountSeed.SubscribeStar;
            var closed = false;
            System.EventHandler<Models.PatreonTier> tier = (sender, _) => global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (closed) return;   // a repaint queued before Close is dropped, as in MainShellWindow.SubscribeStar.cs (P41)
                // WPF OnPatreonTierChanged: Programs rebuilds on a tier change only, not on every navigation. A SubscribeStar
                // change rebuilds it once, in InitializeSubscribeStarDependents (WPF OnSubscribeStarTierChanged).
                if (ReferenceEquals(sender, patreon)) Named<Tabs.ProgramsTabView>("ProgramsTab")?.RefreshPrograms();
                // WPF ProgramsTab.cs:469-478: the Dashboard's Today card refreshes with every Programs refresh,
                // even while Programs is hidden (#3920).
                SettingsPage?.RefreshProgramTodayCard();
                RefreshTierDependentViews();
                RefreshProfileBubble();
                MaybeShowPremiumCelebration();
                TierOnNoticePass();
            });
            // WPF MainWindow.Patreon.cs:1363-1377: a live rise's fanfare, and a focus refresh when the user clicks
            // back in. Both ride the head's one tier path (AccountSeed.ApplyTierReading: rise AND lower).
            Platform.AccountSeed.TierRaised += OnEntitlementTierRaised;
            void Focused(object? sender, System.EventArgs e) => _ = Platform.AccountSeed.OnAppFocusedAsync();
            Activated += Focused;
            // WPF OnGraceClosed: the veils repaint from App.axaml.cs; Programs gate on the same entitlement at build time.
            void GraceClosed()
            {
                Named<Tabs.ProgramsTabView>("ProgramsTab")?.RefreshPrograms();
                SettingsPage?.RefreshProgramTodayCard();
                RefreshProfileBubble();   // WPF OnGraceClosed (MainWindow.Patreon.cs:1206)
            }
            Platform.AccountSeed.GraceClosed += GraceClosed;
            if (patreon != null) patreon.TierChanged += tier;
            if (substar != null) substar.TierChanged += tier;
            Closed += (_, _) =>
            {
                closed = true;
                Activated -= Focused;
                Platform.AccountSeed.TierRaised -= OnEntitlementTierRaised;
                Platform.AccountSeed.GraceClosed -= GraceClosed;
                if (patreon != null) patreon.TierChanged -= tier;
                if (substar != null) substar.TierChanged -= tier;
            };
        }

        /// <summary>WPF MaybeShowPremiumCelebration (MainWindow.Patreon.cs:1140): the one-time card per tier, re-read from
        /// the combined entitlement, never for an invite week, through the presenter (an Inbox row when quiet). The seen-flag
        /// is spent at open time. A live rise bypasses the launch quiet window when the user is free.</summary>
        internal void MaybeShowPremiumCelebration()
        {
            var onRise = _pendingRiseTier > 0;
            _pendingRiseTier = 0;
            try
            {
                // Switched off by the owner (2026-11-07, WPF Patreon.cs:1249): no celebration card, big or small.
                // The tier is still recorded (TierOnNoticePass), so the small card never fires late.
                if (!Platform.PopupSwitches.TierCelebration) { TierOnNoticePass(mayShow: false); return; }
                if (!CoreAccount.HasPremiumAccess) return;
                var s = CoreSettings.Current;
                if (ProviderSubscription.IsInviteWeekOnly(Platform.AccountSeed.Patreon, Platform.AccountSeed.SubscribeStar, s)) return;
                var tier = CoreAccount.HasLabAccess ? 2 : 1;
                if (!TierCelebration.IsOwed(s.SeenFeatureIntros, tier, onRise)) return;
                var key = TierCelebration.KeyFor(tier)!;
                if (onRise && CanCelebrateNow())
                {
                    FeatureIntroPopup.ShowCelebrationIfFirstTime(this, key);
                    return;
                }
                var item = new Services.Startup.InboxItem
                {
                    Key = "intro:" + key,
                    Glyph = "💖",
                    Title = Loc.Get(tier >= 2 ? "premium_celebration_inbox_t2" : "premium_celebration_inbox_t1"),
                    Summary = Loc.Get("premium_celebration_inbox_summary"),
                    Open = () => FeatureIntroPopup.ShowCelebrationIfFirstTime(this, key),
                };
                Platform.StartupLadder.PresentOrInbox(item);   // WPF Patreon.cs: a busy rise goes through the presenter too
            }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "Premium celebration hook failed"); }
        }

        private int _pendingRiseTier;

        private bool CanCelebrateNow() => IsVisible && WindowState != WindowState.Minimized
            && !CoreSession.IsSessionRunning && !CoreTutorial.IsActive
            && !Games.GameWindow.IsAnyOpen() && !OwnedWindows.Any(w => w.IsVisible)
            && !Platform.AppUpdater.IsUpdateDialogActive && !IsStartupDialogShowing;

        private void OnEntitlementTierRaised(int tier)
        {
            _pendingRiseTier = tier;
            var colour = tier >= 2 ? global::Avalonia.Media.Color.FromRgb(0x5E, 0xE6, 0xFF)
                : global::Avalonia.Media.Color.FromRgb(0xFF, 0xD7, 0x00);
            RefreshProfileBubble();
            if (Named<Button>("BtnProfileBubble") is { } bubble) _profileBubbleBurst.Fire(bubble, 60, color: colour);
            FlashProfileBubbleGlow(colour);
        }

        internal void RefreshTierDependentViews()
        {
            var takeover = Named<Tabs.BambiTakeoverTabView>("BambiTakeoverTab");
            var unlocked = CoreEntitlement.HasPremium || CoreEntitlement.IsFreeToday("takeover");
            if (takeover?.FindControl<Control>("AutonomyLocked") is { } locked) locked.IsVisible = !unlocked;
            if (takeover?.FindControl<Control>("AutonomyUnlocked") is { } open) open.IsVisible = unlocked;
            // WPF UiUpdates.cs:937-938 re-sets the two lock texts; here the axaml binds the same keys ({loc:Str}/IconLabel Key).
        }

        private readonly VoiceAudioState _voiceAudio = new();

        internal void ApplyVoiceMute(bool muted)
        {
            _voiceAudio.ApplyMute(CoreSettings.Current, muted);
            SyncHero();
            foreach (var behavior in this.GetLogicalDescendants().OfType<Controls.Companion.Runtime.WorkshopBehaviorCell>())
                behavior.SyncFromSettings();
            _avatarTubeWindow?.SetWindowMute(false);
            if (muted) _avatarTubeWindow?.StopVoiceLineAudio();
            SetMasterVolume(CoreSettings.Current.MasterVolume);
        }

        internal void AdjustMasterVolume(int delta) => SetMasterVolume(CoreSettings.Current.MasterVolume + delta);

        internal void SetMasterVolume(int value)
        {
            CoreSettings.Current.MasterVolume = value; // AppSettings.MasterVolume clamps to 0..100.
            Controls.AppSettings.AudioSettingsBinder.RaiseChanged();
            Platform.LayeredAudio.Instance?.SetMasterVolumeLive();
            Overlays.MandatoryVideoOverlay.Instance.UpdateVolume();
            CoreSettings.Save();
        }

        internal void TierOnNoticePass() => TierOnNoticePass(mayShow: true);

        internal void TierOnNoticePass(bool mayShow)
        {
            try
            {
                var s = CoreSettings.Current;
                var current = Platform.EntitlementRefresh.EffectiveTier;
                var verdict = Services.TierOnNotice.Decide(s.LastKnownEffectiveTier, current,
                    Platform.EntitlementRefresh.ServerAnsweredThisRun);
                if (verdict.Record != s.LastKnownEffectiveTier)
                {
                    // The first cached reading joins the next user save or desktop exit. Startup must not write migrations.
                    var hadReading = s.LastKnownEffectiveTier.HasValue;
                    s.LastKnownEffectiveTier = verdict.Record;
                    if (hadReading) CoreSettings.Save();
                }
                if (!verdict.Owed || !mayShow || !Platform.PopupSwitches.TierCelebration) return;
                if (TierCelebration.IsOwed(s.SeenFeatureIntros, current, onRise: true)) return;
                var title = Loc.Get(Services.TierOnNotice.TitleKey(current));
                var body = Loc.Get("tier_on_body");
                Platform.StartupLadder.PresentOrInbox(new Services.Startup.InboxItem
                {
                    Key = "tier-on:" + current, IconKey = "Heart", Title = title, Summary = body,
                    Open = () => Dialogs.InboxNoticeCard.Present(this, title, body, null, null),
                });
            }
            catch (System.Exception ex) { Serilog.Log.Debug(ex, "Tier-on notice failed"); }
        }

        private static void MarkIntroSeen(string key)
        {
            var s = CoreSettings.Current;
            if (s.SeenFeatureIntros.Contains(key)) return;
            s.SeenFeatureIntros.Add(key);
            CoreSettings.Save();
        }

    }
}
