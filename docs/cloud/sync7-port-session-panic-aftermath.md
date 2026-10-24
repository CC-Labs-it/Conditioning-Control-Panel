# sync7-port-session-panic-aftermath

- Reuse the existing Core panic aftermath rules; arm them before local surface teardown and remote panic. A palette-only Escape does not arm them, while early lock-card and video-grace panic rungs do. The full key route increments the panic generation only once.
- Release quiet on explicit START, Start normally, Jump right in, local Resume and Start today's session. Automatic engine starts and remote disconnect do not release or arm it.
- Adapted WPF sources: `904f7fb298f03cd7ada1bb059b9dfbf892a2c369`, `701cff1571d5a3fdf41f565bf8f6906cfceb0dde`, `d8fa5a5f95c951b67ae159891b2ccaf9337388cd`. Fetched `parity/trunk` read-only; no additional trunk code imported or files overwritten.
- No new surface, so no new PanicSurfaces registry entry. No sync ledger or parity ledger edits.

## Per-commit disposition

Each commit is partially ported, with the remaining head integrations blocked-by-host:

| WPF commit | Ported behavior and proof | Already covered / blocked-by-host |
| --- | --- | --- |
| `904f7fb29` | Local quiet: `CCP.Avalonia/Views/Windows/PanicSurfaces.cs:110`; remote quiet: `CCP.Core/Services/RemoteControl/RemoteCommands.cs:113`. Tests: both `PanicAftermathPortTests` classes, stop-pass and remote-panic tests. | Core quiet and pause rules already covered in `CCP.Core/Services/Safety/PanicAftermath.cs:28`. Blocked: AvatarTube Trigger Mode timer, trigger queue and lead-in; remote/voice session-resume command host. |
| `701cff157` | User-only quiet release: `CCP.Avalonia/Views/Windows/MainShellWindow.StartStop.cs:72`. Test: `PanicAftermathPortTests.AutomaticStartKeepsQuietWhileUserStartAndResumeReleaseIt`. | Monotonic clock and session/run/pause identity already in `CCP.Core/Services/Safety/PanicAftermath.cs:39` and `:84`. Engine-off local session pause already in `MainShellWindow.StartStop.cs:99`, reached unconditionally by PanicSurfaces engine entry; existing `SessionRunWireTests` covers pause. Blocked: scheduler timer/manual-stop runtime; remote session dispatch, pause marking and refusal reporting; Trigger Mode host. |
| `d8fa5a5f9` | Palette exception: `MainShellWindow.PanicKey.cs:87`; Jump right in: `MainShellWindow.StartStop.cs:63`; local Resume: `MainShellWindow.SessionRun.cs:114`; today's session: `MainShellWindow.ProgramsTab.cs:281` (all under `CCP.Avalonia/Views/Windows`). Tests: `PanicAftermathPortTests` and `ProgramsRunLifecycleTests`. | Engine hold rules already in `CCP.Core/Services/Safety/PanicAftermath.cs:69`. Blocked: Lockdown dose keeper; remote start-session host; tube Trigger Mode queue; idle speech dispatch and unprompted metadata/voice gate. |

Missing-host evidence: `CCP.Core/Services/RemoteControl/RemoteCommands.cs:105` falls through to "not on this build" for session verbs; `CCP.Avalonia/Views/Windows/MainShellWindow.RemoteControl.cs:8` documents missing session verbs. `CCP.Core/Services/Bark/BarkEngine.cs:19` documents missing DispatchIdle, and its `BarkSpeech` record carries no unprompted classification. The Avalonia tube has a general speech queue, but no WPF Trigger Mode timer or trigger lead-in. Scheduler settings exist, but no scheduler engine timer/manual-stop state. `LockdownDoseKeeper` exists only in WPF. No unused pause-mark fields or speculative hosts were introduced.

## Assumptions

- Follow the final behavior of the three commits, including their corrections to the first commit; do not restore the initial remote-panic pause mark.
- Keep sibling START/resume session-clock changes out of this unit. The existing local pause behavior and program-session panic policy remain authoritative on this base.
- Treat the absent unprompted speech classification/dispatch as a missing host; do not import sibling idle-voice features or silence prompted replies indiscriminately.

## Verification limits

- SDK: .NET 10.0.100. Channel installation failed with HTTP 403; explicit stable version installation succeeded.
- `dotnet build -c Release`: Core, Avalonia and all 12 Linux test projects named in `.github/workflows/build.yml`: exit 0, zero errors (existing warnings). WPF test project also built with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors, 557 warnings. Windows code compiled but was not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127, script absent from this base. No ledger rows added or promoted.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~PanicAftermathPortTests|FullyQualifiedName~ProgramsRunLifecycleTests|FullyQualifiedName~SessionRunWireTests'`: exit 1; 1 passed, 4 failed before behavior assertions at glyphTypeface creation for Segoe UI Black. Those window tests require the supervisor's font-capable runner; they were compiled but could not execute their behavior here.
- Mutation proof: remove the new remote `ArmQuiet` production line, run the Core `PanicAftermathPortTests` filter: exit 1, expected Assert.True failure. Restore the line. Remove the new `PanicSurfaces.StopAll` arming line and run `StopPassArmsQuietBeforeAnySurfaceEvenIfItThrows`: exit 1, expected Assert.True failure. Restore the line. Neither mutation is committed.
- Initial full Core run revealed that existing remote tests leaked the newly armed quiet into later AI tests. Added disposal cleanup to `RemoteRelayTests`; no assertions removed or weakened. Final rerun results follow.
- No live desktop or real user profile used: test assemblies install `CCP_USERDATA_DIR` sandboxes. Vosk model unavailable; the existing speech tests self-skip. No Windows runtime tests or device tests run.
- Final `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3,954 passed, 0 failed, 5 Vosk tests skipped, 3,959 total. The initial media-selection failure did not recur on the full rerun.
- Restored head production line, reran `StopPassArmsQuietBeforeAnySurfaceEvenIfItThrows`: exit 0; 1 passed, 0 failed. Final head and Core test builds had zero errors. `git diff --check`: exit 0.
