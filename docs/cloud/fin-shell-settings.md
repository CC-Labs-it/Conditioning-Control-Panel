# fin-shell-settings: dependency-only audit

- Audited the single `shell-settings` row and its WPF reference at base
  `50ba1436f695c81f672dec32e9f133565212e778`.
- Kept the row `stub` and replaced the follow-up placeholder with an explicit
  dependency: EMI offer chips, answer/ignore lifetime and effect execution
  (`wire-views-settings-emidesk-b`, row `views-settings-emidesk`).
- No application or test code changed; no new surface needs panic registration.
- No trunk code imported. Read-only `parity/trunk` was fetched at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; the WPF tour-end book offer was
  introduced by `0fc4d3cf11d01094d0718624765a08f02662e9af` and is already in the base.

## Behaviour audit

Reference: `ConditioningControlPanel/MainWindow/MainWindow.Settings.cs`, with the
existing head paths in `MainShellWindow.Settings.cs`, `.Settings.Save.cs`,
`Tours/TutorialHead.cs`, `Views/Controls/AppSettings/` and the named tests below.

| WPF behaviour in this row | State on the pinned head |
| --- | --- |
| Settings/device navigation through the shell and section focus | Live: `OpenAppSettingsSection` / `OpenDeviceSettings`. |
| Settings load/save and audio labels | Live editors own the model, section refresh seeds controls, Save persists before its preset offer; `ShellSettingsWiringTests` and the isolated settings suites cover the user paths. Feature-owned editors remain in their own ledger rows. |
| Save tick/ripple and New/Overwrite/Cancel preset offers | Live: `FlashSaveAbsorb`, existing preset CRUD; overwrite preserves identity, creation date and description; motion Off suppresses decoration. `ShellSettingsWiringTests`. |
| Assets folder selection, personal-root refusal, pack move offer, scan/tree refresh and video queue reload | Live: `ApplyAssetsFolder`, shared `AssetFolderSafety` / `PackFolderMover`; `ShellSettingsWiringTests`, `AssetFolderSafetyTests`, `PackFolderMoverTests`, `FirstRunFolderPickerTests`, `LinuxMediaDefaultTests`. |
| Help panel, scrim/content clicks, browser visibility, owned bug-report modal | Live in `MainShellWindow.Settings.cs`; `ShellSettingsWiringTests`. |
| Shell guides, Mod Creator guide, Awareness natural-completion Puppy editor | Live through `CoreTutorial` / `TutorialHead` and their existing owned surfaces; browser restored on close/panic; `ShellSettingsWiringTests`, `TutorialTourTests`. |
| Tour EMI summon, start/step/end narration and detach | Live: head `EmiTourNarrator` attached before Start; step-specific pools fall back to `tourStep`; existing shell wiring test. |
| Exit | Existing `BtnExit_Click` delegates to the shell exit path, including lockdown refusal; exit/chrome/session behaviour belongs to those existing rows. |
| Book offer after a completed or skipped tour | Missing; every remaining part of this behaviour depends on the unported EMI offers path below. |

The old `BtnTutorialWhatMoved_Click` is declaration-only in the WPF reference:
`git grep -n BtnTutorialWhatMoved_Click 50ba1436f695c81f672dec32e9f133565212e778 -- ConditioningControlPanel`
returns only `MainWindow/MainWindow.Settings.cs:590`. The live Help replay is the
What Moved card on both heads (`MainShellWindow.NavSearch.cs` on Avalonia).
This is not a P122 promotion: the row remains `stub` because the book offer has a
live WPF caller.

## Remaining behaviour and dependency proof

At the pinned base, WPF `Services/EmiDesk/EmiTourNarrator.cs:195` calls
`EmiCodex.MaybeOfferSoon` after both `tourFinished` and `tourSkipped`.
`Services/EmiDesk/EmiCodex.cs:666-736` specifies:

- Wait 20,000 ms behind the ending line; a second ending replaces the pending
  timer rather than queuing another offer. Dispatch to the UI thread and fail softly.
- Require codex content before scheduling; at delivery, check content again,
  reject an already-open codex and reject `EmiState.Current.CodexOpens > 0`.
- Fire `bookOffer` with the ending reason. Away drops it before the engine draws,
  allowing a later caller to retry without spending its once-ever budget.
