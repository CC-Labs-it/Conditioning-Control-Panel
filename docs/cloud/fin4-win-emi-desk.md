# FIN4: EMI desk frame host

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; branch `avalonia-port/cloud-fin4-win-emi-desk`.
- Adapted WPF `Services/EmiDesk/EmiChainPlayer.cs` into the Avalonia head. The Core chain table remains authoritative. Hook ordering, pose overrides, small/flat flags, one-shot FX, per-frame holds, last-frame completion, replacement, cancellation and disposal follow WPF.
- Wired existing PlayChain/Say/ChainLive paths. Summon/dismiss, hover pets and throws now drive face sequences. Completion returns to idle; shutdown cancels the clock.
- Registered `emi-chain` for panic, including deferred-intro cancellation. Dismiss invalidates delayed intro callbacks so wake cannot replace wink.
- Added focused frame-clock and real-window/panic tests. Summon tests preserve hidden-state assertions and assert the live wink. The toss test additionally asserts dizzy before isolating the gentle-drop scenario.
- Consolidated repeated ledger evidence into one honest `stub` row and added a numbered tester step. Full parity is not claimed.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported from it. Adaptations use WPF at the pinned base.

## WPF gap audit

The row's cited XAML/window partials and their service call sites were inspected at the base SHA. Remaining substantive hosts do not all fit the 1,000-added-line allowance; the user-visible frame host is implemented first.

| Behavior | WPF reference | Result / remaining gap |
| --- | --- | --- |
| Visibility, prompt re-entry, placement, counters | service Summon/Dismiss, window placement/React | Already reachable; preserved. |
| Wake/wink/pet/dizzy and Say cadence | EmiChainPlayer, window BuildHooks/PlayChain/Say, Fx/Toss | Clock/hooks now wired. Missing pixel glyph renderer and small/flat visual treatment, body/overlay art loading, canonical body moves and chain FX. Face remains text; pose tracking is not art rendering. |
| Click pats | React:119-194, Alive:751-784 | Sound/count/cooldown exist. Missing live-line guard, wake interruption/FinishSummon bookkeeping, half-second cooldown flick, 4 s poke ladder, glee/chime and rage/truce. Existing hover path now reaches pet/wink sequences; synthetic-hover coverage is absent. |
| Greetings/events | service Summon:335-405, Fire:661; window FireDeskEvent/Toss | Missing moment bus and bubble/voice/ask/offer host. Greeting after wake, knock replacing greeting, backSoon within 5 min, weekend limit, bedtime escalation, dismissed/pat and drag/drop/fling zone/count contexts remain absent. Say face cadence is live but bubble hooks have no implementation. |
| Voice arbitration | service NoteAvatarSpeaking/NoteAwarenessReaction, ArmVoiceHold/OnAvatarHoldTick; Bubble partial | Missing voice delivery and hold/release hooks. WPF polls every second until tube bubble/audio stop, then releases holds with engine tails. Core line engine alone does not deliver a line. |
| Knock | service TryKnock:1071; Core EmiKnockMachine | Machine exists. Missing startup/world wiring, chip pulses, immediate summon, first-contact question/answer/tour hand-off. Spending the once-ever offer without a real question would be wrong. |
| Nudges | service StartNudges/OnNudgeTick/SpeakNudge; Core EmiNudgeMachine | Machine exists. Missing quiet probe, 5 s poll, pin event, delivery/fallback and successful-fire bookkeeping. Pet starts after 25 s; ring after 40 s from summon two; minimum 90 s spacing and lifetime cap six per track. Delivery requires the bubble/ask/voice host. |
| Alive/idle/props | Alive/Props partials; window RestartIdleBeats | Missing gaze/approach/linger/fidgets/stretch poll, blink/sway clocks and prop layout/art/hold/rise/teardown. Needs pure geometry/state extraction plus rendering, not replacement constants. |
| Outfit | window RefreshOutfit/SetPose/PaintOutfitOver | Missing Arcademy Locker store read/push and cached body/overlay rendering. Core paths and shipped web assets exist; older claims that web assets were unlinked were stale. Refresh must occur on construction and every summon. |
| Presentation | Presentation partial; service Dismiss | Already ported, including StopPresentation on dismiss. Broad absence claim was stale. Full interaction parity remains uncertified. |
| Tube hand-off | service Tube partial; window Tube partial; Core TubeDeskVisibility | Lease exists; attached EMI tube host is absent. Base search for SetTubeEmiVisible finds only its WPF declaration/self-marshalling call, no live external caller. No P122 promotion because other live gaps remain. |
| Global chord | service ApplyHotkey; X11SummonChord | Already wired with conflict tests on base. “No X11 chord grab” was stale. Native integration not exercised here. |
| Replay gesture | window ResetOnboarding | No-op pending knock/nudge/line delivery host. |

