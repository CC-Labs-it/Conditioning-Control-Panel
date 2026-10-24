# sync8-remote-end — PR-body draft (no PR opened)

- Port WPF `2a712d9da878a68728890fb9b422242f2281dd2f`: **ported**, `CCP.Core/Services/RemoteControl/RemoteRelay.cs:339` asks the panic hand-back rule; `RemoteRelayTests.Remote_end_preserves_lockdown_and_corrects_its_record` covers disconnect/session end with stop-effects both on and off. Lockdown's flags were already merged at `CCP.Core/Services/Haptics/LockdownService.cs:130` and `:190`; reused, not duplicated.
- Port WPF `4adc4c47678304db03a24d7e1f6623ba8409256e`: **ported**, `CCP.Core/Services/RemoteControl/RemoteHoldings.cs:28`, `RemoteRelay.cs:322` and `:358` snapshot, record writes, correct Lockdown's record and hand back on session end/exit; `CCP.Avalonia/App.axaml.cs:463` heals after Lockdown recovery and `:1198` releases before exit save. Tests: `RemoteHoldingsTests` (ownership, intervening wearer edits, strict/Lockdown matrix, recovery file) and the two new relay theories. The base already had `CorrectRecordedStrictLock` at `CCP.Core/Services/Haptics/LockdownService.cs:145` (its stray App logger required a scratch compile repair).
- Port WPF `9fc33415f07940e80d4c1da17613587465cb7260`: **ported**, `CCP.Core/Services/RemoteControl/RemoteCommands.cs:129` leaves safety settings alone on non-panic stops; `RemoteRelay.cs:317` releases safety before optional effect stop. Test: `RemoteRelayTests.Disconnect_returns_the_wearers_strict_lock`, both an untouched wearer lock and one disabled by the controller.
- Safety surface registration remains `CCP.Avalonia/Views/Windows/PanicSurfaces.cs:62` (`remote-haptics`). No new window or safety surface.
- Read-only `parity/trunk` was fetched (tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`). No code was imported from that tip: source imports are the three credited WPF commits already reachable from the pinned base.
- Separate first `compat-scratch` commit repairs the merge's missing Core helper dependencies and three App references. Scratch helper types carry a Core prefix to avoid duplicate WPF types. Integration should drop this commit in favor of the compat lane.

## Assumptions

- Keep this lane scoped to remote endings. Explicit `trigger_panic` retains the Avalonia head's existing emergency escape behavior and its existing behavioral tests; passive disconnect/session-end cleanup now uses the WPF ownership rules.
- Avalonia currently refuses remote spiral/pink/opacity/Melt/session commands as `not on this build` (`RemoteCommands.cs:9`). Their WPF command/UI wiring is n/a for this head today; the shared holdings/recovery format and its WPF behavior tests are ported, without enabling new commands. Remote Easy opacity application likewise has no head counterpart. `disable_panic` remains refused by the existing head policy.

## Verification limits

- Pinned base verified: `28ab74eed692067fed4f2051791b3a6d0976b1dc`; initial snapshot was clean but on the wrong commit, corrected by the prescribed fetch/checkout.
- SDK 10.0.100 installed (channel discovery failed; the explicit version succeeded).
- Initial Core build failed with 14 baseline compile errors; scratch repairs resolve them.
- `dotnet build CCP.Core -c Release`: exit 0, zero errors.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3990 passed, 5 existing Vosk model-dependent tests skipped, 0 failed. No tests weakened or disabled; the existing direct strict-off test now asserts WPF's new refusal and retained lock.
- Focused Core filter `RemoteRelayTests|RemoteHoldingsTests`: exit 0; 54 passed, 0 failed.
- Regression proof: temporarily restored the pre-port relay and commands from the scratch commit, retaining the new tests. The two new integration theories produced **6 failures / 0 passes**, then passed with the port restored. The mirrored pure holdings tests require the new Core type (absent on base) and cover the WPF truth tables and disk recovery.
- Windows runtime behavior cannot be exercised on this Linux worker. No real desktop or profile was used.
- `dotnet build CCP.Avalonia -c Release`: exit 0, 0 errors (existing warnings). All 12 Linux-capable workflow test projects compiled with 0 errors; CatalogueStartup was rerun successfully with `-m:1 -p:UseSharedCompilation=false` after an initial concurrent-output collision and a rebuild-worker exit. No source change was needed for those infrastructure failures.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~RemoteControlTabTests|FullyQualifiedName~RemoteControlShellTests|FullyQualifiedName~RemoteHapticPanicTests|FullyQualifiedName~PanicSurfacesTests'`: exit 1; 5 passed, 5 failed on `Could not create glyphTypeface` (Inter / Segoe UI Black). This is the documented worker font limitation; these classes still require GitHub rcheck. An earlier overlapping-build run failed loading precompiled XAML; a sequential build removed that artifact issue.
- Core holdings live in `ConditioningControlPanel.Services.RemoteControl`, avoiding collisions with WPF's frozen `Services.Remote` copy when the WPF test project references both assemblies.

- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -m:1 -p:UseSharedCompilation=false -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors (605 existing warnings). The WPF app and tests compiled; Windows tests were not run. Initial duplicate-type failures were resolved by the distinct port namespace and prefixed scratch helpers.

## Changed files

Port: `CCP.Avalonia/App.axaml.cs`; `CCP.Core/Services/RemoteControl/{RemoteCommands,RemoteHoldings,RemoteRelay}.cs`; `Tests/CCP.Core.Tests/{RemoteHoldingsTests,RemoteRelayTests}.cs`; this ledger.

Scratch compatibility files (drop at integration):

- `CCP.Core/Services/Chaster/ChasterService.cs`
- `CCP.Core/Services/Commands/AiCommandAdmission.cs`
- `CCP.Core/Services/Commands/AiCommandService.cs`
- `CCP.Core/Services/Commands/GetBackToMeCommand.cs`
- `CCP.Core/Services/Commands/MediaCommand.cs`
- `CCP.Core/Services/Haptics/LockdownService.cs`
- `CCP.Core/Services/LockdownExitRule.cs`
- `CCP.Core/Services/Safety/BlinkStopGate.cs`
- `CCP.Core/Services/Safety/PanicAftermath.cs`
- `CCP.Core/Services/Safety/VoicePanicReach.cs`
- `CCP.Core/Services/Speech/VoiceCommands.cs`
- `CCP.Core/Services/Timing/TickStep.cs`
- `CCP.Core/Services/Webcam/BubbleAccuracyTest.cs`
