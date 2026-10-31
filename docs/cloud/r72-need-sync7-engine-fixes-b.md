# Sync7 gap re-cut onto 7.2

Base: `74ac6ce3d5e0ae082c2871c00e864943e15aa2ad`, clean `work` checkout after the requested fetch/reset from `aa9918833722f057079e455167cb3d1b14462340`. Origin is `CC-Labs-it/Conditioning-Control-Panel`.

All three requested lanes were fetched and their own ranges read:

- `c2bc2f5aaa47..d932ebb2234d`: bottom bar ported; no-video implementation already present.
- `39260f198fad..8f2187bb846e`: guarded clock steps and parked-session restart covered by fetched PR #3421, not copied.
- `39260f198fad..0dfcb3af6ce9`: direct stop-pass quiet ported; existing stricter key/watchdog/remote paths retained. No WPF changes, new keys or Core dependencies.

| Behavior | WPF specification | 7.2 disposition |
| --- | --- | --- |
| Bottom bar: session name, countdown, elapsed for open-ended, hidden countdown, pause mark, visible again, engine running fallback and stop clear | `ConditioningControlPanel/MainWindow/MainWindow.Presets.cs:1898-1920` | Ported using existing `CCP.Core/Services/Session/SessionClockLabel.cs:64`; `CCP.Avalonia/Views/Windows/MainShellWindow.SessionRun.cs:145,154,182-197`, `MainShellWindow.StartStop.cs:139`. |
| No-video advice, scheduled once per launch, manual each attempt | `ConditioningControlPanel/Services/BubbleCountService.cs:469-473,538-546` | Already: `CCP.Core/Services/BubbleCountScheduler.cs:84-86,206-213`, `CCP.Avalonia/Views/Windows/BubbleCountHost.cs:45`. |
| Guarded clock and teardown steps, first failure per named step, repeat counts | `ConditioningControlPanel/Services/Session/SessionEngine.cs:360-392,665-718` | Covered by fetched PR #3421 head `d3411aaf83711eaf96fc1bf189bc4423846fedb6`: `CCP.Core/Services/Session/SessionRunner.cs:33-37,341-383,520-561`. This includes row 230 despite the earlier triage. |
| Explicit START resumes parked session and reasserts delayed starts | `ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs:129`, `ConditioningControlPanel/Services/Session/SessionClockRules.cs:50-67` | Covered by PR #3421: `CCP.Core/Services/Session/SessionRunner.cs:300-321` and head StartStop/SessionRun changes. |
| Quiet before direct tray/voice stop surfaces, continuation after a throwing stop | `ConditioningControlPanel/MainWindow/MainWindow.xaml.cs:1670`, shared `CCP.Core/Services/Safety/PanicAftermath.cs:53-60` | Ported: `CCP.Avalonia/Views/Windows/PanicSurfaces.cs:119-128`. Key passes its existing arm at `MainShellWindow.PanicKey.cs:122`, preserving one generation increment. Tray reaches it at `MainShellWindow.Tray.cs:121`. |
| Key early rungs arm quiet; palette-only Escape does not | `ConditioningControlPanel/MainWindow/MainWindow.xaml.cs:1670` | Already: `CCP.Avalonia/Views/Windows/MainShellWindow.PanicKey.cs:99-101`. |
| Remote panic arms quiet, watchdog fallback arms quiet | `ConditioningControlPanel/MainWindow/MainWindow.RemoteControl.cs:1484`, WPF key ordering above | Already: `CCP.Core/Services/RemoteControl/RemoteCommands.cs:120`, `CCP.Avalonia/Platform/PanicWatchdog.cs:115`. |
| Explicit local START and Resume release quiet | `ConditioningControlPanel/MainWindow/MainWindow.StartStop.cs:129`, `MainWindow.Presets.cs:2131` | Already: `CCP.Avalonia/Views/Windows/MainShellWindow.StartStop.cs:49`, `MainShellWindow.SessionRun.cs:112`. |

PR inspection used `git fetch origin refs/pull/3421/head` and `refs/pull/3423/head` because `gh pr view` returned Forbidden. The fetched #3423 head is `6c7b28fec49d233043f0e0988cfd2d5afafcc9e6` and contains Chaster/Scrolller/GifCascade changes, not the safety changes the supplied triage attributes to it. No claims about its live open state are made.

