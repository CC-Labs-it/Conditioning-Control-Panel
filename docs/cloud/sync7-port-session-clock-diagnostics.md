# Session clock diagnostics port

- Adapt WPF `293a89197a22f3160e7e193b95f90a2076827944` to the existing Core runner and Avalonia shell. No parity/trunk code was imported.
- Report the first failure of each guarded session step with its full exception through Serilog; count repeats, summarize at stop, and reset at the next session. Avalonia routes these first reports to `logs/crash.log` as well as its regular log.
- Explicit user Start (shell, Start normally, companion, and Resume with the engine off) resumes a parked session after the portal callback starts the engine. Resume does not increment TotalSessions. A connected controller keeps ownership of the engine.
- Reassert pending flash, subliminal, bouncing text and lock-card starts after restart; fit active lock cards to the remaining session window and stop global Mind Wipe.
- Add focused Core and headless regressions plus numbered tester steps. No new surface: no PanicSurfaces registration is needed. Existing panic still pauses the runner and stops the engine.

## Commit disposition

`293a89197`: **ported for available hosts; partially blocked-by-host**.

- Diagnostics: `CCP.Core/Services/Session/SessionRunner.cs:45` (first report/counters), start reset, guarded tick/stop steps and stop summary; `CCP.Avalonia/Program.cs:27` (crash sink). Tests: `SessionRunnerTests.FailingProgress_IsReportedOncePerSession_WithCountsAtStop_AndClockContinues` and `ThrowingRestoreObserver_DoesNotSkipStopNotificationOrSessionLog`.
- Run count: `CCP.Core/CoreEngine.cs:44`. Schedule reassertion: `CCP.Core/Services/Session/SessionRunner.cs:265`. Test: `RestartForResume_DoesNotCountANewRun_AndPendingFeaturesStayOff` checks deferred features, their due starts, and the lock-card window.
- User routing and callback order: `CCP.Avalonia/Views/Windows/MainShellWindow.StartStop.cs:77`, `MainShellWindow.SessionRun.cs:109`, and `CCP.Avalonia/Views/AvatarTube/AvatarTubeWindow.QuickMenu.cs:103`. Tests: `SessionRunWireTests.StartLocksAndLabelsTrayStopPausesResumeRestartsStopOpensTheRecap`, `TubeQuickMenuTests.CompanionStartResumesParkedSessionWithoutCountingAnotherRun`, and `RemoteControlShellTests.ResumeWhileControllerOwnsStoppedEngineOnlyResumesSession`.
- **blocked-by-host:** WPF's session-specific escalating Mind Wipe (`StartSession`, session base multiplier) and pending mandatory-video/bubble-count session timeline entries have no counterpart in this base's `SessionRunner`. Global schedulers exist, but their session timeline hosts do not. This unit does not invent them. Global Mind Wipe is stopped after restart, matching the runner's existing session-start policy.

## Verification limits

- .NET SDK: 10.0.402.
- `dotnet build -c Release` succeeded (exit 0, zero errors) for Core, Avalonia, and every Linux test project referenced by build.yml: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, and the CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, and Snapshot test projects.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3,956 passed, 0 failed, 5 existing Vosk skips (model unavailable). No tests were weakened or newly skipped.
- Focused `--filter FullyQualifiedName~SessionRunnerTests`: exit 0; 15 passed, 0 failed.
- Mutation proof: removing the first-report gate and run-count guard caused both focused regressions to fail (exit 1, two failures). Removing the pending-flash stop caused the schedule regression to fail (exit 1). Removing the snapshot-restore guard caused the stop regression to fail (exit 1). All mutations were restored; the final focused run passed.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed. `git diff --check`: exit 0.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; the script is absent on this base. No ledger rows were edited.
- The read-only original checkout fetch failed (exit 255); the authorized fallback clone and exact-base checkout succeeded. A first build batch exhausted temporary disk because test projects copy the full app resources; generated outputs were removed and every Linux build was rerun successfully in sequence.
- Final `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release`: exit 0; application and tests compiled with 0 errors (266 warnings).
- Windows test build initially failed with MSB4166 (worker exited). Retry: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:UseSharedCompilation=false -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings, 0 errors. WPF application and tests compiled; not executed on Linux.

 Windows-only code cannot be executed on this Linux worker. Headless Avalonia runtime tests are compiled but not run under the supplied font-environment limitation; GitHub rcheck must run the three relevant classes with `DiffEngine_Disabled=true` and filter `FullyQualifiedName~SessionRunWireTests|FullyQualifiedName~TubeQuickMenuTests|FullyQualifiedName~RemoteControlShellTests`.

## Assumptions

- Keep the base's driven-feature subset; do not import sibling lane work or build missing session hosts.
- Use the existing Serilog pipeline and a filtered crash sink rather than WPF's App crash helper in Core.
- Resume under a connected controller resumes only the session, exactly as the WPF review specifies.
- Use the authorized fallback checkout because the supplied checkout rejects writes to FETCH_HEAD. The fallback was verified clean at the exact expected commit before branching.
