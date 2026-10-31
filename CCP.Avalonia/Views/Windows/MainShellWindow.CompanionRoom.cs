// Ported write surface: WPF MainWindow/MainWindow.CompanionRoom.cs.
// Awareness refreshes follow the controls after the v2 tab reparenting.
// Part B: full v2 observer integration. BYO key storage is in Platform/CompanionApiKey.cs.

using System;
using System.Linq;
using Avalonia.LogicalTree;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Awareness;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>Seeding guard for the room's writes. See the header for why it is not
        /// _isLoading.</summary>
        private bool _roomLoading;

        /// <summary>Asked at most once per process - a declined dialog must not become a nag.</summary>
        private bool _awarenessV2ConsentAsked;

        // =====================================================================================
        //  quick actions (Z1)
        // =====================================================================================

        /// <summary>
        /// Show/hide the avatar tube. The old ChkAvatarEnabled_Changed body, with the checkbox
        /// read replaced by the caller's value.
        /// </summary>
        internal void SetAvatarEnabled(bool enabled)
        {
            if (_roomLoading) return;

            CoreSettings.Current.AvatarEnabled = enabled;
            if (enabled) ShowAvatarTube();
            else HideAvatarTube();
            CoreSettings.Save();
            // Every on/off button reads the hero's copy of the switch, wherever it was flipped from.
            SyncHero();
        }

        /// <summary>WPF CompanionRoom?.SyncHero(): re-read every seated hero card.</summary>
        internal void SyncHero()
        {
            foreach (var hero in this.GetLogicalDescendants().OfType<Controls.Companion.CompanionHeroCard>())
                hero.ViewModel?.Sync();
        }

        /// <summary>
        /// Mute/unmute her voice - the old ChkMuteAvatar_Changed body.
        ///
        /// <para>The setting is written and persisted; the live
        /// <c>AvatarTubeWindow.SetMuteAvatar</c> call is dropped because that method is in the
        /// tube's Speech.cs partial, which did not cross. Nothing on this head speaks yet, so the
        /// stored value is the whole of the behaviour there is to have - and it is the value the
        /// speech pipeline will read the moment it lands.</para>
        /// </summary>
        internal void SetAvatarMuted(bool muted)
        {
            if (_roomLoading) return;
            CoreSettings.Current.AvatarMuted = muted;
            CoreSettings.Save();
        }

        // =====================================================================================
        //  personality (Z4) - WPF MainWindow.CompanionRoom.cs:121-178; async where WPF blocked.
        // =====================================================================================

        internal async System.Threading.Tasks.Task<bool> SetSlutModeAsync(bool enabled)
        {
            var s = CoreSettings.Current;
            if (_roomLoading) return s.SlutModeEnabled;
            if (s.SlutModeEnabled == enabled) return enabled;

            if (enabled
                && ExplicitContentGate.RequiresAcknowledgement(PersonalityService.Shared.GetActivePreset(), slutModeOn: true)
                && !ExplicitContentGate.IsAlreadyAcknowledged(s.CompanionPrompt))
            {
                if (!await new ExplicitContentAcknowledgementDialog().ShowDialogSafe<bool>(this)) return s.SlutModeEnabled;
                if (s.CompanionPrompt != null) ExplicitContentGate.MarkAcknowledged(s.CompanionPrompt);
            }

            s.SlutModeEnabled = enabled;
            CoreSettings.Save();
            return enabled;
        }

        internal async System.Threading.Tasks.Task<bool> ActivatePersonalityPresetAsync(string? presetId)
        {
            if (string.IsNullOrEmpty(presetId)) return false;
            var preset = PersonalityService.Shared.GetPresetById(presetId);
            if (preset == null) return false;

            var s = CoreSettings.Current;
            if (ExplicitContentGate.RequiresAcknowledgement(preset, s.SlutModeEnabled)
                && !ExplicitContentGate.IsAlreadyAcknowledged(s.CompanionPrompt))
            {
                if (!await new ExplicitContentAcknowledgementDialog().ShowDialogSafe<bool>(this)) return false;
                if (s.CompanionPrompt != null)
                {
                    ExplicitContentGate.MarkAcknowledged(s.CompanionPrompt);
                    CoreSettings.Save();
                }
            }

            if (!PersonalityService.Shared.SetActivePreset(presetId)) return false;
            Log.Information("Companion room: personality preset switched to {Name}",
                CoreMods.Service?.GetPersonalityDisplayName(preset.Name) ?? preset.Name);
            return true;
        }

        /// <summary>WPF BtnCustomizeCompanion_Click (MainWindow.Patreon.cs:1298): Z4's trait/prompt/fork links.</summary>
        internal async System.Threading.Tasks.Task OpenCompanionPromptEditorAsync()
        {
            CoreBark.NotifyUiAction("customize_companion");
            await new CompanionPromptEditorDialog().ShowDialogSafe(this);
        }

        // =====================================================================================
        //  awareness (Z5)
        // =====================================================================================

        /// <summary>
        /// The awareness capability, driven by Z5's dial.
        ///
        /// <para><b>Consent is not silent.</b> Turning it on raises the one-time plain-language
        /// dialog; declining leaves awareness OFF and returns false, and the caller re-reads so
        /// the dial snaps back. This is the only place AwarenessModeEnabled and
        /// AwarenessConsentGiven are written outside settings load, which is what keeps "every
        /// entry point is gated" a fact.</para>
        ///
        /// <para>Returns whether awareness is enabled after the call. async, because the consent
        /// dialog is.</para>
        /// </summary>
        internal async System.Threading.Tasks.Task<bool> SetAwarenessEnabled(bool enabled)
        {
            var s = CoreSettings.Current;
            if (_roomLoading) return s.AwarenessModeEnabled;

            // Turning it OFF is deliberately never gated: whatever her account is doing, the
            // switch that closes her eyes is on screen and it works.
            //
            // The entitlement bar, ON edge only (#1047), restored from MainWindow.CompanionRoom.cs:210
            // now that TierGate is in Core. Asked BEFORE the consent dialog, as it is there and for
            // the reason given there: it is rude to walk someone through a privacy explanation for a
            // door that will not open. Unseeded on this head, so it currently denies - which is the
            // WPF behaviour with no Patreon service, not a new refusal.
            if (enabled && !Services.TierGate.DemandPremium(Loc.Get("tab_awareness"), "awareness"))
            {
                SyncAwareness();
                return false;
            }

            if (enabled && !await AwarenessConsentDialog.EnsureConsentAsync(this, s))
            {
                // WPF MainWindow.CompanionRoom.cs:223-228: restore the unchanged dial.
                SyncAwareness();
                return false;
            }

            s.AwarenessModeEnabled = enabled;
            s.AwarenessConsentGiven = enabled;
            CoreSettings.Save();

            // Opening her eyes lifts any running "pause for an hour": the user just said yes on
            // purpose.
            if (enabled) AwarenessPause.Resume();

            // WPF MainWindow.CompanionRoom.cs:236-245: the dial is the on/off call site.
            if (enabled) App.WindowAwareness.Start(); else App.WindowAwareness.Stop();

            Log.Information("Awareness Mode {State} via the awareness dial", enabled ? "enabled" : "disabled");
            SyncHero();
            SyncAwareness(); // WPF MainWindow.CompanionRoom.cs:254-255
            return enabled;
        }

        /// <summary>
        /// Raises the v2 consent dialog for an UPGRADER: someone whose awareness was already on
        /// before this version, so the dial they would otherwise have to touch is already in the
        /// "on" position and <see cref="AwarenessConsentDialog.EnsureConsentAsync"/> would never
        /// run.
        ///
        /// <para>Declining leaves them on the legacy pipeline they already consented to, and is
        /// not asked again this session. Turning the dial off remains the way to say no
        /// permanently.</para>
        /// </summary>
        internal async void EnsureAwarenessV2Consent() => await EnsureAwarenessV2ConsentAsync();

        internal async System.Threading.Tasks.Task EnsureAwarenessV2ConsentAsync()
        {
            if (_awarenessV2ConsentAsked || _roomLoading) return;

            var s = CoreSettings.Current;
            if (!s.UseAwarenessV2) return;                 // kill switch down: v2 has nothing to ask
            if (s.AwarenessConsentShownV2) return;         // already accepted
            if (!s.AwarenessModeEnabled || !s.AwarenessConsentGiven) return;  // off: the dial asks

            _awarenessV2ConsentAsked = true;

            if (!await AwarenessConsentDialog.EnsureConsentAsync(this, s))
            {
                Log.Information("Awareness: v2 consent declined by an upgrader, staying on the legacy pipeline");
                return;
            }

            // WPF MainWindow.CompanionRoom.cs:64-74: engage the accepted pipeline now.
            try
            {
                App.WindowAwareness.Stop();
                App.WindowAwareness.Start();
            }
            catch (Exception ex) { Log.Warning(ex, "Awareness: restart after v2 consent failed"); }
            SyncAwareness();
        }

        /// <summary>
        /// The Workshop's intensity dial: how talkative she is, not whether she watches. Writing it
        /// never opens her eyes - that stays Z5's single gated decision - so this needs no consent
        /// gate of its own.
        /// </summary>
        internal void SetAwarenessIntensity(AwarenessIntensity intensity)
        {
            if (_roomLoading) return;
            var s = CoreSettings.Current;
            if (s.AwarenessIntensity == intensity) return;

            s.AwarenessIntensity = intensity;
            // The migration flag is what stops a later start-up from overwriting this choice.
            s.AwarenessIntensityMigrated = true;
            CoreSettings.Save();

            Log.Information("Awareness intensity set to {Intensity}", intensity);
            SyncAwareness(); // WPF MainWindow.CompanionRoom.cs:277
        }

        // WPF AwarenessVm.Sync plus its Workshop re-read. Logical descendants include zones
        // adopted by the Awareness tab, unlike a lookup under the collapsed room.
        internal void SyncAwareness()
        {
            foreach (var view in this.GetLogicalDescendants().OfType<Controls.Companion.AwarenessPrivacyView>())
                view.ViewModel?.Sync();
            foreach (var cell in this.GetLogicalDescendants().OfType<Controls.Companion.Runtime.WorkshopAwarenessCell>())
                cell.SyncIntensity();
        }

        // =====================================================================================
        //  the engine room (Z7)
        // =====================================================================================

        /// <summary>
        /// The daily request limit, which used to be a bare TextBox on the AI Brain card. Same
        /// parse rules: blank or unparseable means "no limit" (0), negatives are clamped by the
        /// same test. async because Avalonia's ShowDialog is.
        /// </summary>
        internal async System.Threading.Tasks.Task PromptForDailyRequestLimit()
        {
            var s = CoreSettings.Current.CompanionPrompt;
            if (s == null) return;

            var current = s.DailyRequestLimit > 0 ? s.DailyRequestLimit.ToString() : string.Empty;
            var dialog = new InputDialog(
                Loc.Get("label_daily_request_limit"),
                Loc.Get("label_daily_request_limit_hint"),
                current);

            if (await dialog.ShowDialogSafe<bool>(this) != true) return;

            var text = (dialog.ResultText ?? string.Empty).Trim();
            s.DailyRequestLimit = int.TryParse(text, out var value) && value > 0 ? value : 0;
            CoreSettings.Save();
        }

        /// <summary>
        /// "Clear conversation" (WPF MainWindow.CompanionRoom.cs:353): drops the thread, leaves what she
        /// knows. ForgetThread, not ForgetConversation, for the reason WPF gives (pinned and Boundary facts).
        /// The bark echo is not here: this head has no bark engine (CoreBark is a doorbell).
        /// </summary>
        internal async System.Threading.Tasks.Task ClearCompanionConversationAsync()
        {
            var message = string.Join(Environment.NewLine + Environment.NewLine,
                Loc.Get("companion_engine_clear_conversation_confirm"),
                Loc.Get("companion_engine_clear_conversation_confirm_body"),
                Loc.Get("companion_engine_clear_conversation_confirm_warn"));
            if (!await MessageDialog.ConfirmAsync(this, Loc.Get("companion_engine_clear_conversation"), message,
                    defaultToCancel: true)) return;
            ClearCompanionConversationConfirmed();
        }

        /// <summary>The confirmed half, separate so a headless test drives exactly what the button does.</summary>
        internal void ClearCompanionConversationConfirmed()
        {
            try
            {
                App.Brain?.ForgetThread();
                // Backstop for the no-brain case: the legacy local transcript must not survive the button.
                if (App.Brain == null)
                    (App.Ai as ConditioningControlPanel.Services.AIService.AiServiceStrategy)?.ClearLocalHistory();
                _avatarTubeWindow?.ChatHistory.Clear();
                Log.Information("Companion conversation cleared from the Engine Room");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to clear companion conversation");
            }
        }
    }
}
