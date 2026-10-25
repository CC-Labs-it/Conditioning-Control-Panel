# fin4-shell-multi-monitor

- Completed the only live gap recorded by shell-multi-monitor: Deeper player clone-on-fullscreen and extend-on-exit/close. The existing player was reachable from the Deeper hub but its fullscreen callback had no caller and only logged a stub.
- Added a same-WebHost borderless fullscreen host on the player's monitor. Reused MainShellWindow.BrowserFullscreenScript, WebHost.BeginReparenting, Core DisplayMirror and the Windows-only ScreenMirrorHead seam. No second decoder, platform abstraction or new native API.
- WPF ordering: clone before entering when DualMonitorEnabled and 2+ active screens; unsupported clone does not block fullscreen. Page exit, native double-click exit, Escape/F11, outside close, navigation, audio mode switch, player close and panic return the web view and restore extend. Duplicate entry is harmless; transient fullscreen-loss signals during entry are suppressed like WPF. Registered the new surface in PanicSurfaces and updated its registry test.
- Added 12 focused cases in EnhancementScreenMirrorTests; numbered tester steps cover native/hardware paths. Promoted shell-multi-monitor to wired, never verified. Corrected the dependent player's obsolete fullscreen gap text.
- Imports: fetched read-only parity/trunk at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038; no trunk code imported. Adapted the WPF reference and reused base implementation instead.

## WPF gap audit

Reference pinned at 3423845518798bdaf6b95f17cb298602007c123e:

- ScreenMirrorService: clone/extend state, unsupported topology, single-active-display guard, idempotence and shutdown restore already existed in Core DisplayMirror / ScreenMirrorHead. Browser fullscreen already called this service.
- App.xaml.cs:3141-3142,6304-6305 constructs and disposes display services; Avalonia already restores its mirror on app exit.
- EnhancementPlayerWindow.xaml.cs:2001-2046,2422-2553,2632-2645 supplied the remaining live path: HTML5 fullscreen signal, host, clone gate and cleanup. This unit ports that host and its signal rather than leaving an unreachable mirror call.
- DualMonitorVideoService is a single-decoder, shared-frame service, but Play/PlayFile has no shipped WPF caller (P122). The live mandatory-video path already shares one decoder across screens.

Reproducible grep proof:

```sh
git grep -n -E 'DualMonitorVideoService|DualMonitorVideo[?]?[.]' \
  3423845518798bdaf6b95f17cb298602007c123e -- 'ConditioningControlPanel/*.cs' \
  ':!ConditioningControlPanel/Services/Video/DualMonitorVideoService.cs'
```

Results: App property declaration, construction and Dispose; FlashService reads IsPlaying; documentation/comments in VideoService and InlineLoopVideo. No Play or PlayFile invocation. No invented UI trigger was added. Render-all is the companion P122 check.

## Assumptions

- Follow the WPF player's DualMonitorEnabled gate rather than the browser's global monitor-pick gate; the player always enters fullscreen even on a single display.
- Use the existing browser's post-navigation page-message bridge because NativeWebView has no document-created hook. Largest-video double-click entry follows WPF. Actual native browser fullscreen and mixed-DPI behavior require desktop testing.
- The unit concerns display behavior; audio, webcam, zoom and rule execution remain separate player gaps.

## Verification limits

- .NET 10 installed. Required snapshot fetch was read-only; used the authorized independent clone and verified the exact base commit before creating the unit branch.
- `dotnet build CCP.Core -c Release`: exit 0.
- `dotnet build CCP.Avalonia -c Release` and workflow-listed Linux test projects: exit 0, zero errors. Projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests. Existing warnings remain. Initial native-asset copies exhausted temporary storage; generated outputs were moved to a larger temporary volume and all affected builds retried successfully.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 2,840 passed, five skipped, one failure in unchanged BoardTilesTests.A_frame_fits_the_budget (16.01 ms versus a 12 ms threshold). Isolated retry also failed at 17.48 ms. No test weakened or skipped by this unit; Vosk/native integration limitations remain as on the base.
- Regression proof: temporarily restored the original player and panic registry, then ran EnhancementScreenMirrorTests: exit 1, all initial 11 cases failed at the missing fullscreen-host assertion. Restored the fix; initial combined run passed 25/25. A twelfth case now covers transient fullscreen loss during topology transition.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (424 XAML references checked). `test-plan-coverage.sh` does not exist on this base; numbered coverage was added regardless.
- Sandboxed `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all`: exit 0, 225/225 views rendered. This is render proof, not native desktop or Windows topology validation.
- Windows topology SetDisplayConfig, real browser adapter fullscreen/reparenting, X11 multi-head placement and mixed-DPI hardware were not run. Browser injection is post-navigation rather than document-created, consistent with the existing head's browser implementation.
- Final `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~EnhancementScreenMirrorTests|FullyQualifiedName~BrowserScreenMirrorTests|FullyQualifiedName~MultiMonitorResultTests|FullyQualifiedName~PanicSurfacesTests'`: exit 0, 26 passed, zero failed/skipped. Includes all 12 new cases.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors (512 existing warnings). Windows/WPF tests compiled but were not run on Linux.
- No outstanding implementation blocker. The unrelated Core frame-budget failure and real desktop/hardware checks above remain verification limits.
