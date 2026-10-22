# sync7-engine-fixes-c: temporary decrypt retries and Mantra master gain

- Ported the coherent temp-file and Mantra slice of WPF main commits `333cdca2d`, `41840e44c`, `85188f4a8`, and `a0b7e609b`. No parity/trunk code imported.
- Moved WPF's `MediaTempFileLedger` and `MasterVolumeWatch` (introduced in `f601b7917`) into CCP.Core, adapting their head references to Serilog/CoreSettings. WPF continues using the same shared implementations.
- Flash, mandatory-video and Bubble Count pack pools keep refused deletes on record, retry on subsequent draws/cleanup, and preserve source identity until deletion succeeds. The ledger also retries on every Track/Release below the cap.
- Mantra's synthesized drone and tones start at master gain; the drone follows settings changes live, and closing/panic removes the watch. Gain is applied to this voice, never the app's mixer. Completion timers are canceled on close/panic so an old completed window cannot end the next session. Headless tests use the app font builder so isolated runs have Inter; the original unattenuated-gain test explicitly sets master to 100% and still keeps its exact gain assertions.
- The parity row remains `stub`. Follow-up lane `sync7-engine-fixes-c-next`: row 28 (`42c1696a1`) needs the actual keyword engine (KeywordTriggerHead is currently a stand-in), its preview distinction, and engine-off diagnostic wiring; row 67 (`f601b7917`) still needs Mind Wipe's master gain and live sample gain for the existing `CompanionEffects.PlayAudio` one-shot surface. Importing the keyword engine and completing all of these audio routes would exceed this unit's 1000-line cap. Neither row is marked ported.
- Avalonia has no RemoteMediaCache; online flash/video media remains pending on its existing lane. The shared ledger is ready for that host, and WPF RemoteMediaCache now uses the Core copy. Hosts absent from Avalonia (DtRH/Goon/Arcademy/Back Room) are outside this slice.

## Verification limits

Commands use `PATH="$HOME/.dotnet:$PATH"`, `DiffEngine_Disabled=true` and, for later builds, `DOTNET_PROCESSOR_COUNT=2`, `-m:1`, `-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true`.

- Red proof: `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~RefusedCleanup_RetainsTheDecryptAndRetriesAfterPlayerRelease`: failed before the fix, expected one retained decrypt and got zero. Uses a sandbox file held open on Windows, or a non-writable sandbox directory on Unix.
- Red proof: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~MasterVolume_MutesTheDroneLiveAndScalesNewTones`: after correcting the isolated font setup, failed before the head fix with expected drone volume 26, actual 41. The first attempt failed earlier because bare headless startup did not register Inter; that was not a behavioral proof.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter 'FullyQualifiedName~PackMediaPoolTests|FullyQualifiedName~MediaTempFileLedgerTests'`: 10 passed, zero failed/skipped.
- `dotnet test Tests/CCP.Core.Tests -c Release`: 2850 passed, zero failed, five existing Vosk-model tests skipped (model unavailable); total 2855. No tests were changed to skip.
- `bash .github/scripts/core-guards.sh`: all Core guards passed (420 XAML references checked).
- Builds of `Tests/CCP.Core.Settings.Tests` and `Tests/CCP.Core.Content.Tests`: succeeded, zero errors.

- Additional red proof: `ClosingCompletedWindow_DoesNotEndTheNextSession` failed against the pre-timer-fix head: the previous window's timer ended the next session after its five-second deadline. The final head cancels that timer on close/panic; animation and Escape assertions remain intact.
- Focused final Core tests (`MasterVolumeWatchTests`, `PackMediaPoolTests`, `MediaTempFileLedgerTests`): 11 passed, zero failed/skipped.
- Explicit Release builds of `CCP.Core/CCP.Core.csproj` and `CCP.Avalonia/CCP.Avalonia.csproj` with `--no-restore` and `BuildProjectReferences=false`: succeeded, zero warnings/errors (referenced projects had already built).
- All seven auxiliary Linux Avalonia test projects listed in `.github/workflows/build.yml` built successfully against the built head (`BuildProjectReferences=false`): CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot. Core.Settings and Core.Content test builds also succeeded. These auxiliary suites were compiled, not run. Their generated bin output was removed after successful builds to recover shared disk space; no source/tests were removed.

- Optional WPF test cross-build attempted with `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` plus the resource flags above. Restore succeeded and temporary WPF app compilation printed its warnings, but the overall build did not finish after 23 minutes and was canceled. This is an incomplete build, not a pass; the WPF test assembly and native Windows behavior remain unvalidated.

- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-restore --filter FullyQualifiedName~MantraWindowSessionTests` (building final Core/Avalonia/test sources): seven passed, zero failed/skipped, including master gain, timer ownership, animations, panic, and existing session behavior.

No visual/layout change: render-all was not run. Audio is exercised through fake player/provider seams; no native sound was heard. Windows playback/locked-file behavior cannot run on this Linux host. No desktop or real user profile was touched by the tests.
