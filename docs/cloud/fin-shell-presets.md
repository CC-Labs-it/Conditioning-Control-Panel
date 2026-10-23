# fin-shell-presets

- Added the session corner-GIF option to the Presets rack: GIF picker, corner, opacity and size controls; size edits rest for 400 ms before recreating the overlay. Position and opacity edits keep the realized window and animation.
- Added session start/end timing, live admission, pause/resume, panic teardown, and terminal handback to standalone slots. The user's session-GIF master, pre-session Spiral master, fullscreen spiral and standalone occupancy all participate in admission. Session art never arms the standalone crash-restore sentinel.
- Added session spiral enable/delay/ramp behavior; constants leave the Studio opacity slider live, and ramps leave persisted opacity alone. Repaint uses the existing overlay and opacity curve.
- Added immediate/delayed session bubbles, five-minute frequency steps, intermittent bursts at twice the per-burst count, pause/resume and snapshot restoration. Bursts retain WPF's wall-time duration.
- Added focused Core tests and a headless rack-to-overlay test, including animation, live edits, debounce, sentinel and panic assertions. The existing `corner-gif` panic registration now closes the runner's claim too.
- Adapted imports from read-only `parity/trunk`: `7bb4351d8d9c7d1a4d77cfa893a01d45e8bbbb0b` (session corner lifecycle, admission and surface seam), `7fdf53167ee5f9e491bf6f7b7145a48e0d0b2250` (session controls), `35c231e66a70e63cbcd592ef159022ca1d0e0b39` (spiral/bubble phases). Trunk inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; spiral/bubble behavior was adapted against the WPF source, retaining this base's ramp curves and wall-time bursts. Existing base files were merged, never replaced.
- Kept the single `shell-presets` ledger row `stub`: a user path still does not reach all live WPF behavior. No PR was opened.

## Verification limits

