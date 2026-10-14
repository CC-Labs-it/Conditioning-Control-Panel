# wire-win-tutorial-overlay

- Wire automatic advance on button click, edited text, selection content/tag, slider release within the specified range, and the case-sensitive CoreTutorialEvents bus. Detach subscriptions on every advance and close; queued work cannot advance a successor tour.
- Move WPF's numeric/substring text matching into CCP.Core/TutorialAdvanceRules.cs; the WPF overlay and Avalonia overlay call the same implementation.
- Keep lookup usable for dynamically constructed controls without a name scope. Six new control-trigger tests failed with `Could not find parent name scope` before this visual-tree fallback fix (pre-fix targeted run: 6 failed, 8 passed). The existing tour test now uses the production font builder so Linux headless setup matches the shipped head.
- Adapt trigger subscriptions from parity/trunk commit `e5b3976dd6ab2bf215b89c6e0719e7489dd153ed` (read-only trunk tip fetched: `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`). Preserve our Fluent icon mapping. Unlike trunk's initial-emission text observable, subscribe to edits without advancing on a prefilled matching value.
- Keep only ledger row `win-tutorial-overlay` at **stub**, with the named follow-up below. The existing `tour` panic surface and its SafetyCritical registration are retained; no new surface is introduced.

## Verification limits

- SDK: .NET 10.0.402, `PATH=/tmp/ccp-dotnet:$PATH`, `DOTNET_ROOT=/tmp/ccp-dotnet`, `DiffEngine_Disabled=true`.
- `dotnet build CCP.Core/CCP.Core.csproj` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj`: passed on Linux. Builds use `-m:1` and the SDK hardlink copy properties to fit the shared workspace; auxiliary test builds use `-p:BuildProjectReferences=false` after compiling their heads.
- Linux workflow test projects built: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests. Auxiliary suite binaries were removed after recording build results to conserve disk; those auxiliary suites were compiled, not executed.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj --no-build`: passed, 2,820 passed / 6 existing skips / 2,826 total. The existing Vosk speech cases and the existing BoardTiles budget case skipped; no test was changed or newly skipped. Focused `--filter FullyQualifiedName~TutorialAdvanceRulesTests`: 10 passed / 0 failed / 0 skipped.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj --filter "FullyQualifiedName~TutorialAdvanceTests|FullyQualifiedName~TutorialTourTests" -p:BuildProjectReferences=false`: passed, 14 passed / 0 failed / 0 skipped. This includes the seven new trigger cases and existing tour startup/step/Escape/panic tests.
- `bash .github/scripts/core-guards.sh`: passed. `git diff --check`: passed.
- Initial overlapping builds failed with Avalonia output locks; subsequent copies ran out of disk and left a zero-byte generated DLL. Serial hardlinked rebuilds repaired the outputs. Initial xUnit discovery also needed DOTNET_ROOT. These failed attempts are not counted as passes.
- Attempted `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: timed out after 300 seconds. WPF compilation success is not claimed; Windows execution is unavailable here.
- No visual layout or art changed, so `--render-all` was not run. Real desktop pointer forwarding and Windows behavior were not run; tests use the repository's sandbox profile and headless dispatcher.

## Follow-up lane: wire-win-tutorial-overlay-b

The complete unit exceeds this bounded trigger slice. Still missing: an in-window stage and the spotlight click-through hole (the current separate overlay still intercepts highlighted controls); WindowLoaded retarget/fallback and per-step target preparation; the ? panel StartGuide rows; Mod Creator section navigation; EMI narration/EmiTourNarrator; the keyword demo (KeywordTriggerHead stand-in); and WPF's 300 ms entrance / 200 ms exit fades. Keep this row stub until these remaining user paths work and have focused evidence.

## Publication blocked

The implementation commit is `60b43ab7d`. The normal fast-forward publication attempt was:

`git push origin HEAD:refs/heads/avalonia-port/cloud-wire-win-tutorial-overlay`

GitHub rejected it with `Permission to CodeBambi/Conditioning-Control-Panel---CSharp-WPF.git denied to ClearlyNotMich` and HTTP 403. Origin therefore retains only this worker's empty claim; the completed slice is committed locally on the claimed branch. A claim-to-HEAD format-patch is preserved at `/workspace/scratch/wire-win-tutorial-overlay.patch`. Further queue claims are paused while publication is denied.

## Account correction for publication retry

The publication-block notes above describe the earlier `ClearlyNotMich` attempt. The user selected `ObviouslyNotMich` for this retry; that connection reports repository push permission and successfully uploaded a Git blob. The current origin branch head is the publication source of truth.
