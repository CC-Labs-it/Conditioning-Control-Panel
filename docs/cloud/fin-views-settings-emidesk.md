# EMI Desk settings parity follow-up

- Restore offer effects through existing owners: `book:open`, `pinTop:`, `bedtime`, `packs`, and `assets`. Keep unknown/unadapted effects infeasible at draw time. Book opening remains single-instance; pin-to-top uses Core's six-slot eviction rule and rebuilds a live ring; bedtime persists the next local 06:00 in UTC; Assets opens the tab without changing the media source.
- Rebuild settings ring tiles from the shared pin store on settings synchronization. Prefer the bubble side opposite the book and coalesce relayout after book placement, movement, opening, and folding; Core's monitor clip guard retains final authority.
- Restore the summon route/context, silent dismissal moment, five-minute backSoon boundary, local weekend beat, bedtime-broken counter, mute verdict at greeting, empty-library check seven seconds after greeting, and unread-book offer twenty seconds after a second-or-later summon. Recheck media/book state at the tick. Cancel pending follow-ups on dismiss, presentation, panic, and teardown using the existing `emi-desk-speech` panic entry; no new surface type was introduced.
- Add `EmiDeskFinishTests` covering chip-to-effect paths, feasibility, pin eviction/ring refresh, bedtime boundaries, book bubble dodge, settings reload, route/day/dismissal context, delayed beats, media arrival, mute verdict, and cancellation. Tests use the assembly's isolated profile and replace the browser opener.
- Source: behavior adapted directly from WPF at base **45d52d35f3f363514da469a0c6a9bb5ea72b0998**. Fetched/read-only parity trunk **a4ee52d40cd347d288dcf7fdaa8445b79f8e5038** was inspected (`EmiDeskService.Offers.cs`, `EmiDeskWindow.Chains.cs`); no trunk files were imported or existing head files overwritten.

## WPF gap inventory

All reference paths below are relative to `ConditioningControlPanel/`, pinned to the base SHA above. This records the gaps found before implementation and their disposition. The ledger remains **stub**, with one row for this id; this is neither a dependency-only NOOP nor a P122 dead-code claim.

| WPF behavior absent on the starting head | Reference | Disposition |
| --- | --- | --- |
| WPF system-wide summon registration on Windows; native Wayland focus | `Services/EmiDesk/EmiDeskService.cs`, ApplyHotkey | **Missing:** platform backends beyond the existing X11/XWayland chord seam; prior limitation retained. |
| Rebuild pin tiles when settings opens/reloads | `Views/Controls/AppSettings/EmiDeskSettingsSection.xaml.cs`, OnLoaded | Implemented through SyncFromSettings; reuse the existing picker. |
| Book, pin-to-top, bedtime, pack catalogue, Assets effects and draw-time feasibility | `Services/EmiDesk/EmiOffers.cs`, EffectFeasible/Run/OpenBook/SetBedtime; `EmiDeskService.cs`, PinTop | Implemented using the existing book, suggester, shell and URL opener. |
| Prefer the bubble side away from the book; relayout after placement/open/fold | `Windows/EmiDesk/EmiDeskWindow.Bubble.cs`, LayoutBubble/PostBubbleRelayout; `Services/EmiDesk/EmiBook.cs`, SideChanged | Implemented with a direct owner callback; no redundant static event/router. |
| Summon route, elapsed minutes, backSoon, weekend, bedtimeBroken, mute verdict, empty-library beat, book-offer route, silent dismissal event | `Services/EmiDesk/EmiDeskService.cs`, Summon/Dismiss/OnSummonGreetTick/OnEmptyLibraryTick; `Services/EmiDesk/EmiCodex.cs`, MaybeOfferSoon/MaybeOffer | Implemented, including 2600 ms greeting, 7 s empty-library and 20 s book-offer timing. |
| Spiral (6 s timed hold), one non-strict local video plus video contexts, gif rain (10 s), shortwalk/upgrade tour effects and completion latches | `Services/EmiDesk/EmiOffers.cs`, FireSpiral/FireVideo/FireRain/StartTour | **Missing:** head adapters and effect lifecycle integration; existing render/player services alone do not wire these verbs. They continue to refuse. |
| Glass setting reader, idle/activity gating, channel selection/painters/skin, glitch/flip, tap actions, timeouts/declines/wrong-channel reactions and teardown | `Windows/EmiDesk/EmiDeskWindow.Glass.cs`; `Services/EmiDesk/EmiChannels*.cs` | **Missing:** complete glass lifecycle. Its WPF window file is 611 lines and the six channel files are another 2828 lines before adaptation/tests. ChkGlass remains persistence only. |
| Ambient app/feature moments, counters, hold/release arbitration and clock-driven speech | `Services/EmiDesk/EmiDeskService.cs`, Fire/WireAppEvents/OnClockTick/NoteAvatarSpeaking/NoteAwarenessReaction/NoteOpen; `Services/EmiDesk/EmiTargets.cs`, Pick | **Missing:** full bus and producers (XP, takeover, mic, daily free, bark/avatar/awareness, session/video/game/feature and ring events; day-turn/night/morning/long-sitting/idle beats). Existing widget-local producers and newly added summon beats are only a subset. |
| Blipese typing ticks, landed-line synthesis, volume/breaker/hold gating and stop hooks | `Services/EmiDesk/EmiVox.cs`; `Windows/EmiDesk/EmiDeskWindow.Bubble.cs` | **Missing:** audio rendering/playback adapter and bubble voice hooks. |
| Chain body-frame playback, idle gaze/fidget beats and prop presentation | `Services/EmiDesk/EmiChains.cs`, `EmiAlive.cs`; `Windows/EmiDesk/EmiDeskWindow.Alive.cs`, `.Props.cs`, `.Fx.cs` | **Missing:** full frame/idle playback and prop art path. Existing face/speech and summon FX do not provide these behaviors. |
| Other summon/mute integrations: wardrobe refresh, onboarding knock/nudge machine, tube suppression/hand-off, live-service talking probe | `Services/EmiDesk/EmiDeskService.cs`, Summon/TryKnock/StartNudges/AnyTalkingFeatureLive; `.Tube.cs` | **Missing:** these integrations; Core's settings-only mute probe remains in use. |

