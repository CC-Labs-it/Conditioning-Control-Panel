# fin-views-deeper-editor-window

- Restored the editor preview's visible page-zoom controls: 10% steps, 25–500% bounds, Ctrl+wheel messages and zoom reapplied after navigation. Controls disable when the host has no engine.
- Added page fullscreen notifications and WPF-style rehosting of only the preview in a borderless, topmost window on the editor's screen. Escape/F11, page double-click/click-pair, navigation, media swap, window close and panic restore the pane. `NativeWebView.BeginReparenting` retains its adapter during the move.
- Registered `deeper-editor-fullscreen` in panic handling and its safety-critical roster.
- Restored Windows native dark/accent caption, border and contrasting text through a head-only DWM seam; owner activation is deferred until application idle and skips minimized/closed owners.
- Assigned the Windows preview WPF's separate `browser_data_deeper_editor` profile before native attachment. Linux isolation remains missing.
- Added focused chrome/zoom, window-free host-event bridge and headless preview lifecycle/profile tests. Kept the ledger `stub` because the audit below found additional live WPF behavior.
- Adapted the page bridge from trunk commit `d8a24e93a9e17eae95e075bd2c8254caeb3a1a7f` and Windows environment configuration from `89258c5c4877e14415d39c9481ad8612fec2c84c`. Existing base files were merged, not replaced. Read-only trunk was fetched; no other branch was pushed.

## Verification limits

Commands use .NET SDK 10.0.402 and Release configuration. Test commands set `DiffEngine_Disabled=true`.

| Command | Exit / result tail |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 48 warnings, 0 errors. |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 0 errors. |
| `dotnet build <test.csproj> -c Release`, each Linux test project in `.github/workflows/build.yml` | 0 for all 11; 0 errors each. Projects: CCP.Avalonia.CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot and Tests; CCP.Core.Content, Settings and Tests (all under `Tests/`, with their `.Tests` project suffixes). |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 after cache repair; 511 warnings, 0 errors. |
| `bash .github/scripts/core-guards.sh` | 0; All Core guards passed; 424 XAML references checked. |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build` | 0; 2,818 passed, 5 existing Vosk tests skipped, 0 failed (49 s). Earlier concurrent-build run had one board-frame timing failure; retry after compiler-server shutdown passed. No Core source or test was changed. |
| `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~DeeperEditorChromeTests\|FullyQualifiedName~DeeperEditorBridgeTests'` | 0; 5 passed, 0 failed. No windows or layout. |
| Same filtered test command with message subscription and caption-color operation temporarily absent | 1 as expected; 4 failed, 1 passed. Restoring both operations returned all 5 to green. This is a negative control, not an execution of the full old checkout. |

Linux project names, each under `Tests/<name>/<name>.csproj`:

- `CCP.Avalonia.CatalogueStartup.Tests`
- `CCP.Avalonia.Consent.Tests`
- `CCP.Avalonia.GeneralSettings.Tests`
- `CCP.Avalonia.Language.Tests`
- `CCP.Avalonia.Report.Tests`
- `CCP.Avalonia.Scheduler.Tests`
- `CCP.Avalonia.Snapshot.Tests`
- `CCP.Avalonia.Tests`
- `CCP.Core.Content.Tests`
- `CCP.Core.Settings.Tests`
- `CCP.Core.Tests`

All 11 Linux test projects were compiled; auxiliary compiled outputs were discarded after their successful build to avoid exhausting storage. WPF cross-build initially found damaged cached package files from the earlier storage failure; only demonstrably incomplete generated package caches were removed and restored.

- Window-opening Avalonia tests and render-all are not run in this cloud runtime because the documented font-creation issue has not been repaired on this base. They are compiled for supervisor execution; no real desktop or profile was used.
- Native WebView fullscreen/reparenting, browser page rendering/zoom and Windows DWM/profile behavior need desktop verification. CSS document zoom is the available adaptation, not a native zoom-factor API; listeners install after navigation completes.
- Fail-before execution of the window regressions is blocked by the same font limitation; no tests were weakened or skipped in source.
- The first fallback checkout and validation logs were lost when temporary storage filled and the runtime restarted. Recovery uses disk-backed task storage through the fallback path and discards only generated outputs of auxiliary test builds.