## Part B

- Keep row 271 out of this lane per the supplied ownership assignment. Today's session quiet release is still absent at `CCP.Avalonia/Views/Windows/MainShellWindow.ProgramsTab.cs:281`; WPF `ConditioningControlPanel/MainWindow/MainWindow.ProgramsTab.cs:2214` uses StartEngineFromUser. The fetched #3423 does not supply this, so ownership needs reconciliation.
- Rows 231-232 remain partial: remote/voice session resume is not hosted (`CCP.Core/Services/RemoteControl/RemoteCommands.cs:106`, `CCP.Avalonia/Views/Windows/MainShellWindow.RemoteControl.cs:7`). When hosted, wire the local-panic pause identity/refusal matching WPF `ConditioningControlPanel/MainWindow/MainWindow.RemoteControl.cs:1367-1378`. The pure rule is already in `CCP.Core/Services/Safety/PanicAftermath.cs:90-99`.
- Trigger Mode has menu settings only (`CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow.QuickMenu.cs:195-198`); no pulse timer/lead-in host to silence. WPF behavior is specified in `CCP.Core/Services/Safety/PanicAftermath.cs:9-25` and implemented by `ConditioningControlPanel/AvatarTube/AvatarTubeWindow.Speech.cs:2089,2149,2174`. No speculative host imported.
- Guarded SessionRunner steps are absent on this base but covered by #3421; integration must retain that work. Its errors use the standard file sink (`CCP.Avalonia/Program.cs:22-27`); the old lane's separate crash.log routing is not copied. WPF reports its first trace via `ConditioningControlPanel/Services/Session/SessionEngine.cs:711-715`. Reconcile dedicated crash-journal routing with row 229's owner. No row is marked verified.

## Validation

SDK: `dotnet-install --channel 10.0` could not discover a build (HTTP 403 from ci.dot.net); explicit stable `10.0.100` installed successfully in `/tmp/dotnet`. Commands below used that SDK and `-c Release`.

- `dotnet build CCP.Core`: exit 0, 50 warnings, 0 errors.
- Initial parallel head/test-project builds hit an Avalonia PDB lock. Sequential head build then passed with 0 warnings and 0 errors; test-project build passed with 0 errors. Final restored builds are recorded below.
- `dotnet test Tests/CCP.Core.Tests`: initial run had 4635 passed, 5 skipped and one frame-budget failure (21.36 ms) while compilation ran. Sequential full rerun with `--no-build`: exit 0, **4636 passed, 5 skipped, 0 failed**, 53 seconds. No tests altered or skipped to obtain the pass.
- Affected shell tests compiled, not run through behavior: shell creation fails on unavailable Segoe UI Black in PremiumSpark. Filter `FullyQualifiedName~PanicAftermathPortTests|FullyQualifiedName~SessionRunWireTests`: 2 passed, 2 failed at font construction. This includes the unchanged pre-existing shell test. No runtime shell parity claimed.
- Font-independent filter `FullyQualifiedName~Bottom_bar_projection|FullyQualifiedName~PanicAftermathPortTests`: 3 passed, 0 failed. The clock projection test calls the same production projection as the shell, on the Avalonia test dispatcher with Skia drawing. The shell wire test is retained for a font-capable runner.
- Fail-proofs: temporarily disable the production projection and omit the new stop-pass arm, then run `FullyQualifiedName~Bottom_bar_projection|FullyQualifiedName~StopPassArmsQuiet`. Exit 1, 2 failed, 0 passed, both `Assert.True` failures. Production files restored in a `finally` block. The separate already-armed test checks one panic generation increment. No mutation committed.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.` The first sandboxed attempt could not create temporary files; the successful run had filesystem access.
- `python3 .github/scripts/avalonia_docs.py`: exit 0, `avalonia_docs self-check OK`. Affected parity rows updated in place, status still stub.
- `git diff --check`: exit 0.
- Restored production rebuilds: `dotnet build CCP.Avalonia -c Release` exit 0, 201 warnings, 0 errors; `dotnet build Tests/CCP.Avalonia.Tests -c Release` exit 0, 144 warnings, 0 errors. Focused tests are `SessionRunWireTests.cs:26` (font-independent projection), `:65` (shell wiring, font-blocked), and `PanicAftermathPortTests.cs:11,31` (stop ordering and one generation).
- Final restored `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~Bottom_bar_projection|FullyQualifiedName~PanicAftermathPortTests'`: exit 0, 3 passed, 0 failed, 0 skipped.