The remaining set needs more than this unit's 1000-added-line budget. This change prioritizes independently reachable user actions and summon follow-ups, without presenting glass/voice/chain behavior as complete.

## Verification limits

- Required checkout verification found a clean snapshot at a different commit. The prescribed base fetch failed on the read-only snapshot; the prescribed fallback clone was checked out at the exact expected base before creating the authorized unit branch.
- SDK: .NET 10.0.402. The channel installer hit a blocked CI fallback; installation from the release feed succeeded.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. An initial sandboxed invocation could not create temporary files; rerun with writable temporary storage passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0, **2820 passed, 0 failed, 5 skipped, 2825 total**. The five existing Vosk tests require the unavailable model; no tests were changed or newly skipped.
- All twelve workflow test projects built successfully (exit 0, 0 errors each). Native dependency copies initially exhausted the container's disk; sequential builds with generated-output cleanup and `-p:CreateHardLinksForCopyLocalIfPossible=true` resolved that limit. The catalogue project also required a single-node retry after an MSBuild worker error. Final head and Avalonia-test recompiles used `-m:1` and both passed.

| `dotnet build` project under `Tests/` (`-c Release`) | Exit | Error tail |
| --- | --- | --- |
| `CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | 0 Error(s) |
| `CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | 0 Error(s) |
| `CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | 0 Error(s) |
| `CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | 0 Error(s) |
| `CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | 0 Error(s) |
| `CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | 0 Error(s) |
| `CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | 0 Error(s) |
| `CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | 0 Error(s) |
| `CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | 0 Error(s) |
| `CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | 0 Error(s) |
| `CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | 0 Error(s) |
| `ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 | 0 Error(s) |

- Avalonia UI tests are **compiled but not run**, following the supplied cloud font limitation. No before/after UI failure proof or new render-all proof can be claimed here. Supervisor should run `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter "FullyQualifiedName~EmiDeskFinishTests|FullyQualifiedName~EmiDeskOfferTests|FullyQualifiedName~EmiDeskSettingsSpeechTests|FullyQualifiedName~EmiDeskChordTests|FullyQualifiedName~EmiDeskSummonTests|FullyQualifiedName~EmiRingWiringTests"` on the supported runner.
- The WPF application and tests cross-compiled successfully with the required Windows-targeting flags; Windows paths were never executed here. No real desktop, user profile, native chord registration or browser launch is used by the new tests.

## Assumptions

- Keep the existing head's owners and Core behavior; a direct book-to-owner relayout callback replaces WPF's static book event because the widget already owns the single book window.
- Keep unsupported offer verbs infeasible rather than exposing inactive chips. Existing hosts need their own effect adapters before those verbs can be enabled.
- Pending follow-up speech is cancelled on panic/presentation/teardown, extending the existing greeting cancellation so a delayed offer cannot restart speech after a stop.
