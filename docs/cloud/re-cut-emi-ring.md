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
- Follow-up book-side bubble arbitration: WPF Windows/EmiDesk/EmiDeskWindow.Bubble.cs:356. Avalonia EmiDeskWindow.Speech.cs:164 currently passes false; EmiBookWindow.axaml.cs:1183 still lacks the side-change bridge.
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

Focused tests: Tests/CCP.Avalonia.Tests/EmiRingSpeechTests.cs:28 (cadence, cancellation and hide), :61 (hold cut and rest), :81 (ring events, live engine draw, first-dot cooldown acknowledgement, panic surface and away hold).

Fail-proof: temporarily removed frame bubble painting, the hold face draw, and the ringOpen emission. All three EmiRingSpeechTests failed at their corresponding assertions (3 failed, 0 passed). Both implementation files were restored immediately. No tests were skipped or weakened.

## Changed files

- CCP.Avalonia/CCP.Avalonia.csproj
- CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskService.RingMoments.cs
- CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskService.cs
- CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskWindow.Ring.cs
- CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskWindow.Speech.cs
- CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskWindow.axaml.cs
- CCP.Avalonia/Views/Windows/PanicSurfaces.cs
- Tests/CCP.Avalonia.Tests/EmiRingSpeechTests.cs
- Tests/CCP.Avalonia.Tests/PanicSurfacesTests.cs
- docs/avalonia-parity.html
- docs/cloud/re-cut-emi-ring.md
