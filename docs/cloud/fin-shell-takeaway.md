# fin-shell-takeaway

- Ported the remaining Takeaway label accents, the tray's slider glyph (Fluent `Options`, replacing WPF's emoji), and receipt/shop hover lift: scale 1.02, 150 ms quadratic out, snap at Motion Off. Copy, receipt, row, toggle and shop now show keyboard focus.
- Added native owner links for the Just Drop shop/player through small Win32/X11 seams. Links release while the panel is hidden/minimized and return when it comes back; replay attaches after its deliberate duck. Deferred native visibility repair handles an owner minimize; owner subscriptions release on close. The existing `games` panic registration covers these windows; no new surface was introduced.
- Applied WPF's exact autoplay/background-timer/occlusion arguments to Just Drop's WebView2 environment. Other games keep their defaults; WebKit is left on its supported configuration.
- Extended `TakeawayShelfTests` with label/glyph/focus/hover assertions, actual native-owner request recording and cleanup, replay duck ownership, and browser-environment isolation. Tests use the existing isolated `CCP_USERDATA_DIR` and a native-owner recorder, never the desktop.
- Kept the ledger `stub`, with explicit remaining gaps. No `verified` claim.
- Reference branch fetched read-only: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported. Behavior was adapted directly from the WPF reference on base `7e1cc58b27da780bcfc37daf3577e434e849cf65`; existing head files were retained.

## Gap audit and decisions

At the pinned base, `MainWindow/MainWindow.Takeaway.cs` supplied receipt/tray ordering, three pinned receipts, full tray, repaint collapse, empty/count text, permanent catalogue/export zone, public-link copy/toasts with handled input, and a gated shop door. These paths already existed in the head and were retained.

The remaining differences found in that file and its live host/FX calls were:

| WPF behavior | Result |
| --- | --- |
| Pink bold 11.5 labels on toggle and shop; pink plus | Ported. |
| Tray slider symbol rather than package | Ported with Fluent `Options`; pinned receipts keep `Box`. |
| `PreparePresetCardFx` on pinned receipts and shop | Ported; no lift on tray rows or toggle. |
| Native owner link, released while panel is down | Ported, including deferred visibility repair. Windows' pre-hide cascade veto remains missing. |
| Chromium audio autoplay and unthrottled live clock | Exact arguments wired for WebView2; WebKit adapter exposes no matching controls. |
| Page fullscreen confined to the web area, preserving host strip | Still missing a supported NativeWebView fullscreen-element seam, especially on WebKitGTK. |
| Browser-process failure closes host and restores panel, without relaunch | Still missing: NativeWebView exposes adapter destruction but no equivalent process-failure event. Treating every unload as a crash would change behavior. |
| Takeaway per-zone entrance stagger on Session tab show | Still missing in the shared Session-tab FX port (`StaggerPresetCards`); deliberately listed rather than claiming full visual parity. |
| Native authenticated shop/player exercise | Not performed; runtime verification remains outstanding. |

The old ledger's “sheen” gap was inaccurate: `PreparePresetCardFx` only hooks hover. `RefreshCardSheen` selects a preset/session card via `FindSelectedCard`; it never selects a receipt. Proof at the pinned base: `git grep -n -e PreparePresetCardFx -e FindSelectedCard -e RefreshCardSheen 7e1cc58b27da780bcfc37daf3577e434e849cf65 -- ConditioningControlPanel/MainWindow/MainWindow.TabFxPresetsQuestsAchievements.cs`. This is not a P122 dead-caller claim and does not promote the row to wired.

Assumptions: use raw native ownership, matching WPF's `GWL_HWNDPARENT`, instead of Avalonia managed ownership that also couples visibility. Reuse the existing X11 shim's locked connection and error handler. No emoji icons or ambient loop were added.

## Verification limits

- Required base verified exactly; clean checkout on local `work`, then only `avalonia-port/cloud-fin-shell-takeaway` created.
- .NET SDK 10.0.402 installed with the requested installer.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`, with `DiffEngine_Disabled=true`: exit 0; 2815 passed, 0 failed, 5 existing Vosk tests skipped (model unavailable; no tests changed or disabled).
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release --no-restore -m:1 -nr:false`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-restore -m:1 -nr:false`: exit 0, 0 errors.
- `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false`: exit 0, 0 errors; includes all new assertions. Single-worker retries resolved shared-output contention and an MSBuild worker exit.
- All other Linux test projects named in `.github/workflows/build.yml` built with `dotnet build <project> -c Release`: exit 0, 0 errors each: Avalonia CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot; Core, Core.Content and Core.Settings.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors (512 existing warnings). Compiled only; WPF tests cannot execute on Linux.
- `git diff --check`: exit 0.
- Avalonia UI tests and render-all were not run, as instructed for this cloud image's known font-creation failure; the cloud-test-fonts lane is absent from this base. Focused supervisor command: `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~TakeawayShelfTests|FullyQualifiedName~PresetsTakeawayEmptyStateTests'`. Consequently the new UI assertions have no local before/after runtime failure proof.
- Native Win32/X11 ownership, native browser flags, audio, page fullscreen and authenticated shop/player were compiled but not exercised. Windows cannot run here. The native owner test recorder proves lifecycle requests, not window-manager behavior. The pre-hide Windows cascade veto and a native taskbar-entry check remain outstanding.
