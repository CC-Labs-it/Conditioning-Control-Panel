# wire-views-companion-workshop-behavior-cell

- Wire the actual Behavior-cell pause switch to the shell's existing Home browser, across tabs: mute and pause top-document media; unmute and resume on release. Preserve WPF's live-state behavior without changing the saved BrowserVideoMuted preference or replaying commands on reveal.
- Share the pause/play script in CCP.Core; WPF's existing CoreWebView2 route delegates to it. Reuse WebHostMedia's existing mute seam (already adapted on BASE from parity/trunk `e3109afdf`); no trunk code imported in this slice. Read parity/trunk for reference only.
- Keep the ledger row `stub`. Follow-up **wire-views-companion-workshop-behavior-cell-b** owns camera shortcut capture/global registration and live toggle, wallet-backed midnight-glass ownership/enablement and appearance, idle-timer restart/quick-menu refresh side effects, and the Avatar Tube browser pause route. Those require additional live host/backend seams and tests beyond this coherent slice.
- No new window, timer or playback surface: the command drives the existing Home WebHost. Existing browser/Panic lifetime remains in place.

## Verification limits

- Regression before the route existed: `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~WorkshopBehaviorBrowserTests` failed 1 / passed 1; pause expected 2 scripts, got 0.
- Final focused filter `WorkshopBehaviorBrowserTests|WorkshopCellsTests|WebHostProbeTests|WebHostSandboxTests`: **35 passed, 0 failed, 0 skipped**. The first combined run failed 4 times on missing Segoe UI Black glyphs; the existing WorkshopCells fixture now uses Program.BuildAvaloniaApp's font setup. Assertions were preserved.
- `dotnet build` passed all 13 Linux projects listed in `.github/workflows/build.yml` (Core, head, and every Linux test project). Builds used `-m:1 -p:BuildInParallel=false` and all three SDK hardlink flags; auxiliary assemblies were compiled, not run. Filtered tests used `DiffEngine_Disabled=true` and the existing isolated profile harness.
- `bash .github/scripts/core-guards.sh`: passed both guards, including 424 XAML references against 1591 app / 1881 Core types.
- `dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/worker3-behavior-render` with a disposable CCP_USERDATA_DIR: **224/224 rendered**; inspected `WorkshopBehaviorCell.png`. No error/binding/exception log hits. The existing layout is unchanged; dispatch is proved by the focused tests.
- Compiled Core `BrowserMediaPlaybackScript.ForPaused` scripts were emitted by an out-of-tree console and executed with Node's VM against two media objects: pause called both pause methods, resume called both play methods, and acknowledgements matched. Passed. No Node dependency was added to the repository.
- `dotnet test Tests/CCP.Core.Tests --no-build --nologo -v:minimal`: **2810 passed, 0 failed, 6 existing skips**, 2816 total. Skips: Debug BoardTiles timing budget and five Vosk/model-dependent cases.
- `timeout 180s dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false -p:UseSharedCompilation=false` plus serial/hardlink flags: **timed out, exit 124**, with package-pruning warnings and no compile result. WPF was not compiled successfully or executed in this check; only its existing pause script was replaced by the shared helper.
- `git diff --check`: passed.

Native WebView2/WebKitGTK playback, cross-origin iframe audio, camera/global hotkeys, wallet ownership, and Windows execution are not exercised headlessly. The existing script-mute adapter cannot mute cross-origin iframe media; WPF's native engine mute remains stronger. Resume may be blocked by the browser's normal autoplay policy, as on the existing WPF route. The test uses a per-instance script boundary to prove UI-to-browser dispatch; a real browser engine was not available.