- Preserve the shipped priority 3 / `ever:1` moment and the engine's offers,
  bedtime, ignore-streak, cadence and situational gates. `bookOffer` is not scripted.
- Show the authored question and two chips, wait indefinitely (no give-up
  timeout), honour yes/no replies and Esc/dismiss/fullscreen cancellation with
  answer/ignore bookkeeping; accepting `book:open` opens `EmiBook`, not the old plain
  codex reader (`EmiOffers.BookFeasible` / `OpenBook`).

Evidence in the same base:

- `CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskService.Speech.cs:32-33`
  explicitly defers asks/effect execution; Fire calls `Draw` but neither
  `DrawAsk` nor a `ShowAsk` surface.
- `git grep -n -E 'AskSituationProbe|EffectFeasibleProbe|DrawAsk|MaybeOfferSoon' 50ba1436f695c81f672dec32e9f133565212e778 -- CCP.Avalonia`
  returns only the two comments mentioning `MaybeOfferSoon`; there is no EMI
  offer probe assignment, pending-ask consumer or delayed-offer implementation.
- Core `EmiLineEngine` intentionally refuses asks when its head-side
  `AskSituationProbe` or `EffectFeasibleProbe` is unset. Removing that protection
  would create offers without working effects.
- WPF `EmiDeskService.Speak` consumes `DrawAsk` and calls `win.ShowAsk`;
  `EmiDeskWindow.Bubble.cs` owns answer/ignore handling. No matching EMI surface
  exists in Avalonia. The companion tube's `ShowAskCard` is a different feature.
- The manual itself is available: `EmiDeskWindow.OpenBook` and the `codex` ring
  target already open it and persist its bookmark/open count. The missing
  dependency is the offer path, not a missing book reader.

Adding only a timer/Fire call would not make the WPF behaviour user-reachable.
The unit's dependency-only rule therefore requires this documentation-only
result. Once EMI offers land, adapt the delay and gates above, test both endings,
re-arming, refusal/retry and yes/no/ignore paths, and register the offer surface
for panic in its owning unit.

## Verification limits

SDK: .NET 10.0.402. All commands used Release configuration. The original cloud
snapshot had a different HEAD and read-only Git metadata; the requested clone
fallback was used and its HEAD was verified equal to the exact base before work.

| Command | Result |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | Exit 0; 0 errors (48 existing warnings). |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | Exit 0; 0 errors (172 existing warnings). |
| `dotnet build <test-project> -c Release` for every Linux test project listed below | All 11 exit 0; 0 errors. |
| `bash .github/scripts/core-guards.sh` | Exit 0; both guards passed, 424 XAML type references checked. |
| `export DiffEngine_Disabled=true; dotnet test Tests/CCP.Core.Tests -c Release --no-build` | Exit 0; 2,834 passed, 0 failed, 5 skipped; 2,839 total. |

Built test projects (each `Tests/<name>/<name>.csproj`):

- `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`.
- `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`,
  `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Scheduler.Tests`,
  `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`,
  `CCP.Avalonia.Snapshot.Tests`, `CCP.Avalonia.Tests`.

Windows cross-build:
`dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`
returned exit 0, 0 errors (512 existing warnings). The WPF reference and tests
were compiled but not run.

Avalonia tests were compiled but not run, following this job's measured font
creation limitation; execution belongs to GitHub rcheck. Relevant existing
classes include `ShellSettingsWiringTests`, `TutorialTourTests`,
`EmiDeskSettingsSpeechTests`, `EmiBookDemoTests` and `EmiCodexReaderTests`.
No new behaviour or fix was implemented, so there is no new test or before/after
failure claim. No desktop, notification, native picker, render-all or live
Keincheck run was performed. No new Windows code was added; Windows runtime
behaviour cannot be exercised here.

The five existing Core speech integration tests skipped because the Vosk model
is unavailable (the job reports its download blocked with HTTP 403);
`CCP_REQUIRE_VOSK=1` was not set. Tests were not modified to obtain this result.
The Core test process used an explicit scratch `CCP_USERDATA_DIR`.
Completed, unused test/app `bin` directories were removed after successful builds
to keep repeated asset copies within the cloud disk quota.

## Assumptions

- Apply the explicit dependency-only rule to the last missing behaviour; do not
  partially wire an offer that the head cannot display or execute.
- Retain the existing Linux media-folder default and live-editor ownership;
  neither is a new gap introduced by this audit.