- Checkout mismatch and read-only fetch were handled with the requested independent clone fallback, then the exact base commit was verified before source work. SDK: .NET 10.0.402.
- Spiral fail-proof: before the implementation, `SessionSpiralHoldsDelayedStartAndRestoresUserSettings` failed `Assert.False` because the user's running spiral stayed enabled during a delayed start.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`, with `DiffEngine_Disabled=true`: exit 0; 2,830 passed, 0 failed, 5 existing Vosk skips. No new skips were added.
- Build matrix: all 14 projects below exited 0 with zero errors, using `dotnet build <project> -c Release`. WPF additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; it was compiled, not run.
- Final `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release -m:1`: exit 0, zero errors. A preceding attempt lost an MSBuild child node after WPF outputs consumed the remaining temporary storage; the single-node retry succeeded after output cleanup.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~SessionRunnerTests -m:1`: exit 0; 25 passed, 0 failed, 0 skipped (including the WPF pre-show opacity edge case).
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- Initial batch builds exhausted temporary storage through duplicated native runtime outputs; an overlapping Core run failed with disk I/O errors. Generated outputs were removed, builds rerun sequentially with per-project output cleanup, and the full Core suite rerun. No test was weakened, skipped or deleted.
- Headless Avalonia classes compiled, not run: `SessionCornerGifWireTests`, `SessionRunWireTests`, `CornerGifOverlayTests`, `PresetsWallParityTests`, `PresetsTabSessionDetailTests`, `PanicSurfacesTests`. The supplied environment limitation identifies font creation failures, and the cloud-test-fonts lane is absent from this base. The supervisor must run these on GitHub with `DiffEngine_Disabled=true`.
- No desktop/X11/Windows overlay execution, native picker interaction or render-all was performed. Windows code can only be compiled here. Vosk model acquisition is known blocked (403); speech coverage cannot be claimed.


## WPF audit and remaining work

Reference pinned at base `d232f98fd5eb863d6366f9e4e1dc97792772bb49`: `ConditioningControlPanel/MainWindow/MainWindow.Presets.cs`, plus its live session implementation in `Services/Session/SessionEngine.cs`, corner admission in `Services/Media/CornerGifMedia.cs`, and dashboard click handlers in `Views/Tabs/SettingsTabView.xaml.cs`.

| Behavior missing on the base | Result |
| --- | --- |
| Session corner option, picker and live position/opacity/400 ms size edits | Added; existing overlay decoder, placement and display-change retry reused. |
| Session corner start/end, admission, pause/resume, panic and handback | Added; Core lifecycle and existing overlay queue share ownership. |
| Session Spiral enable, randomized start and opacity ramp | Added; shipped default art means the WPF no-file refusal is unnecessary on this head. |
| `EnablePinkFilter` / `UpdatePinkFilterOpacity` | Already implemented by Core settings writes, `PinkOpacity` and the head's refresh; no unused shell wrappers added. |
| `ApplySessionSettings` editor repaint | Ported feature panels already subscribe to settings changes; new writes use that path. No duplicate shell settings loader added. |
| `SetBubblesActive`, session bubble timing/ramp/bursts | Added through existing `CoreBubbles` actions; no new surface. |
| Brain Drain enable/intensity/audio/compositor actions | Missing: depends on unported Brain Drain service and capture/compositor surface. Settings-only controls would falsely imply a running effect. |
| `RefreshMosaicTierBadges` / `RefreshMysteryTile` | Missing: mystery reveal name/art/tier/free stamp repaint and triggers, Bubble v2 ownership badge and opportunistic daily-free refresh. Entitlement and daily-free services already exist; this is wiring work, not a missing-Patreon dependency. |
| Dashboard click-choice popup and inverted routing | Missing: hover/pin/450 ms close/Escape behavior, persistence, swapped caption and per-half click/toggle routing. |
| Session program/punch-card/companion hooks | Program completion/end bridge and row refresh already exist. Missing: start/end announcements, intake punch-card completion and companion finished notification. |
| Recap/editor/confirmation details | Missing: video teardown/presentation integration, takeaway shelf refresh, WPF confirm button copy, pause-warning opt-out checkbox and deadline-based summary suppression. |

`CmbPresets` stays hidden on both heads; live preset CRUD belongs to PresetsTabView and remains intact. `ShowFeaturePopup` and `SetTierBadge` have definitions but no call sites in WPF Presets.cs at the pinned base (grep `ShowFeaturePopup\(` / `SetTierBadge\(`). This is not a P122 promotion: the live gaps above remain, and render-all is not claimed.

The user-visible session effects took priority within the 1000-added-line cap. Remaining dashboard and unrelated session integrations are explicitly retained as gaps, not silently declared wired.

## Assumptions

- Keep session GIF opt-in across a language-only detail refresh; selecting a different session resets it. The option never clears a template's prescribed GIF, matching WPF StartSession.
- Reuse the standalone overlay's shared decoder/queue for the session slot, keeping ownership and restore-sentinel state distinct.
- Preserve WPF wall-time bubble burst duration, rather than trunk's elapsed-time variant.

## Build matrix

| Project | Exit | Errors |
| --- | --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | 0 |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 | 0 |

## Changed files

- `CCP.Avalonia/Views/Features/SpiralFeatureControl.axaml.cs`
- `CCP.Avalonia/Views/Overlays/CornerGifOverlay.cs`
- `CCP.Avalonia/Views/Overlays/SpiralOverlay.cs`
- `CCP.Avalonia/Views/Tabs/PresetsTabView.CornerGif.cs`
- `CCP.Avalonia/Views/Tabs/PresetsTabView.axaml`
- `CCP.Avalonia/Views/Tabs/PresetsTabView.axaml.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.Presets.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.SessionRun.cs`
- `CCP.Avalonia/Views/Windows/PanicSurfaces.cs`
- `CCP.Core/CoreCornerGif.cs`
- `CCP.Core/Services/Media/CornerGifAdmission.cs`
- `CCP.Core/Services/Session/SessionRunner.Bubbles.cs`
- `CCP.Core/Services/Session/SessionRunner.CornerGif.cs`
- `CCP.Core/Services/Session/SessionRunner.cs`
- `Tests/CCP.Avalonia.Tests/SessionCornerGifWireTests.cs`
- `Tests/CCP.Core.Tests/SessionRunnerTests.cs`
- `docs/avalonia-parity.md`
- `docs/cloud/fin-shell-presets.md`