## WPF audit and remaining gaps

Reference is pinned to base `3c67e2a84aecf937d9fff4f7fe6051b6660d9a73`. Compared all eight ledger-cited WPF files with the head, including method/control inventories. Lane counts, sidebar width, metadata drawer, list activation, transport, file operations and inspectors already have callers; this unit preserves them. The following gaps were present before this unit:

| WPF behavior and reference | Result / remaining work |
| --- | --- |
| Native title colors and idle owner restore (`xaml.cs:159`, `Services/UI/WindowChromeHelper.cs`) | Implemented; Windows compiled, native calls not run. |
| Preview page zoom and fullscreen, including recovery (`xaml.cs:545–1069`, `:1476`) | Implemented through a post-navigation script bridge and native reparenting guard; native zoom and document-created injection remain unavailable through the current wrapper. |
| Separate browser profile and disabled devtools/context menus/status/accelerators/error page (`xaml.cs:501–523`) | Windows profile implemented. Missing: Linux WebKit store isolation and native browser-settings hardening. |
| Interactive tutorial pending Part 2 dispatch after 800 ms, cancel on close, HT/audio/video advance (`xaml.cs:291–343`) | Missing. Depends on `win-tutorial-overlay` automatic advance/retarget/click-through and `views-deeper-new-enhancement-dialog` Part 1 handoff; the dialog currently never sets pending Part 2. Manual coachmarks remain wired. |
| Browser-focused shortcut routing; arrows nudge by 0.1 s / Shift 1 s or seek, End, brackets jump, Ctrl+D, Ctrl+0, rule Delete/Escape (`xaml.cs:5008–5109`, `MultiSelect.cs:551–803`) | Missing. Current basic shortcuts do not cover these cases. |
| Item right-click selects without moving playhead; Duplicate/Copy/Cut/Delete/Jump, rule clipboard gating (`Unified.cs:56–180`) | Missing; the head still exposes the add-item menu over items. |
| Inspector undo once per focus/edit session (`MultiSelect.cs:694–727`); rule pins rebuilt after history restore (`:847`) | Missing; head inspector edits are not grouped into history, and restored pins can retain old rule objects. |
| Taskbar/Alt+Tab dirty asterisk (`xaml.cs:4314`, `:4641`) | Missing; the inline unsaved indicator exists. |
| Hover m:ss.ff readout (`xaml.cs:1793`, `xaml:928`) | Missing. |
| Timeline-height splitter and persisted 120–400 px height (`MetadataDrawer.cs:209–229`) | Missing; head's preview/timeline split is fixed. Sidebar width persistence already works. |
| Region-end normalization on focus loss (`xaml.cs:2090`); constrained-band drag updates rule inspector (`:1776`) | Missing. |
| All rule kinds represented on timeline, parked detached/past-end markers, explanatory tooltips and bounded hit areas (`Unified.cs:1120–1265`) | Missing; head currently draws ordinary TimeReached pins only. |
| Relink probes the library and offers Yes/No/Cancel (`xaml.cs:4905–4941`) | Missing: library match and cancel-without-relink branch. Embedded/sidecar probes already work. |
| Trigger/action/region help is a click-away popup beside its button (`xaml.cs:2871`) | Missing exact interaction: head currently opens dialog reference panels. |
| Local video decoder coverage and waveform channel semantics | Head browser codecs can differ from WPF LibVLC (notably MKV/AVI); Core peaks use downmixed mono while WPF takes max over channels. Existing documented limits retained. |

These are live WPF callers; P122 does not apply. This is not a pure dependency NOOP. Remaining independent editor behaviors require additional implementation and focused tests; the ledger must stay `stub`.

## Assumptions

- Use WPF's profile folder shared by editor instances, separate from the main browser; no new per-editor GUID directories.
- Use WPF's pane-only fullscreen layout and exit gestures, with Avalonia's native reparenting guard; preserve the editor's own window state.
- Preserve the existing preview allowlist, local-file fence, transport poll and manual tutorial behavior; unrelated tutorial surfaces remain for their own units.
