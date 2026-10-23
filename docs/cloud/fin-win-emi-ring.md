# fin-win-emi-ring

- Restore desktop click-away and Escape through the options panel's existing 30 ms, non-consuming pointer snapshot. Publish card regions in physical pixels after layout/rebuild/show; exempt the desk silhouette; clear input and polling at fold, panic and close. Windows reads only pointer buttons/Escape and retains no-activate on interactive cards. Headless windows never read native desktop input.
- Restore WPF BackEase EaseOut amplitude 0.30 on translation, keeping scale cubic and opacity quadratic. Preserve the 62 ms stagger/340 ms move/210 ms fade and reverse 26/190/150 ms fold, with its 40 ms tail.
- Restore Intake and Just Drop catalogue entries now that this base has their hosts. Intake reuses the existing pass/AI gates and single-run launcher; locked cards retain WPF's Lab refusal. Just Drop remains unavailable until its existing server flag opens the door and uses the shell's launcher. Existing `intake` and `games` panic registrations own those hosts; the existing `emi-ring-speech` registration now folds the ring too.
- Resolve ring/pat cues through the already-seeded Core mod-audio override before shipped fallback, retaining candidate order, gain, throttle and silence gates.
- Add focused input/easing, door and cue tests; strengthen catalogue and panic assertions. Keep one `win-emi-ring` ledger row, honestly `stub`.
- Import credit: no trunk code imported. Inspected read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; its ring still lacks these input paths and its older icon/text changes were not imported. Adapted the frozen WPF reference at base `42048690b60804eb9d3226cf08873f96a796a79b` into the existing head files.

## WPF comparison and remaining gaps

Reference: `ConditioningControlPanel/Windows/EmiDesk/EmiRingWindow.xaml` and `.xaml.cs`, plus the live callers in `EmiDeskWindow.Ring.cs`, `Services/EmiDesk/EmiTargets.cs`, `EmiSfx.cs`, `EmiDeskService.cs` and `Windows/EmiDesk/EmiDeskWindow.React.cs`, all at the base SHA above.

The initial row understated several gaps: desktop-wide dismissal/Escape and gap pass-through, the gentle opening overshoot, Intake/Just Drop entries, mod cue resolution, and panic leaving the fan open. Those are addressed in this unit. Composition, card/pin/lock appearance, left-release picking, follow-resize/drag-fold, reverse fold and interrupted reopen, scoring, ring moments, delayed pin teaching, ambient nudges and book dodge were already present and retained. Right-click on a card stays inert; no card pin gesture is reintroduced.

Remaining WPF behavior is reachable there, so this is neither a P122 dead-caller claim nor `wired`:

| Missing behavior | Dependency / evidence on this base |
| --- | --- |
| Arcademy, DTRH, Goon and Back Room launch cards, including Arcademy's specific pick moment | `CCP.Avalonia/Views/Games/GameWindow.cs` registers only breakoutdemo, fyp, justdrop, piecebypiece and race. `GoonTestWindow` is a development cockpit with stub services, not the production Goon host. Production host/bridge ports and their web/art resources must land before these cards are offered. |
| Spiral card: six-second ad-hoc overlay, opacity floor 15%, overlap bump and restoration without changing settings | `SpiralOverlay.Refresh` is sustained/session-driven. There is no Avalonia `ShowOverlayTimed` or timed hold/bump/restore API. WPF `EmiTargets.FireSpiral` calls `ShowOverlayTimed("spiral", 6000, opacity)`; the underlying overlap rules are in `Services/Notifications/OverlayService.cs`. Reusing the sustained enable switch would change user settings and timing. |
| Named body/FX chains, voice and actionable offer chips alongside ring moments | `EmiDeskService.RingMoments.DrawRingMoment` draws text only and explicitly refuses asks via the unset feasibility probe. `EmiDeskWindow.RunBodyMove` lacks the five named moves. Requires the reaction/voice/offer director owned by `wire-win-emi-codex-b`. |
| Endpoint-failure silence and recovery | `CoreAudio.IsOutputSuppressedProvider` remains unseeded by the Avalonia head. WPF `Audio/AudioService.Playback.cs` supplies a five-failure/30-second breaker, worker-wedge detection and endpoint-change recovery; `LibVlcAudio` has no corresponding lifecycle. The ring's existing provider/volume/hold gate remains in force; no fake device-health flag is introduced. |
| Every very short desktop press and native Wayland click-away | The reused snapshot is a 30 ms poll; a complete press/release between ticks can be missed. Native Wayland clients are outside X11's reach. Full event-observer/backend work remains separate. |

WPF's declared `PinToggled` event has no raising caller in `EmiRingWindow.xaml.cs`; options already reach `NotePinMadeElsewhere`. No unused event is added. Avalonia-specific text rasterization and the established Fluent lock icon remain the head's existing adaptations.

## Verification limits

- SDK: .NET 10.0.402. Exact required base verified before creating the requested branch; the read-only snapshot required the authorized writable clone fallback.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 2,879 passed, 5 existing Vosk-model skips. The model cannot be downloaded in this environment.
- Before fixes: easing filter exited 1 (5 failures: no WPF easing member); restored-door filter exited 1 (missing Intake entry); cue filter exited 1 (shipped cue chosen instead of mod override).
- After fixes: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~EmiRingDoorTests.RestoredDoorsFollowLiveAvailabilityAndIntakePass|FullyQualifiedName~EmiRingCueTests|FullyQualifiedName~EmiRingInputTests.PopMoveUsesWpfAmplitudePointThree|FullyQualifiedName~EmiRingWiringTests.CatalogueHidesDoorsWithNoSurfaceAndComposesInTableOrder'`: exit 0, 8 passed. These checks need no fonts, display or real audio device.
- Final run adds `OverlayBackendDispatchTests` to the same filter: exit 0, 15 passed (including the existing passive Windows style policy).
- Full headless test assembly compiles. New window/input/host/panic tests and existing ring UI suites were **not run**, following the supplied cloud font limitation; they require supervisor rcheck. No UI runtime or render-all success is claimed.
- Windows pointer/no-activate/input-region code is compiled as part of the Avalonia head, not run. Real X11 click-through, native Windows input/focus, mixed-DPI interaction, actual audio and network-host behavior were not exercised.
- Complete workflow project build results are recorded below. A concurrent intermediate Avalonia compile hit a PDB sharing error; final builds are sequential.

## Assumptions

- Reuse the existing options-panel snapshot and region helpers as the smallest working desktop path; retain explicit polling/Wayland limits instead of claiming hook equivalence.
- Reuse the head's existing Intake launcher and its first-run duck behavior for the ring; do not create a second host or bypass its AI/pass gates.
- Keep doors hidden when their production host or timed overlay API is absent, and leave the row `stub` until the listed dependencies and event-level input gaps are closed.

## Workflow build results

All commands use `dotnet build <project> -c Release --nologo -v:minimal`; every build exited 0 with `0 Error(s)`. Existing warnings were retained.

| Project | Exit | Result tail |
| --- | --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 | `0 Error(s)` |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 | `0 Error(s)` |

The WPF test build additionally uses `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. It compiles on Linux (512 existing warnings) but was not executed. Generated output for unrelated test projects was removed after each successful build to keep the workspace within disk limits.
