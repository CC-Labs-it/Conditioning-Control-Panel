# fin5-shell-lab: dependency audit

- Audited `shell-lab` against frozen WPF at the exact base commit
  `db0a1c5ee0719cab7abbdab1b07cc4bf7ac70e19`.
- Dependency-only outcome: no runtime changes, no new surfaces, no ledger
  promotion. The existing single row remains `stub`; no new tester step applies.
- No trunk imports; no imported commit credits apply.

## Behavior audit

Historical absence claims in the ledger are superseded by current code. The head
already implements premium/double-warning activation, panel swap, clock/masking,
five-tap phrase exit, wrong-phrase tripwire, title badge/navigation, strict/panic
holds, crimson theme/restoration, level glow and 600 ms flash/close cleanup.
Windows key suppression has a Win32 seam. `GradedIntakeTabView` now owns quiz
focus/login/fullscreen/drone, history/reports, intake/pass gates and launch,
pass refresh, pop-quiz settings and test launch. FYP has the gated shell
`ShowTab("fyp")` -> `GameWindow.Fyp` path. Do not overwrite these implementations.

Remaining live behavior and dependencies:

1. **Back Room launch**: WPF `MainWindow.Lab.cs:302` opens/focuses the room without
   a tier gate or boot-failure prompt; failure uses localized copy. Live callers:
   `MainWindow.PlayTab.cs:71`, `MainWindow.SparkleWallet.cs:85`, and
   `MainWindow.Exclusives.cs:204`. No Back Room entry exists in Avalonia's
   `Views/Games/GameWindow.cs`. Its common ready/init/media/fx handling is not a
   complete room host. WPF `Services/BackRoom/BackRoomHostService.cs` (778 lines)
   requires account/sign-in gating, room/Breakout switch confirmation, identity pin
   per bridge, generation isolation, media warmup, SP relay, settings/voice/FX/
   haptics, race handoff/return, presence, minimize/panic suspend and teardown.
   The bridge and head adapters are additional work; merely loading a URL would
   lose account/payout/lifecycle contracts.
2. **Possession host/readout**: WPF `Lab.cs:38,688,867-986,1035-1094` initializes
   the shell host; attaches/detaches the same rung delegate; immediately paints
   Settle, five inclusive-index ember pips and localized rung text; shows only
   when enabled; clears/unsubscribes on exit; repaints Settle immediately on timer
   restart. Its development preview dresses the active panel, fixed clock, Melt
   readout and badge without running Lockdown. Core already has the shared
   `PossessionDirector` and contracts: reuse them. However Avalonia's
   `MainShellWindow.Possession.cs` is an empty host stub. `PossessionRuntime` has
   only four effects, without the shell target walk, real director integration,
   scenes, warden and reactive layer. WPF's shell host alone is 704 lines.
   A synthetic rung would falsely imply that the room is actually haunted.
3. **Possession first-run rules/consent**: WPF `Lab.cs:1104-1141` presents or inboxes
   the rules, spends the intro flag only when opened, and barks immediately.
   This needs the actual possession lifecycle above. The activation warning must
   describe intensity and Full Doki theatre only once those behaviors exist;
   the head currently omits that claim deliberately.
4. **Dose Keeper consent/behavior**: WPF `Lab.cs:89-91` promises engine start,
   feature conscription and escalation when everything is turned off. The head
   omits that promise because it has no Dose Keeper host. WPF
   `Services/Haptics/LockdownDoseKeeper.cs` is 590 lines of selection, grace,
   entitlement, takeover and engine integration; shared logic belongs in Core.
5. **Emergency Exit gamble consent**: WPF `Lab.cs:84-88` explains early release
   versus full timer restart. The game has a live call from
   `Views/Tabs/LockdownTabView.xaml.cs:359`. Avalonia's button instead states
   phrase steps and remaining time; it does not launch a game or restart the
   timer. The missing host/protocol is WPF `EmergencyExitHostService.cs`
   (566 lines), including verdict and safety handling. Consent must stay honest
   until the actual game/verdict path is ported.

## Stale and dead members: pinned proof

```sh
git grep -n -E 'BtnStartGoon_Click|BtnStartChaos_Click|BtnQuickStartChaos_Click' db0a1c5ee0719cab7abbdab1b07cc4bf7ac70e19 -- 'ConditioningControlPanel/*.cs' 'ConditioningControlPanel/*.xaml'
git grep -n -E 'BtnStartBureau_Click|BtnStartArcademy_Click' db0a1c5ee0719cab7abbdab1b07cc4bf7ac70e19 -- 'ConditioningControlPanel/*.cs' 'ConditioningControlPanel/*.xaml'
```

