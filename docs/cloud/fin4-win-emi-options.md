# fin4-win-emi-options

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; independent cloud snapshot, initially clean. Local runtime branch name was `work`.
- Reuse the existing X11 panic listener's non-consuming XI2 connection for options dismissal. Scoped subscribers receive left/right button presses and Escape; brief presses no longer depend on sampling held state every 30 ms. Panic routes first. Fold, direct close and Kill use the existing disposal and opening-generation guards.
- Restore WPF's Windows `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE` styles, preserving them through Avalonia style rebuilds without making clicks pass through the panel.
- Add focused event filtering, brief-press, scoped disposal and Windows style tests. Retain existing headless tests for panel hit testing, stale queued events and close cleanup. No new window/surface was introduced.
- Replace the duplicated historical ledger narrative with one honest `stub` row and add a numbered manual tester step.
- `parity/trunk` was fetched read-only. No trunk code was imported; no imported commit credits apply.

## WPF comparison and remaining gaps

Reference: `ConditioningControlPanel/Windows/EmiDesk/EmiOptionsWindow.xaml` and `.xaml.cs` at the pinned base above.

WPF's live caller is the desk gear. The head already implements the panel close button, fold-before-cards request, mouse-only controls, chord/unbound display, live size/clamping, 400 ms width debounce and drag-completion save, mute resetting don't-ask, offers/glass/spice persistence, ring picker rebuild, loading guards, chrome hold, owner move/resize subscriptions, left-first physical-pixel placement with screen clamping/height cap, and Kill cleanup. Settings and control order were retained.

Gaps found before this unit:

1. X11's state poll misses presses released before the next tick. Closed when the existing XI2 host is available; the old poll remains as a degraded fallback when it is unavailable.
2. WPF prevents activation on every click, whereas ShowActivated only prevents activation when showing. Closed for Windows with native styles; still missing on Linux.
3. XWayland cannot observe clicks or Escape over Wayland-native windows. Still missing: a compositor-supported desktop-input host. The existing GlobalShortcuts panic portal does not expose arbitrary mouse presses or the Escape key in other applications. A consent-driven input-capture session would change the interaction and cannot silently reproduce WPF's non-consuming hooks.
4. Live native input pass-through/focus verification was missing and remains unavailable on this headless host.

Additional explicit limitation: XI2 raw button events carry no position. The listener queries current root coordinates immediately when dispatching the event. A pointer that moves across a hot-rectangle boundary before dispatch may therefore be classified differently from WPF's event-time coordinates. Exact coordinates require a further native input backend; do not mark the row wired on this evidence.

## Verification limits

- .NET 10.0.402 installed. The channel install encountered transient latest-version endpoint failures before falling back successfully; a pinned 10.0.100 install also succeeded.
- Build sweep: Core, Avalonia, and every Linux-compatible test project named in `.github/workflows/build.yml`, Release configuration. All 15 project builds passed with zero errors.
- Conditional WPF test build attempted with `EnableWindowsTargeting=true` and `ValidateExecutableReferencesMatchSelfContained=false`; build passed with zero errors; not run on Linux.
- Focused headless options tests: 13 passed, zero failed both before negative controls and after restoring both fixes (exit 0). Tests use the repository's isolated profile and never install native desktop listeners.
- Negative controls temporarily removed event delivery and Windows passive styles: all four new tests failed (exit 1). Both fixes were restored afterward.
- Core tests run with `DiffEngine_Disabled=true`: 3,953 passed, five Vosk tests skipped, zero failed (exit 0). Core guards passed (exit 0). This base has no `.github/scripts/test-plan-coverage.sh`; manual numbered row coverage was added.
- An initial overlapping build/test attempt encountered an Avalonia PDB file lock. The subsequent test invocation used the completed build and passed. Final build/test checks run without overlapping Avalonia builds.
- Native Windows hooks/style application compile but cannot run here. No live X11/Wayland desktop or visual render-all validation was performed; the row remains `stub`.

## Assumptions

- Preserve the existing Fluent icon decision, control text and settings behavior.
- Reuse the already-running XI2 panic host rather than introducing another native listener and thread. If that host is unavailable, preserve the existing poll fallback.
- Keep Wayland dismissal, Linux no-activation, exact event-time coordinates and live focus checks explicitly missing rather than claiming parity.

### Commands and result tails

- `dotnet build <project> -c Release --nologo`: all 15 projects, exit 0; `0 Error(s)`. Includes Core and Avalonia; Core Tests/Settings/Content; Avalonia Tests/CatalogueStartup/Consent/GeneralSettings/Language/Migration/Report/Scheduler/Snapshot; and ConditioningControlPanel.Tests with the Windows-targeting flags above.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; `Failed: 0, Passed: 3953, Skipped: 5, Total: 3958`.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~EmiOptionsX11Tests|FullyQualifiedName~EmiOptionsDismissTests|FullyQualifiedName~EmiRingWiringTests'`: final exit 0; `Failed: 0, Passed: 13, Skipped: 0, Total: 13`.
- Negative control, `--filter FullyQualifiedName~EmiOptionsX11Tests`: expected exit 1; `Failed: 4, Passed: 0`.
- `bash .github/scripts/core-guards.sh`: final exit 0; `All Core guards passed.`
- `git diff --check`: exit 0.
