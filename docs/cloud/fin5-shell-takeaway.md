- Preserve the existing authenticated receipt drawer and user paths; keep `shell-takeaway` at `stub` with explicit remaining native browser gaps.
- Port native panel ownership through a small Windows/X11 native seam, with pre-duck suspension, state/visibility relinking, Windows cascade-hide veto and deferred visibility repair. Existing games panic surface covers both shop and player; no new panic surface.
- Pass WPF autoplay/no-background-throttle switches to WebView2 before environment creation. Preserve default browser environments for other hosts.
- Match WPF pink toggle/shop labels and receipt/shop hover scale, duration, easing, center origin and Motion Off snap. Add focus strokes to keyboard-reachable chips, tray rows and copy links. Keep Fluent icons as required.
- Expand real shell tests for ownership lifecycle, playback environment options, receipt accent/focus/hover and replay survival. Add numbered manual tester steps.
- No trunk code imported. Read-only `parity/trunk` fetched at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038; existing base files take precedence.

## WPF gap analysis

Pinned reference: base 7e1cc58b27da780bcfc37daf3577e434e849cf65.

At entry the ledger named native ownership; autoplay/no-throttle switches; page fullscreen confinement; tray glyph; label accent/focus/PreparePresetCardFx polish; and native signed-in verification. The first, Windows switches, and interactive polish are implemented here. The glyph intentionally remains Fluent by the project's icon rule. `MainWindow.Takeaway.cs` calls `PreparePresetCardFx` only on pinned receipts and shop; `MainWindow.TabFxPresetsQuestsAchievements.cs:320-341` calls only `MotionFx.HoverLift`, not a sheen. No Takeaway sheen behavior is missing.

Remaining: WebKitGTK autoplay and unthrottled live-clock policy; native page-fullscreen confinement; `JustDropHostService.OnProcessFailed` closing the host; and document-start Escape bridge timing (the current port injects after load). `NativeWebView` exposes no fullscreen-element or process-failure event; its public GTK environment options expose no media autoplay or timer throttling settings. Implementing these faithfully requires a native web-engine lifecycle/signal integration (including document-start/fullscreen behavior), rather than a post-load JavaScript approximation that misses the first player request. That host work and its native tests are deferred; this unit does not claim complete parity. Signed-in native playback and taskbar/cascade behavior need Windows/X11 desktop testing.

## Assumptions

- Avalonia restricts `IWindowImpl.SetParent` to framework callers, so a small HWND/XCB seam implements WPF's raw native owner, avoiding managed modal/visibility coupling.
- Retain Fluent receipt/link/add icons and existing host title/localization behavior.
- Keyboard focus visibility is needed for the keyboard paths already present in this head.

## Verification limits

- .NET SDK 10.0.402. `dotnet build -c Release` succeeded with zero errors for Core, Avalonia and every workflow-listed test project (14 builds total), including WPF tests with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. Projects: CCP.Core; CCP.Avalonia; CCP.Avalonia.CatalogueStartup.Tests; CCP.Avalonia.Consent.Tests; CCP.Avalonia.GeneralSettings.Tests; CCP.Avalonia.Language.Tests; CCP.Avalonia.Report.Tests; CCP.Avalonia.Scheduler.Tests; CCP.Avalonia.Snapshot.Tests; CCP.Avalonia.Tests; CCP.Core.Content.Tests; CCP.Core.Settings.Tests; CCP.Core.Tests; ConditioningControlPanel.Tests. Existing warnings remain. The final UI rebuild used `-m:1 -p:UseSharedCompilation=false` after transient overlapping-build/PDB and MSBuild-node failures.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --no-restore --filter 'FullyQualifiedName~TakeawayShelfTests|FullyQualifiedName~JustDropWindowTests|FullyQualifiedName~PresetsTakeawayEmptyStateTests'`: exit 0, 9 passed / 0 failed / 0 skipped. Fonts worked in this run; tests were executed, not merely compiled.
- Fail proofs: temporarily restore the base Takeaway builder and remove the new Windows playback arguments, run the unchanged accent/playback tests: exit 1, both fail (white vs pink; null vs required arguments). Restore final code and obtain the green focused run. The ownership assertion also failed before passing the invoking panel explicitly into shop launch. No tests weakened or skipped.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (424 XAML references checked).
- Full Core tests: `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore`: exit 0, 2,815 passed / 0 failed / 5 skipped, 57 s.
- Earlier full Core runs each reported 2,814 passed / 1 failed / 5 skipped: first the board-frame timing budget (14 ms), then GoonClock dispatch timing. Both unchanged tests passed in isolation (exit 0, 2 passed); no engine/test edits. Vosk's five tests skip because the model is unavailable in this environment.
- `git diff --check`: exit 0. Test-plan coverage script does not exist on this base; numbered WPF/Rows tester steps are supplied.
- Not run: Windows/WPF test execution, native signed-in browser playback and Windows/X11 taskbar/cascade verification, the full Avalonia suite and render-all. Headless ownership tests prove the requested link lifecycle, not native window-manager behavior. No real desktop/account was used. No trunk code imported; no PR opened.
