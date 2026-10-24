# fin2-chaos-unlock-card

- Add the interactive lesson unlock-card scrim, reusing the hub card builder, Core copy/cues, and shared beat. Match WPF click-anywhere dismissal, queue swaps, hidden kept-alive window, primary-work-area placement within a virtual-screen scrim, 160 ms in / 240 ms pop / 150 ms out, and 12-second timeout only for pause-free cards.
- Subscribe to real lesson completions in ChaosRunHost. Hold clock, spawns, field movement/input and toy use until the final card leaves; preserve a manual pause. Drop stale posted events after teardown. Panic registration and run cleanup discard pending callbacks.
- Resolve optional library/shipped art, falling back to the existing glyph tile; copy existing non-staging boon and habit PNGs. New ChaosArt.cs adapted from read-only parity/trunk commit `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no existing head file was overwritten.
- Reuse Windows passive styles through a small platform seam: clickable NOACTIVATE without forcing the pin-on-top setting. Existing OverlayBackendDispatchTests covers the passive style bits.
- Add focused headless tests for the real lesson event, queue, click-only pause cards, pause-free timeout, fade-time arrivals, manual-pause ownership, panic/teardown, art lookup and fallback. Preserve the existing hub timeline test and add the surface to the panic safety list.
- Replace the repeated historical ledger text with one honest `stub` row. Remaining gaps are dependencies: draft/ready-GO card deferral requires the unported boon draft; preserving a live freeze pickup's remaining time requires the unported freeze pickup. Other lesson producers remain in their field/payload lanes.

## WPF gap audit

Pinned reference: base `a65dbbe2f4935a4797b32efd87513054dd39e1d2`, Chaos/ChaosUnlockCardOverlay.cs and Services/Chaos/ChaosModeService.cs:131-210, 1590-1625, 3175, 3228, 3347.

Before this unit, the head lacked the interactive full-screen scrim and hint; centered 420-DIP card and 0.9-to-1 pop; 160/150 ms fades; left-click dismissal; conditional 12-second timeout; callback queue with direct swaps and arrivals during fade; kept-alive hide/reuse; callback-dropping teardown/panic; lesson-event caller and pause ownership; sprite resolution. Those are implemented. The two cross-feature handoffs above remain absent, so `wired` would overstate parity. The hub's 400-DIP, noninteractive 4.5-second purchase announcements remain separate and unchanged in timing.

## Verification limits

.NET SDK 10.0.402 installed with the requested channel installer. DiffEngine_Disabled=true for test commands.

- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- Release builds: CCP.Core, CCP.Avalonia, Tests/CCP.Core.Tests (built by test), Tests/CCP.Avalonia.Tests, Tests/CCP.Core.Settings.Tests, Tests/CCP.Core.Content.Tests, Tests/CCP.Avalonia.Consent.Tests, Tests/CCP.Avalonia.Language.Tests, Tests/CCP.Avalonia.CatalogueStartup.Tests, Tests/CCP.Avalonia.Migration.Tests, Tests/CCP.Avalonia.Scheduler.Tests, Tests/CCP.Avalonia.Report.Tests, Tests/CCP.Avalonia.GeneralSettings.Tests, Tests/CCP.Avalonia.Snapshot.Tests: exit 0, zero errors. Existing warnings remain. An initial overlapping dependency build hit an output-file lock; serial rebuilds resolved it.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 554 warnings, zero errors. Windows UI/native styles compiled but cannot run here.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 3,952 passed, 1 failed, 5 skipped. Failure: DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls, expected Loops but got Clips at line 185. No Core or existing Core-test source changed. Vosk-dependent tests were skipped by the existing suite (model unavailable).
- Focused Core rerun with `--no-build --filter 'FullyQualifiedName~ChaosUnlockCardRulesTests|FullyQualifiedName~DtrhRemoteMediaTests.Take_ColdCacheFetches'`: exit 1; all 3 unlock-card rule tests passed, the same unrelated DTRH test failed.
- Pure platform-style tests: `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~OverlayBackendDispatchTests`: exit 0, 7 passed, no windows/fonts created.
- Window/font-dependent headless execution NOT RUN per the supplied cloud font-creation limitation. CI command: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~ChaosUnlockCardOverlayTests|FullyQualifiedName~ChaosUnlockCardLayerTests|FullyQualifiedName~ChaosRunHostTests|FullyQualifiedName~PanicSurfacesTests|FullyQualifiedName~OverlayBackendDispatchTests'`. Test assembly compiles with zero errors. Runtime assertions and before-fix behavioral failure proof therefore remain unverified here; no tests were weakened or disabled.
- Real desktop, mixed-DPI multi-monitor geometry, native activation and audio behavior were not exercised. No Windows tests or render-all run was attempted.

## Assumptions

- WPF source takes precedence over the stale ledger's blanket timeout wording: a pause-holding card never times out.
- Keep the row `stub` while draft and freeze handoffs depend on unported features; implement the independent user-visible gaps now.
- Reuse this head's combined field freeze/input hold; no separate pause abstraction or speculative draft implementation.