Remaining reference hosts include Bubble (944 WPF lines), Vox (649), Face (517), Alive (784), outfit and service pipelines. Each needs adaptation and behavioral tests; raw copying exceeds the cap before integration. This branch supplies the missing frame host without claiming dependent hosts are complete.

## Verification limits

Final command results follow below. Tests use isolated `CCP_USERDATA_DIR` profiles and `DiffEngine_Disabled=true`. No test was removed or skipped by this change. The base lacks `.github/scripts/test-plan-coverage.sh`; numbered coverage was checked directly. Windows/native desktop behavior cannot be run here.

- SDK: .NET 10.0.402. The channel installer initially encountered a blocked fallback endpoint; the explicit 10.0 SDK installation succeeded.
- `dotnet build <project> -c Release`: all 15 required projects exited 0, with 0 errors. WPF tests additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. Compiled Windows code was not run.

```text
CCP.Core/CCP.Core.csproj: exit 0
CCP.Avalonia/CCP.Avalonia.csproj: exit 0
Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj: exit 0
Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj: exit 0
Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj: exit 0
Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj: exit 0
Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj: exit 0
Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj: exit 0
Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj: exit 0
Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj: exit 0
Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj: exit 0
Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj: exit 0
Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj: exit 0
Tests/CCP.Core.Tests/CCP.Core.Tests.csproj: exit 0
Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj: exit 0
```

- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.”
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3953 passed, 5 existing Vosk tests skipped, 0 failed. The model is unavailable; speech recognition integration is not validated.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~EmiDeskChainWiringTests|FullyQualifiedName~EmiChainPlayerTests|FullyQualifiedName~PanicSurfacesTests.EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered'`: exit 0; 4 passed, 0 skipped/failed. These tests configure shipped fonts and include panic before the delayed intro reaches wake.
- Regression proof: temporarily restored the original string PlayChain entry point's immediate `done?.Invoke()` behavior. The new wiring test exited 1 (expected callback count 0, actual 1); restored the implementation and reran the focused checks successfully. This is a targeted restoration of the old behavior, not a claim that the whole new test suite ran against an unmodified base.
- Broader filter also included EmiDeskSummonTests, EmiDeskTossTests, EmiRingFanTests, EmiDeskChordTests and PanicSurfacesTests. First run exited 1 with 19 passes and 2 test-assumption failures (dizzy now stays live; forced-render pump time is not elapsed wall time). Tests were strengthened to assert live dizzy/wink while preserving the original gentle-drop and hidden-state assertions. The subsequent run encountered the known fatal `Could not create glyphTypeface` Inter failure under existing bare font bootstrap and stalled; its process was terminated. This broader run is NOT claimed green and must be rerun by the supervisor.
- `git diff --check`: exit 0. The requested coverage script is absent on this base (invocation exited 127); the row has numbered manual coverage in the test plan.

## Assumptions

- Prioritize the reusable frame host in this unit; retain `stub` rather than claiming unported bubble/voice/offer, art, alive, outfit, knock/nudge and tube-host behaviors.
- Preserve WPF frame timing and callback order. Only intro cancellation was hardened because asynchronous intro callbacks must not replace a live goodbye or restart after panic.
- No P122 promotion for the uncoupled tube transfer declaration: other live WPF gaps remain. No native X11/Windows chord grab or real desktop behavior was exercised.
