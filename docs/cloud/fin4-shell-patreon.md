# fin4-shell-patreon

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; work branch `avalonia-port/cloud-fin4-shell-patreon`.
- Added shared live-tier application and focus throttle; successful V2 profile/heartbeat responses are dispatched by the head. Save grace before rise observers and provider notification; never apply a lower reading as a revocation or renew equal-tier grace. Reject offline, unsigned and stale-account readings.
- Restore immediate per-tier rise celebration and gold/cyan bubble fanfare using the existing popup and finite animations. No new surface is introduced. Busy users retain the Inbox path; launch celebrations retain their prior route.
- Restore voice mute/unmute with remembered volume, whisper enable and avatar flags, current voice clip stop, and audio/companion/menu mirrors. Wire louder/quieter through the existing voice host at WPF’s 15-point steps, clamped to 0–100; settings rules are shared Core VoiceAudioState.
- Repaint effective AI permissions without clearing the saved choice; rebuild Programs and repaint takeover autonomy lock on tier events and entitlement refresh. Existing Programs implementation already defers hidden rebuilds.
- Replaced the repeated historical ledger prose with current evidence and an honest stub/Missing list; added numbered tester steps and focused tests.
- No trunk code imported. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; implementation adapted the frozen WPF reference at the base SHA and reused Core/head code.

## WPF gap inventory and decisions

Compared `MainWindow.Patreon.cs` (including initialization, account UI, gates, celebration, companion/AI/quick-controls/prompts) and `MainWindow.UiUpdates.cs` against the actual head. Ledger absence claims were stale in several places:

- Already present: provider lifecycle, tier/Discord/link/reconnect/visit UI in AccountSettingsSection; premium gate/lapse; launch celebration; profile bubble refresh from App and Login; companion detach/customize/switch/phrase editor and drawer persistence; idle/bubble/awareness/trigger sliders and controls; provider selection, local setup wizard, Ollama/OpenAI tests and sampler; chat memory/effect permissions/haptic cap; whisper/voice-line flags; Discord sharing/privacy handlers in ProfilePrivacyPanel.
- Closed here: EntitlementTierSync focus/read/rise host, immediate celebration, profile tier fanfare, Programs entitlement refresh, takeover autonomy lock. AI effect permission uses the current WPF effective gate without clearing the saved preference; the old header's force-clear claim is obsolete.
- Still missing: complete community prompt service and browse/install/import/export/refresh dialogs, including installed-name display and personality assignment. The WPF service alone is 500 lines; its view hosts, storage pickers, failure paths and focused tests exceed the remaining budget once this unit's live tier fixes and tests are included. Deactivation already exists in MakeHerYoursView.
- Still missing: embedded browser host for pause/mute/suspend/resume. Pause cannot truthfully work without the browser host. Voice-command integration is now wired through the existing dispatcher.
- Newer WPF behavior also remains: TierOnNotice's small repeat-rise notice; conversion readings/hooks, active Patreon confirmation re-stamp, checkout-watch any-window refresh and explicit just-subscribed checks. These are not claimed wired by this partial live host.
- Assumption: keep the row stub and prioritize the visible live-tier and stale entitlement fixes within the 1,000-added-line cap. No dead-WPF/P122 claim. No new surface; FeatureIntroPopup is reused unchanged. No real account or desktop is used by tests.

## Verification limits

- `dotnet build -c Release`: CCP.Core, CCP.Avalonia and every Linux test project referenced by `.github/workflows/build.yml` passed (exit 0, zero errors). The WPF test project also compiled with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (exit 0, zero errors); WPF/Win32 was not executed.
- Final exact head/test compilation: `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false` passed, 74 warnings, zero errors. One preceding parallel MSBuild attempt lost a child worker (MSB4166); the single-worker retry succeeded.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0, 3,958 passed, 5 skipped, 3,963 total. The existing five Vosk cases lack their model. Earlier concurrent-build runs exposed frame-budget/media timing failures; the idle runs passed without test changes.
- New focused Core tier/wire tests: 4 passed before the voice addition. The wire regression was also run with both tier callbacks temporarily removed: expected exit 1, expected readings versus an empty collection. Restoring the callbacks passes in the full suite. VoiceAudioState's repeated mute/restore/default-volume test passes in that final suite.
- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~PremiumCelebrationTests|FullyQualifiedName~AccountSeedTests|FullyQualifiedName~PremiumGatesTests|FullyQualifiedName~AiPermissionsLiveFeedTests'`: exit 1, 36 passed, 25 failed, 61 total. Every failure reports `Could not create glyphTypeface` (Inter, Consolas or Segoe UI Black), the documented cloud font limitation. New shell assertions cannot be claimed executed successfully here; their assembly compiled with zero errors. Supervisor/GitHub must run them with fonts.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed. `git diff --check`: exit 0. Exactly one ledger row remains for shell-patreon, with numbered tester steps 18–20.
- `.github/scripts/test-plan-coverage.sh` does not exist on this base (attempt: exit 127); no coverage script was added or weakened.
- No Windows runtime, real OAuth/checkout, real desktop/profile or render-all run. No PR opened. The row stays stub because the remaining host-dependent behavior is not implemented.
