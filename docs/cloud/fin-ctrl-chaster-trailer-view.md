- Added the reachable Chaster menu-row trailer: one 320 px popup, 150 ms hover delay,
  immediate retargeting between rows, 140 ms leave grace, pointer hold over the card,
  and WPF row art, flavour, explanation, tier badge and live figures.
- Reused the shipped `Assets/web/chaster/trailers.html`, Core `TabMenuCopy` /
  `TabPriceEdit`, the head's `WebHost`, `FrameClock` and `ChasterBookedFlash`.
  Pending requests coalesce to the latest scene and figures before readiness;
  missing engine/page, failed navigation and failed scripts reveal the still picture.
- Added a loopback navigation fence, new-window suppression, devtools disabling,
  the dedicated Windows browser profile and WPF's constant anti-occlusion arguments.
  GTK uses an ephemeral browser data manager for these offline scenes.
- Added the fallback picture's 7 s scale / 9.1 s pan at 20 fps on the shared frame
  clock and the first / 1700 ms recurring booked figure. Motion gates stop drift;
  hide, detach, unlink and panic stop the card, scene and figure. Price edit/reset
  refresh the open trailer. No new Core or WPF code, package or asset was needed.
- Adapted only the new trailer partial from read-only trunk commits
  `d5052645908a78cf990a3b80f8c6d1bcb5c1cc4f` (hover card) and
  `2997ed7bcecc1f412714d51477aa2d52794e92b0` (figure loop).
  Existing base files were merged, never replaced. Trunk was inspected at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; it leaves the browser scenes unported.
- Added `ChasterTrailerTests` for loading/coalescing, same-scene edits during a
  mount, failures, shutdown races, fencing/escaping, hover cancellation/delay/
  handoff/grace, all rows' copy/tier/figures, price edit/reset, motion and panic.

## Reference audit and remaining parity

The pinned WPF reference is `f7be538c73f0408d9b654786cc0aa2f77906cfe0`:
`Controls/ChasterTrailerView.cs`, `Views/Tabs/ChasterTabView.xaml:1057-1108`,
`ChasterTabView.xaml.cs:58-126,1376-1460`, `ChasterTabView.Fx.cs:593-655`,
`ChasterTabView.Help.cs:48` and `ChasterTabView.PriceEdit.cs:158`.

Before this change Avalonia lacked the entire trailer: row hover callers and
open/close timing; reusable opaque card and fallback art; localized flavour/why,
tier and edited figures; shared browser, scene readiness and last-request queue;
missing-runtime/page/navigation/process failure fallback; script escaping;
navigation/window fencing; browser profile/options; hidden-host prewarm; art
drift and recurring figure; cleanup on hide; edit/reset refresh. The menu-row
prerequisite in the old ledger was stale: `ChasterTabView.Menu.cs` already builds
all Core prices and wires clicks, and `MenuPanel` is shown on the linked tab.
WPF has live hover callers, so P122 and the dependency-only NOOP rule do not apply.

The row stays **stub**, with one row for its id. Remaining gaps:

- Immediate native web-process failure notification: `WebHost` has no such event.
  A failed script falls back on the next mount, rather than immediately on crash.
- WPF's native accelerator, zoom, status-bar and built-in-error-page settings are
  not exposed by this browser API. Context menus are suppressed through the DOM.
- Hidden-host prewarm and moving the native browser from warm host to popup are
  wired but still need live Windows/Linux proof; headless tests use the still art.

## Verification limits

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- All 12 Linux-compatible test projects named by `.github/workflows/build.yml`
  built in Release: exit 0, 0 errors each. These are Core, Core.Settings,
  Core.Content, Avalonia, Avalonia.CatalogueStartup, Avalonia.Consent,
  Avalonia.GeneralSettings, Avalonia.Language, Avalonia.Migration,
  Avalonia.Report, Avalonia.Scheduler and Avalonia.Snapshot.
  Final head + main test rebuild after the reparenting change: exit 0,
  `241 Warning(s), 0 Error(s)`, elapsed 41.55 s. Builds used `-m:1 -nr:false
  -p:UseSharedCompilation=false` after an earlier worker failure.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0,
  `250 Warning(s), 0 Error(s)`, elapsed 28.01 s on retry. Windows code and WPF
  tests were cross-compiled only; they cannot be run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0,
  `All Core guards passed` (429 XAML references checked).
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release
  --no-build --no-restore`: exit 0, **3541 passed, 0 failed, 5 skipped**, 48 s.
  The existing Vosk tests self-skip without the model; its download is blocked
  by the environment. No tests were changed, disabled or weakened for this.
- `git diff --check`: exit 0.
- Avalonia tests were **compiled but not run**, as directed by the measured cloud
  font-creation limit. The intended supervisor filter is
  `FullyQualifiedName~ChasterTrailerTests|FullyQualifiedName~ChasterMenuTests|FullyQualifiedName~PanicSurfacesTests|FullyQualifiedName~WebHost`.
  There is no local fail-before/pass-after execution proof. No render-all or
  live desktop/browser run was attempted. Native Windows options, GTK options,
  new-window suppression and browser reparenting require live platform checking.
- The first WPF output copy exhausted the disk and interrupted a concurrent head
  rebuild. Both final retries passed after removing only generated output copies;
  no source or test settings were changed to address the resource limit.

The cloud snapshot initially differed from the required commit. Its Git directory
was read-only, so the explicitly requested independent clone fallback was used;
the expected base was verified before creating the sole authorized branch.

Assumptions: preserve WPF copy/order/timing and the existing menu/editor; use the
shared cross-platform browser/server instead of importing WebView2; use the WPF
fallback when no native engine exists; keep the row stub for the explicit gaps.