The first source-only search returns no hits (historical Markdown audits still mention these names). The second returns declarations/log strings,
Bureau's stale XML/XAML comments and an Arcademy host comment, with no live WPF
invocation. Goon/Chaos/QuickStart are historical ledger names, not current Lab
handlers; do not overwrite the existing ChaosHub/ChaosRunHost. Bureau and Arcademy
wrappers do not justify new unused code. P122 cannot promote the whole row:
Back Room and possession still have live callers. No dead-row/render-all promotion
is claimed.

## Assumptions and scope decision

The pinned WPF implementation takes precedence over old member lists. Completing
the missing hosts, adapters and focused tests collectively exceeds the 1,000-line
cap even with the existing Core director and GameWindow reused. Source sizes are
scope evidence, not a proposal to copy entire WPF files. Follow the explicit
pure-dependency rule: retain the stub and change only this audit. No partial
launcher, fabricated readout or unsupported consent claim is introduced.

## Verification limits

No behavior changed, so no tests or panic registrations were added. Windows hooks
cannot run on Linux. No real desktop/profile was opened. Render-all was not run:
this is a retained stub, not a P122 promotion. Execution results follow below.

Commands use .NET SDK 10.0.100 and Release configuration, with
`DiffEngine_Disabled=true` for test runs.

- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML
  references checked). Initial read-only scratch-file failure was resolved by
  rerunning with writable scratch/output access.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127, script absent on this base.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 3,960 passed,
  5 existing Vosk skips, 1 failed. `BoardTilesTests.A_frame_fits_the_budget`
  measured 16.87 ms while compilation was running. No test was changed or disabled.
- `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release`:
  exit 0, 0 errors.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release
  --no-build --filter 'FullyQualifiedName~LabLockdownThemeTests|FullyQualifiedName~LockdownTests|FullyQualifiedName~LockdownVeilTests|FullyQualifiedName~PerformanceLockdownPulseTests'`:
  exit 1; 3 passed, 16 failed, 0 skipped. The failures report
  `Could not create glyphTypeface` for `Segoe UI Black` and `Inter`; the known worker font
  limitation persists even with this base's embedded-font setup. Compilation
  succeeded; functional validation of these cases still needs a capable runner.

Build commands: `dotnet build <project> -c Release --nologo`.
All Linux workflow test projects were compiled (they were not all executed):

| Project | Exit | Result tail |
| --- | --- | --- |
| `CCP.Core` | 0 | 0 errors |
| `CCP.Avalonia` | 0 | 0 errors |
| `CCP.Core.Tests` | 0 | 0 errors |
| `CCP.Core.Settings.Tests` | 0 | 0 errors |
| `CCP.Core.Content.Tests` | 0 | 0 errors |
| `CCP.Avalonia.Consent.Tests` | 0 | 0 errors |
| `CCP.Avalonia.Language.Tests` | 0 | 0 errors |
| `CCP.Avalonia.CatalogueStartup.Tests` | 0 | 0 errors |
| `CCP.Avalonia.Migration.Tests` | 0 | 0 errors |
| `CCP.Avalonia.Scheduler.Tests` | 0 | 0 errors |
| `CCP.Avalonia.Report.Tests` | 0 | 0 errors |
| `CCP.Avalonia.GeneralSettings.Tests` | 0 | 0 errors |
| `CCP.Avalonia.Snapshot.Tests` | 0 | 0 errors |
| `CCP.Avalonia.Tests` | 0 | 0 errors |
| `ConditioningControlPanel.Tests` (Windows targeting flags below) | 0 | 0 errors |

- WPF compile command: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
  Exit 0; 557 warnings, 0 errors. The WPF application and its test assembly were
  compiled, not run. No Windows hook/runtime behavior was verified.
- Isolated rerun after compilation: `dotnet test Tests/CCP.Core.Tests -c Release
  --no-build --filter 'FullyQualifiedName~BoardTilesTests.A_frame_fits_the_budget'`:
  exit 0, 1 passed, 0 failed. The full-suite result above remains a failed run;
  the isolated pass supports a timing/load limitation rather than a code regression.
- `git diff --check`: exit 0. Only this documentation file changed.

Outcome: NOOP pending complete Back Room, Possession, Dose Keeper and Emergency
Exit hosts. The row must remain `stub` until their real user paths are present.