## Review P2 correction

The review is accepted. The first port overlooked the base's late `panic-quiet` registry action and the empty-registry count test hid it. Direct stops armed twice, and the key's early arm was repeated during teardown. WPF arms once before rung handling (`ConditioningControlPanel/MainWindow/MainWindow.xaml.cs:1666-1670`). Remove that registry action and keep `PanicSurfaces.StopAll` as the sole pre-teardown arm; the key still supplies `aftermathArmed: true`.

The generation-count test now covers direct stops and an already-armed key. It retains any production `panic-quiet` action and replaces only unrelated desktop teardown actions with no-ops, so reintroducing that action fails the test. The existing quiet-ordering test now observes quiet at every production stop position. The structural safety list no longer requires a registry action for a preamble; quiet coverage moves to these stronger behavior assertions. No WPF/Core changes or new localization keys.

Review validation uses SDK 10.0.403 at `/tmp/dotnet/dotnet`:

- Fail-proof before the production fix: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~ProductionStopPassCountsEachPanicOnce'` exited 1. Both cases failed with an extra generation (expected 1 / actual 2; expected 3 / actual 4). The actual production registry quiet action was retained.
- Final Core, Avalonia and Avalonia test-project builds: exit 0 each, 0 errors (0, 201 and 144 warnings respectively).
- Focused Avalonia filter covering the 3 port cases, quiet-before-every-stop, registry ids/order and failing-stop continuation: exit 0, 7 passed, 0 failed, 0 skipped. These tests ran without constructing a font-dependent shell.
- Core guards: exit 0, `All Core guards passed.` Documentation self-check: exit 0, `avalonia_docs self-check OK`. `git diff --check`: exit 0. Ledger rows remain stub, with no live verification claimed.
- Full `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0, 4636 passed, 5 existing Vosk skips, 0 failed, 42 seconds.


## Local review P2: projection test cleanup

Accepted the request for explicit cleanup in `SessionRunWireTests.Bottom_bar_projection_uses_the_runner_clock_without_a_shell`: save the previous `App.Sessions` reference and `CoreSettings.Current.ShowSessionCountdown`, then restore both in `finally`, like the sibling shell test. This changes tests only; no WPF/Core behavior or localization keys change. The assembly hook in `TestIsolation.cs:49-87` already restores process state after each test, but the test now also fulfills its own cleanup contract without depending on that hook.

A two-case regression installs a sentinel runner and each countdown value, calls the projection test directly, then checks both values before any outer isolation hook can restore them. It restores its own incoming state in `finally`. This checks cleanup deterministically instead of relying on xUnit test order. Existing ledger notes are retained and the projection evidence is appended; the rows remain stub.

Local review validation (SDK `/tmp/dotnet/dotnet`, Release):

- Fail-proof before adding the two restores: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~Bottom_bar_projection_restores_caller_state'` exited 1, 2 failed. The true case failed `Assert.Equal` (true versus false); the false case failed `Assert.Same` (wrong runner reference). No isolation hook ran between the test-under-test and either assertion.
- Final `dotnet build CCP.Core`, `dotnet build CCP.Avalonia`, and `dotnet build Tests/CCP.Avalonia.Tests`: exit 0 each, 0 errors. Warnings: 0, 0 and 144 respectively.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~Bottom_bar_projection|FullyQualifiedName~PanicAftermathPortTests|FullyQualifiedName~TheStopPass_ArmsTheQuiet|FullyQualifiedName~EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered|FullyQualifiedName~TheWpfOrderingRulesStayDeclared|FullyQualifiedName~OneFailingStopNeverSkipsTheRest'`: exit 0, 10 passed, 0 failed, 0 skipped.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.` `python3 .github/scripts/avalonia_docs.py`: exit 0, `avalonia_docs self-check OK`. `git diff --check`: exit 0.
- Full `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0, 4636 passed, 5 existing Vosk skips, 0 failed, 43 seconds.
