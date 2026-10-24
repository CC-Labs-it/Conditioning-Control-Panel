# fin2-chaos-announcer

- Base verified at `a65dbbe2f4935a4797b32efd87513054dd39e1d2`; unit branch `avalonia-port/cloud-fin2-chaos-announcer`.
- Restore WPF announcement word-art: 120 DIP uniform image, optional uppercase 26 DIP suffix with 2.2 DIP outline; absent/unreadable art retains the 60 DIP outlined announcement and ignores suffix. Reuse the existing effect-banner resolver and bitmap cache (user assets before bundled assets); preserve queue, timing, scale, work-area anchor, keep-alive and panic behavior.
- Add headless banner/suffix/fallback tests using the assembly's isolated profile. Existing queue, timing, run teardown and panic tests remain intact.
- Replace the duplicated historical ledger entry with current evidence and explicit remaining dependencies; retain `stub`.
- Source review: `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Reviewed its `CCP.Avalonia/Views/Chaos/ChaosArt.cs`; no trunk code imported because the base already has an announcement-art resolver. Behavior adapted directly from the WPF reference at the pinned base.

## Gap analysis and assumptions

At the base, the local rendering gap is word-art plus its dynamic suffix. The remaining gaps are narrator lines and Story interruption (AnnounceNarrator/CutShort), mandatory-video stacking (RaiseActive and ShowNext re-stack), and the other gameplay/teaching/boon/boss announcements in ChaosModeService/ChaosHappyPath. These require the unported narrator/Story director, mandatory-video orchestration, and remaining Chaos gameplay engine respectively. Existing priority queue logic in Core is already shared. No unused narrator or stacking API is added ahead of its caller (ponytail).

The ledger's claim that no user can start a run is stale: `ChaosHubWindow.axaml.cs` calls `ChaosRunHost.StartRun` from the menu flow; `ChaosRunHost.Tick` calls the announcer at T-10 seconds and relapse. Draft-skip and “it noticed.” lines also call the live overlay. New art therefore has a real gameplay path. Panic registration `chaos-announcer` already closes the window and clears its queue.

WPF pinned-base grep evidence: `rg -n 'ChaosAnnouncerOverlay\.(AnnounceNarrator|RaiseActive)' ConditioningControlPanel/Services/Chaos` finds narrator invocation in `ChaosNarrator.cs:55` and video re-stack in `ChaosModeService.cs:759`. `rg -n 'Announce\(' ConditioningControlPanel/Services/Chaos/{ChaosModeService,ChaosHappyPath}.cs` finds 35 call lines. These are live WPF callers, so P122 does not apply. WPF StoryModeEnabled is false, but this does not justify marking the entire row wired while gameplay calls remain unported.

Decision: implement the reachable local rendering behavior and leave dependencies explicit. Preserve the existing resolver's root convention, which matches WPF UserAssetsPath plus the appended assets directory. No broad ChaosArt import or unrelated game-engine port.

## Verification limits

- SDK: .NET 10.0.100 (pinned install succeeded; channel discovery returned HTTP 403).
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.”
- `dotnet test Tests/CCP.Core.Tests -c Release` with `DiffEngine_Disabled=true`: exit 0; 3,953 passed, 5 skipped, 0 failed. The existing five Vosk tests require the unavailable speech model; no tests were changed or disabled for this limitation.
- Initial focused run without bundled-font setup: 6 failed, 3 passed (existing lifecycle tests failed too). Adding the app's bundled Inter/font options allowed the same assertions to execute headlessly. The combined run initially reproduced the issue because the effect-banner class initialized the shared test host first; its setup now uses the same bundled fonts too. Assertions were not weakened.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-restore --filter FullyQualifiedName~ChaosAnnouncerOverlayTests`: exit 0; 9 passed, 0 skipped, 0 failed.
- Failure proof: temporarily compiled the original overlay from the pinned base with the new tests and bundled-font setup. Filter `FullyQualifiedName~ChaosAnnouncerOverlayTests.Banner_replaces`: exit 1; both cases failed specifically with expected StackPanel / actual OutlinedTextBlock. Restored the final source in a finally block before final validation.

Windows-specific code cannot be executed in this Linux environment. No new platform code or new surface is introduced. Full desktop, mandatory-video stacking, Story narration, and the full Avalonia suite were not exercised. The existing primary-screen surface bounds are preserved; WPF spans the virtual screen but anchors the announcement to the primary work area.

Build validation: `dotnet build <project> -c Release` exited 0 with 0 errors for CCP.Core, CCP.Avalonia, and all 12 Linux workflow test projects: CCP.Avalonia.CatalogueStartup.Tests, Consent.Tests, GeneralSettings.Tests, Language.Tests, Migration.Tests, Report.Tests, Scheduler.Tests, Snapshot.Tests, CCP.Avalonia.Tests, CCP.Core.Content.Tests, CCP.Core.Settings.Tests and CCP.Core.Tests. Existing warnings remain. The final focused test command also rebuilds the changed test assembly.

- Final combined regression: `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-restore --filter 'FullyQualifiedName~ChaosAnnouncerOverlayTests|FullyQualifiedName~ChaosEffectBannerOverlayTests'`, `DiffEngine_Disabled=true`: exit 0; 11 passed, 0 failed, 0 skipped.
- WPF test cross-compile: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0; 0 errors, 554 existing warnings. Compiled only; Windows tests/platform behavior were not run.
- `git diff --check`: exit 0. No PR opened; only the unit branch is pushed.
