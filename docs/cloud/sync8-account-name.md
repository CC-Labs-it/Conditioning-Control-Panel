# sync8-account-name

- Port `5f666d8ef645586a6339a71eb27409684741d0b5`: explicit server `needs_display_name` answers win both ways; only an absent flag falls back to an empty name. Anonymous installs never ask.
- Carry the nullable answer through Avalonia provider/account and device-code login results. Await the name prompt before subsequent post-login notices; reuse CoreAccount's rename and the existing dialog, with required-mode validation and retry on rejection.
- Keep merge-base build repairs in the separate first commit `compat-scratch: repair Core merge compile dependencies`; integration should drop that commit when the compat lane lands.
- Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Behaviour and supporting pure rules adapted from WPF already reachable on the pinned base.

5f666d8ef = ported (`CCP.Core/Services/Account/NeedsDisplayNameRule.cs:28`, `CCP.Avalonia/Views/Windows/MainShellWindow.DisplayNameAsk.cs:14`, login result at `CCP.Avalonia/Views/Dialogs/LoginDialog.axaml.cs:145`; tests: `NeedsDisplayNameRuleTests.Server_answer_wins_both_ways`, `DisplayNameAskTests.Login_preserves_nullable_server_answer`, `DisplayNameAskTests.Required_mode_validates_and_keeps_a_refused_save_open`).

## Assumptions

- This unit covers the changed post-sign-in decision; the earlier WPF startup modal-ladder onboarding flow is outside this one-commit port. The base Avalonia ladder only supports passive surfaces.
- Reuse the dialog's Cancel button and Escape as its close affordances rather than importing the WPF corner-close chrome (`ponytail:` comment in code). Closing leaves the account signed in, as WPF required mode does.
- Reuse the rename seam's server error text on rejection rather than adding a second HTTP availability client.
- The display-name prompt has no effects, audio or screen ownership; it introduces no safety surface to register in PanicSurfaces.

## Verification limits

- Verified clean checkout at required base `28ab74eed692067fed4f2051791b3a6d0976b1dc` before work; local branch name was `work` as expected.
- .NET SDK 10.0.100 installed from the official builds feed after the default channel endpoint and blob feed returned 403.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~NeedsDisplayNameRuleTests`: before fix **3 failed / 8 passed**, using the WPF pre-fix predicate; after fix **11 passed / 0 failed**. The three failures are explicit false with null, empty and whitespace names. No tests weakened or skipped by this lane.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: **3964 passed / 0 failed / 5 existing Vosk skips**. Vosk model unavailable in this environment.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- Release builds: Core, Avalonia, CCP.Avalonia.Tests and every Linux test project named by build.yml passed with zero errors. Existing warnings remain. Projects: Core.Tests, Core.Settings.Tests, Core.Content.Tests; Avalonia.Tests, Avalonia.CatalogueStartup.Tests, Avalonia.Consent.Tests, Avalonia.GeneralSettings.Tests, Avalonia.Language.Tests, Avalonia.Migration.Tests, Avalonia.Report.Tests, Avalonia.Scheduler.Tests, Avalonia.Snapshot.Tests.
- Avalonia runtime tests were not run, following this job's explicit font-creation environment limit. Pending supervisor command: `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~DisplayNameAskTests`. Their runtime red/green proof remains unverified; removing flag propagation or required-mode validation should fail those assertions.
- Windows runtime behaviour cannot be executed on Linux. WPF test cross-build result recorded below.
- WPF cross-build: `dotnet build Tests/ConditioningControlPanel.Tests -c Release --no-restore -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, **0 errors**. Frozen WPF and Windows code compiled but were not run. Temporary compatibility rules use distinct `Compat*` type names to avoid collisions with the frozen reference.
- One later full Core run had an unrelated intermittent failure in `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` (expected Loops, received Clips); its isolated rerun passed (1/1). The final full-suite rerun also passed: 3964 passed, 0 failed, 5 existing Vosk skips (exit 0). No DTRH code or tests changed.

## Changed files

- `CCP.Avalonia/Views/Dialogs/DisplayNameDialog.axaml`
- `CCP.Avalonia/Views/Dialogs/DisplayNameDialog.axaml.cs`
- `CCP.Avalonia/Views/Dialogs/LoginDialog.axaml.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.DisplayNameAsk.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.Login.cs`
- `CCP.Core/Services/Account/NeedsDisplayNameRule.cs`
- `CCP.Core/Services/Chaster/ChasterService.cs`
- `CCP.Core/Services/Commands/AiCommandService.cs`
- `CCP.Core/Services/Commands/GetBackToMeCommand.cs`
- `CCP.Core/Services/Commands/MediaCommand.cs`
- `CCP.Core/Services/Haptics/LockdownExitRule.cs`
- `CCP.Core/Services/Haptics/LockdownService.cs`
- `CCP.Core/Services/Safety/BlinkStopGate.cs`
- `CCP.Core/Services/Safety/PanicAftermath.cs`
- `CCP.Core/Services/Safety/VoicePanicReach.cs`
- `CCP.Core/Services/Timing/TickStep.cs`
- `CCP.Core/Services/Webcam/BubbleAccuracyTest.cs`
- `Tests/CCP.Avalonia.Tests/DisplayNameAskTests.cs`
- `Tests/CCP.Core.Tests/NeedsDisplayNameRuleTests.cs`
- `docs/cloud/sync8-account-name.md`
