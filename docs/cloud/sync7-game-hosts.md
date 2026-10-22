# sync7-game-hosts

- Port WPF main `6e24e43f5523c7c81e58a57e28cfa7f86633cd03`: Intake borrows the wake/PTT microphone for each spoken answer, waits for the posted stand-down and capture release (up to 600 ms), retries the handover between listen windows, and returns it on answer, idle, stop, error, close and panic. Return reconciles current settings rather than forcing voice on.
- Move the existing pure `IntakeMicBorrow` unchanged from WPF into CCP.Core; WPF and Avalonia share one holder rule. The old answer cannot return a newer answer's loan. Dispose each loop's cancellation source even after supersession.
- Port WPF main `1638538cbb7b21a9e8eff1528b53ce1fbd5095ca`: foreground Intake keeps the first Escape as pause before panic side effects. Only ready/non-exiting pages receive `kept-escape`, followed by web focus. A second Escape within two seconds still panics. Rebound panic keys, active engines and lock cards retain the Core panic policy. Existing Intake PanicSurfaces registration is reused.
- Add Core holder tests and headless page-driven handover/Escape tests. Existing Intake test harnesses now use shipped fonts and test-only Inter aliases for unavailable Windows faces. Headless activation is delivered through WindowBase's real notification.
- Update main-sync rows 81/186 and append evidence to the existing Graded Intake parity stub. Arcademy, Back Room and Goon hosts are n/a for this unit on Avalonia.
- Read the named WPF diffs and current WPF implementations. Fetched `parity/trunk` read-only; no trunk code imported. No PR opened.

## Verification limits

- SDK: .NET 10.0.100 at `/workspace/dotnet`; Linux checks use `DiffEngine_Disabled=true`. Builds use `-m:1 -p:UseSharedCompilation=false -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForAdditionalFilesIfPossible=true` to fit shared resources.
- `dotnet build CCP.Core/CCP.Core.csproj -t:Rebuild`: passed, 48 existing warnings, 0 errors. This repaired an invalid generated Core DLL left by initial disk exhaustion; the failed assembly-discovery runs are not behavioral evidence.
- `bash .github/scripts/core-guards.sh`: passed (429 XAML references checked).
- `dotnet test Tests/CCP.Avalonia.Tests --filter 'FullyQualifiedName~IntakeMicHandoverTests|FullyQualifiedName~IntakeGameEscapeTests|FullyQualifiedName~IntakeBridgeTests|FullyQualifiedName~IntakeHostWindowTests|FullyQualifiedName~IntakeLifecycleTests'`: **19 passed, 0 failed, 0 skipped**.
- Escape negative proof: run the new test assembly against the compiled head before the Escape fix (`BuildProjectReferences=false`); both cases failed at the assertion that the host stays visible. Log `/workspace/game-escape-negative-activated.log`.
- Mic negative proof: temporarily disable both `Mic.Take(id)` calls, run all five handover exit cases, then restore final source in `finally`; **5 failed, 0 passed**, at the borrowed-before-listen assertion. Log `/workspace/game-mic-negative.log`. Final combined run above passes.
- Required Linux build matrix: CCP.Core, CCP.Avalonia and all 11 Linux test projects named in `.github/workflows/build.yml` passed (13 projects, every `build exit=0`). Log `/workspace/game-build-matrix.log`, individual logs `/workspace/game-hosts-checks/build-*.log`.
- `dotnet test Tests/CCP.Core.Tests`: **2,849 passed, 0 failed, 6 existing skips** (2,855 total). Five skips require the absent Vosk acoustic model; the existing frame-budget test skips Debug because it is Release-only. No tests were newly skipped or weakened. Log `/workspace/game-core-full.log`.
- `dotnet test Tests/CCP.Core.Tests --no-build --filter FullyQualifiedName~IntakeMicBorrowTests`: **4 passed, 0 failed, 0 skipped**. Log `/workspace/game-core-focused-final.log`.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (same resource flags): **passed, 561 existing warnings, 0 errors**, elapsed 4:40.25. Windows head and test assemblies compiled on Linux; not executed. Log `/workspace/game-wpf-build.log`.
- No Windows execution, live microphone, Vosk acoustic-model recognition or native WebKit/WebView keyboard-focus test was run. Head speech tests use a fake engine and sandboxed profile, never a real microphone or desktop.
- No authored visual layout changed; `--render-all` was not run for this protocol/lifecycle unit. Native focus behavior is compiled but needs a live host check.
- Existing broad Graded Intake parity stub remains; this unit implements only the two named sync rows.

## Changed files

- `CCP.Core/Services/Quiz/IntakeMicBorrow.cs` (moved unchanged from WPF).
- `CCP.Avalonia/Views/Controls/WebHost.axaml.cs`.
- `CCP.Avalonia/Views/Windows/IntakeHostWindow.cs`, `.Bridge.cs`, `.Lifecycle.cs`; `MainShellWindow.PanicKey.cs`.
- `Tests/CCP.Core.Tests/IntakeMicBorrowTests.cs`.
- `Tests/CCP.Avalonia.Tests/IntakeGameEscapeTests.cs`, `IntakeMicHandoverTests.cs`, `IntakeBridgeTests.cs`, `IntakeHostWindowTests.cs`, `IntakeLifecycleTests.cs`.
- `docs/avalonia-main-sync-20261015.md`, `docs/avalonia-parity.md`, this handoff.
