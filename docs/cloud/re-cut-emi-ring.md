# EMI ring speech re-cut
Base: 74ac6ce3d5e0ae082c2871c00e864943e15aa2ad. Snapshot initially aa9918833722f057079e455167cb3d1b14462340; fetched the intended branch and reset work before editing.
Sources inspected by own-work range:
- codex-b: 12fa6a4f1197..5d994fe543db (offer, ask, ring speech and follow-up wiring).
- ring-b: a003f9d4be63..a37a692ae368 (ring speech and onboarding).
- ring-c: 12fa6a4f1197..42048690b608 (ring speech, onboarding poll and follow-up work).
- PR #3373: fetched refs/pull/3373/head into origin/pr3373; window dismissal/picker work excluded.
Paths below are relative to the repository. WPF is unchanged.
| Behaviour | Result | WPF spec | Avalonia / Core evidence |
| --- | --- | --- | --- |
| Ring open, dismiss and third ignore moments | Ported | ConditioningControlPanel/Windows/EmiDesk/EmiDeskWindow.Ring.cs:106,287,293 | CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskWindow.Ring.cs:56,130 |
| Moment observers, live draw, hold while away | Ported | ConditioningControlPanel/Services/EmiDesk/EmiDeskService.cs:661 | CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskService.RingMoments.cs:17 |
| Say cadence, bubble, cancellation | Ported from ring-b, adapted to existing Core frames | ConditioningControlPanel/Windows/EmiDesk/EmiDeskWindow.xaml.cs:1071; EmiDeskWindow.Bubble.cs:503 | CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskWindow.Speech.cs:26,82,113; CCP.Core/Services/EmiDesk/EmiChains.cs:519 |
| First-dot acknowledgement, wordless holds | Ported | ConditioningControlPanel/Windows/EmiDesk/EmiDeskWindow.Bubble.cs:503 | CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskWindow.Speech.cs:93 |
| Panic silence | Ported, preserves base panic inventory | ConditioningControlPanel/MainWindow/MainWindow.xaml.cs:1620; MainWindow.RemoteControl.cs:1479 | CCP.Avalonia/Views/Windows/PanicSurfaces.cs:46; EmiDesk/EmiDeskService.RingMoments.cs:44 |
| Codex content and offer brakes | Already on base | ConditioningControlPanel/Services/EmiDesk/EmiCodex.cs:109,698 | CCP.Core/Services/EmiDesk/EmiCodexRules.cs:28,48 |
| Ring window dismissal and picker pin wiring | Covered by PR #3373, excluded | ConditioningControlPanel/Windows/EmiDesk/EmiRingWindow.xaml.cs:415,1209; Views/Controls/EmiRingPicker.xaml.cs:165 | origin/pr3373:CCP.Avalonia/Views/Windows/EmiDesk/EmiRingWindow.axaml.cs:363,1197; Views/Controls/EmiRingPicker.axaml.cs:158,180 |
## Part B
- Her-book offer and ask UI: WPF Services/EmiDesk/EmiCodex.cs:656,694; EmiDeskService.cs:870; Windows/EmiDesk/EmiDeskWindow.Bubble.cs:580. Avalonia still lacks EmiDeskService.Offers.cs and EmiDeskWindow.Ask.cs; reuse codex-b versions. Core probes remain unset (CCP.Core/Services/EmiDesk/EmiLineEngine.cs:68,74) so no dead offer chips are enabled.
- Pin and ambient onboarding: WPF Services/EmiDesk/EmiDeskService.cs:1205,1250,1304,1505. Avalonia EmiDeskService.RingMoments.cs contains only the moment bus, no nudge scheduler. Reuse ring-c's RingMoments.cs poll and delayed pin recheck, retaining base safety.
- Book-side bubble arbitration: fixed in the review follow-up below.
- External-open scoring and ringPick/pinAdded speech: WPF Services/EmiDesk/EmiTargets.cs:279 and EmiDeskService.cs:1505. Avalonia EmiTargets.cs still lacks the lane's moment bridge; coordinate with PR #3373's pin rebuild to avoid duplication.
- Cue suppression follow-up: WPF Services/EmiDesk/EmiSfx.cs:156,161 gates suppressed output and holds. Base Avalonia EmiSfx.cs:97 currently gates only master volume. The ring-c stricter gate remains part B.
- Named body/FX chains and voice remain outside this self-contained speech cut. Avalonia EmiDeskWindow.axaml.cs:892 is still a named-chain stub.
No new language keys. No safety or Scrolller code imported from old lanes. Core remains head-independent.
## Validation
- dotnet-install --channel 10.0: default latest-version feed returned 403; allowed builds.dotnet.microsoft.com feed installed SDK 10.0.403.
- dotnet build CCP.Core: 0 errors (50 warnings).
- dotnet build CCP.Avalonia -m:1: 0 errors (201 warnings).
- dotnet build Tests/CCP.Avalonia.Tests -m:1 -t:Rebuild: 0 errors (396 warnings).
- dotnet test Tests/CCP.Core.Tests: Failed 0, Passed 4635, Skipped 6, Total 4641.
- Affected Avalonia filter: EmiRingSpeechTests, EmiRingWiringTests, EmiDeskPatFeedbackTests, PanicSurfacesTests, TestIsolationTests. Failed 0, Passed 28, Skipped 0.
- Initial overlapping builds produced stale XAML artifacts and runtime loader failures; the serial rebuild and run above passed.
- bash .github/scripts/core-guards.sh: All Core guards passed (temporary-file writes require execution outside the read-only sandbox).
- python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.
- git diff --check: passed.
Focused tests: Tests/CCP.Avalonia.Tests/EmiRingSpeechTests.cs:89 (cadence, cancellation and hide), :122 (hold cut and rest), :142 (ring events, live engine draw, first-dot cooldown acknowledgement, panic surface and away hold).
Fail-proof: temporarily removed frame bubble painting, the hold face draw, and the ringOpen emission. All three EmiRingSpeechTests failed at their corresponding assertions (3 failed, 0 passed). Both implementation files were restored immediately. No tests were skipped or weakened.
## Changed files
- CCP.Avalonia/CCP.Avalonia.csproj
- CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskService.RingMoments.cs
- CCP.Avalonia/Views/Windows/EmiDesk/EmiBookWindow.axaml.cs
- CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskService.cs
- CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskWindow.Ring.cs
- CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskWindow.Speech.cs
- CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskWindow.axaml.cs
- CCP.Avalonia/Views/Windows/PanicSurfaces.cs
- Tests/CCP.Avalonia.Tests/EmiRingSpeechTests.cs
- Tests/CCP.Avalonia.Tests/PanicSurfacesTests.cs
- docs/avalonia-parity.html
- docs/cloud/re-cut-emi-ring.md
## Review follow-up
P2 accepted: WPF Windows/EmiDesk/EmiDeskWindow.Bubble.cs:356 reads EmiBook.SideOfHer, with the Core screen-edge rule retaining final say. WPF Windows/EmiDesk/EmiBookWindow.xaml.cs:1093 announces placement changes; Services/EmiDesk/EmiBook.cs:147,179,190 announces open/close changes. Avalonia EmiDeskWindow.Speech.cs:166 now reads its owned book; EmiBookWindow.axaml.cs:1183 notifies its owner on placement side changes, and EmiDeskWindow.axaml.cs relayouts on open and both close paths. No new language keys or WPF/Core changes.
Focused test: EmiRingSpeechTests.BubbleDodgesBookOnBothSidesAndStaysInTheWorkArea checks a live bubble across right/left book placement, opening and both close paths, and clamps long lines at both monitor edges.
Review fail-proofs: replacing the book-side preference with false failed the focused test at line 60; independently removing the book placement notification failed it at line 66. Both runs: 1 failed, 0 passed, 0 skipped. Both implementation files restored afterwards. The test invokes the book's real follow-move handler because the headless Position setter does not raise the widget's drag-only Moved event.
Review checks: Core build 0 errors; Avalonia build 0 errors; Core tests 4635 passed, 6 pre-existing skips, 0 failed; Core guards passed; avalonia_docs self-check OK; git diff --check passed. Ledger statuses remain stub. No new language keys.
Restored review build: 0 errors. Final affected Avalonia run (speech, book navigation, ring wiring, pat feedback, panic surfaces, isolation): Failed 0, Passed 36, Skipped 0, Total 36.
## Windows CI follow-up
Run 41181036820 failed ContentHitTestTests and CompanionEditorsTests on stale/moving compositor hit geometry, and PresetsFocalFxTests on an entrance still owning row Y after Motion Off (expected -2, actual 0.005325). Artifact upload and Node notices were unrelated.
Pointer tests now finish the finite tab entrance and await the committed frame before calculating/clicking coordinates. All hit, click, and editor-reachability assertions remain. The preset motion gate cancels in-flight entrances, matching WPF MainWindow.TabFxPresetsQuestsAchievements.cs:437-440 (remove animation before snapping the hover offset). The existing Off test now starts a fresh entrance before toggling Off.

The content test also uses Program.BuildAvaloniaApp so first-test order cannot omit font collection setup. Release builds: Core, Avalonia, and Avalonia tests each 0 errors. Core tests: 4636 passed, 5 pre-existing skips, 0 failed. The three CI-failing classes plus EMI speech: 15 passed, 0 skipped, 0 failed on Linux. Windows execution remains CI-only in this environment.

CI fail-proof: the Off test now also asserts cancellation of the fresh entrance token, independent of frame speed. Removing OnFxMotionGateChanged cancellation failed at PresetsFocalFxTests.cs:77 (1 failed, 0 passed); implementation restored. All original scale/offset assertions remain.

Full Linux shard 0/2 attempts hit fatal glyphTypeface initialization failures (Inter/Segoe UI Black/Consolas) in other test bootstraps; testhosts were stopped after the fatal errors, so these are not passing shard results. No test or assertion was disabled. Windows CI still needs to run the pushed fix.

Final restored Release targeted run, including the cancellation assertion: Failed 0, Passed 15, Skipped 0, Total 15.
