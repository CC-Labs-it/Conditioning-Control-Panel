# fin4-views-settings-emidesk

- Base verified at `45d52d35f3f363514da469a0c6a9bb5ea72b0998` after correcting the clean snapshot checkout.
- Added WPF front-row pin offers using existing Core `EmiSuggester.PinToTop`: catalogue validation, six-pin eviction, immediate open-ring rebuild, and `pinAdded` reaction. Already pinned targets refuse at draw time.
- Added bedtime offers using Core `EmiBedtime`: next local 06:00 stored in UTC, persisted with `EmiState.SaveSoon`, then `bedtimeSet` with `fromAsk`. The existing Core engine owns the quiet gate. No new window, timer, OS service, or panic registration is introduced; existing offer cancellation/panic paths remain in use.
- Added chip-path regression tests for both effects and Core boundary tests before, exactly at, and after 06:00 across a month boundary. Both new head tests failed before the implementation at their feasibility assertions.
- Kept `views-settings-emidesk` at `stub`; added numbered tester steps. Removed a machine-specific evidence path from the edited ledger row.
- Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk implementation imported; adapted WPF references from the pinned base.

## WPF gap audit and scope

The cited settings XAML/code-behind were read against the Avalonia section. Editor order, copy, persistence, spice clamping, mute/don't-ask reset, enable/dismiss/rebind, chord capture and the shared ring picker are already present. The section is reachable in `AppSettingsTabView.axaml`. Its picker rebuilds on Loaded. The remaining gaps belong to the downstream widget/service, not additional settings editors.

At the pinned base the ledger names these live gaps:

1. Offer effects: `spiral`, `video`, `rain`, `tour:`, `book:`, `pinTop:`, `bedtime`, `packs`, `assets`. This unit implements `pinTop:` and `bedtime`; the other seven remain refused at draw time. Several visual hosts exist but their one-shot orchestration, feasibility and moment routes still need adaptation.
2. `EmiDeskGlass`: saved with no reader. WPF `EmiDeskWindow.Glass.cs:256` gates on visibility, input/transit/chain/ask/bubble/ring/video/hold, the preference, bedtime, and channel availability. Missing: idle trigger, flip/skin, channel selection, painters, tap-to-effect, return/teardown. Its host is 611 lines; channel files add 2,828 lines at this base. Porting this complete behavior with meaningful tests exceeds the unit cap even before other gaps.
3. Full ambient moment/counter bus: WPF `EmiDeskService.WireAppEvents`, clock/day/idle events, progression, autonomy, speech, daily-free and bark subscriptions, matching teardown and hold release. Head `Fire` exists but these producers do not all call it. Some live services remain unported.
4. Blipese voice and speech synchronization; WPF's voice bus/awareness hold behavior is not supplied by the head's bubble cadence alone.
5. Book-side preference and bubble/chip dodge: WPF `EmiBook.SideChanged`/`SideOfHer`; head `LayoutBubble` still passes false to placement and the book owner documents the missing side notification.
6. Body/overlay chain-frame playback and alive/idle beats: head `PlayChain`/`ChainLive` are stubs; Core speech draws wordless face/chain requests but the full player/art/frame timing is absent. WPF `EmiChains.cs` alone is 688 lines.
7. Summon beats beyond greeting: WPF knock handoff, book-on-summon, return-within-five-minutes, weekend, bedtime-broken escalation, empty-library beat and associated timers/cancellation. Related nudge/knock and avatar-tube handoff lifecycle are incomplete on this head.
8. Mute live-service half: the head explicitly uses shared settings-only `EmiMuteRule.SettingsTalk`; WPF also probes live autonomy/remote talking features. The settings toggle itself is functional.

These paths have live WPF callers; P122 does not apply. The row cannot honestly be promoted to wired. This is a partial implementation with remaining scope, not a pure-dependency NOOP.

## Verification limits

Commands used .NET SDK 10.0.100 and `DiffEngine_Disabled=true` for tests.

- `dotnet build <project> -c Release`: exit 0, zero errors for CCP.Core, CCP.Avalonia, and all eleven Linux test projects named in build.yml: Core.Tests, Core.Settings.Tests, Core.Content.Tests, Avalonia.Tests, Avalonia.Language.Tests, Avalonia.CatalogueStartup.Tests, Avalonia.Scheduler.Tests, Avalonia.Report.Tests, Avalonia.Consent.Tests, Avalonia.GeneralSettings.Tests, Avalonia.Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 existing warnings, zero errors. WPF head and tests compiled, never executed on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed. The first attempt lacked writable temporary files; rerun with normal workspace write access passed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~EmiDeskOfferTests|FullyQualifiedName~EmiDeskSettingsSpeechTests|FullyQualifiedName~EmiDeskChordTests'`: exit 0, 15 passed, zero failed/skipped. The two new effect tests failed before implementation (both feasibility assertions false); final chip-path tests pass. Font creation worked in this runtime.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1, 2,822 passed, one failed, five skipped. Repeating with `--no-build` gave the same counts. Failure: `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch`, line 80 (ScheduleAt returned null). Its deadline uses exactly the minimum scheduling buffer, so elapsed time between timestamp creation and scheduling can cause refusal; this unit does not change that clock or test.
- Final serial `dotnet test Tests/CCP.Core.Tests -c Release --no-build` after all competing work completed: exit 0, **2,823 passed, zero failed, five skipped**. Earlier scheduling failures remain recorded above.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~EmiBedtimeTests`: exit 0, three passed.
- A combined GoonClockAndRoundsTests/EmiBedtimeTests recheck: exit 1, 14 passed, the same scheduling test failed. An isolated pinned-base run of that scheduling test passed (one test); the full pinned-base suite passed (exit 0, 2,820 passed, five Vosk skips). The current-branch scheduling test also passed in isolation (exit 0, one passed). This establishes intermittent scheduling behavior, not a consistently failing base test.
- Five pre-existing Vosk cases skipped because the model is absent; no tests were weakened, deleted or newly skipped. No Vosk download attempted (the task documents the host's 403).
- `git diff --check`: exit 0. The base has no test-plan-coverage script; numbered row coverage was added in the expected format.

Real desktop hotkey behavior, manual tester steps, full Avalonia suite, render-all, and Windows runtime behavior were not exercised. Remaining parity scope is listed above.

## Assumptions

- Prefer the pinned WPF behavior and reuse existing Core pin/state/quiet-gate logic. Keep remaining unported offer effects hidden rather than creating dead chips.
- Local 06:00 uses the runtime time zone, matching WPF `DateTime.Now` and `ToUniversalTime` (including its daylight-saving conversion).
