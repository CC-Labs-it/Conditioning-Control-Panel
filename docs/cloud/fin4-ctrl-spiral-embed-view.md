# fin4-ctrl-spiral-embed-view

- Match WPF MotionFx's Windows animation-effects cap in the shared Avalonia motion gate and Spiral state payload. Read the preference on demand; preserve user Off and Reduced. Keep native access behind a Windows-only seam and the decision in Core.
- Add six Core motion-policy cases and a headless state-message regression covering preference changes, ambient-loop suppression and transition gates. No new surface or panic registration is needed.
- Keep `ctrl-spiral-embed-view` **stub**: the real room still cannot enter Spiral. No invented block, migration outcome, or test-only path is presented as live parity.
- Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Behaviour adapted from the frozen WPF reference at base `2cab1712fd0fa2c094507e494f33ee207a422bb3`.

## WPF gap inventory and remaining dependencies

The base row was read before editing. WPF `Controls/SpiralEmbedView.cs` provides the origin fence, mini/map URLs, shared profile, dark sink, anti-occlusion arguments, ready/latest-state bridge, raw descent forwarding, successful-navigation splash cue, fail-soft callbacks, and idempotent teardown already present in this head. Remaining differences:

1. **Live state and reachability:** WPF `Views/Tabs/SpiralTabView.xaml.cs` wires/unwires `App.Descent.BlockChanged`, re-evaluates the room/withhold, initially posts `App.Descent.Current`, and posts each non-null updated block. Avalonia passes `hasBlock: false`, `spiralWithheld: false` and initially posts null. WPF has live callers, so P122 does not apply.
2. **Descent lifecycle:** `Services/Descent/DescentService.cs` needs authenticated profile fetches, offline gating, the shared refresh gate, sign-in poke, generation-based logout rejection, deferred retry, preserving the last block on failed reads, UI-thread events, and progression handoff. The pure reader/gate and V2 auth client exist in Core, but the runtime service is absent.
3. **Migration withhold and sync:** `Services/Descent/DescentMigrationService.cs` includes held offers, silent auto-restore, pending-choice/ack handling and surface notifications. The WPF `Services/Settings/ProfileSyncService.cs` is 5,626 lines and has no Core/Avalonia counterpart. A faithful live host also needs its offer, progression, sign-in, sync and reset lifecycle; merely adding a block getter would bypass migration/account guarantees. Porting that dependency together with focused lifecycle tests exceeds this unit's 1,000-added-line budget. These services remain deferred rather than installing a partial authenticated host.
4. **Carrier before boot:** WebHost injects the WebKit bridge only after completed navigation. A page probing `chrome.webview` during boot can miss it. The pinned Avalonia.Controls.WebView 12.1.0 public API has no document-created injection hook.
5. **Native process failure:** WPF subscribes to `CoreWebView2.ProcessFailed`, disposes, and raises Failed without recovery. The current adapter exposes no corresponding process-failure event.
6. **Native switches:** WPF disables default context menus, status bar, browser accelerator keys, zoom control and built-in error pages, and explicitly enables web messages. The current public adapter does not expose those settings. A native adapter extension/package change is required; post-load JavaScript would not reproduce these native settings or their timing.
7. **Motion cap:** closed by this unit. WPF reads `SystemParameters.ClientAreaAnimation`, defaults enabled on read failure and resolves Full to Reduced when OS animations are disabled. The state sender now uses the same effective gate as ambient FX. Non-Windows retains the app preference, matching the Windows-specific scope of this reference.

Native dark-background painting remains unproven, as recorded on the base; property forwarding is already covered. This is a verification limit, not a claim of a missing colour assignment.

## Verification limits

- SDK: .NET 10.x. The channel lookup initially returned HTTP 403 from `ci.dot.net`; a pinned 10.0.100 install succeeded. The channel installer subsequently recovered through `builds.dotnet.microsoft.com` and installed 10.0.402, which is the final selected SDK.
- Fail-before proof: the new focused state-message test failed at the OS-disabled `reduced_motion` assertion (1 failed, 0 passed; test exit 1) before the sender/shared gate fix. After the fix, `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~SpiralEmbedViewTests` passed 14/14 (exit 0).
- `dotnet test Tests/CCP.Core.Tests -c Release`: initially 2,829 passed, 1 failed, 5 existing Vosk skips (exit 1). The unrelated board-frame timing guard measured 29.76 ms during concurrent compilation; no budget or test was changed. Quiet full rerun with `--no-build` also exited 1: 2,829 passed, 1 failed, 5 skipped; the same board-frame guard measured 12.21 ms against its unchanged 12 ms limit. The full suite is not claimed green.
- Isolated Core diagnosis: `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter "FullyQualifiedName~MotionPolicyTests|FullyQualifiedName~A_frame_fits_the_budget"` passed 7/7 (exit 0), including the unchanged board-frame test and all six policy cases. This does not replace the failed full-suite result.
- Existing `SpiralRoomShellTests|AmbientBeatLoopsTests` filter: 5 passed, 5 failed on missing Inter/Segoe UI Black glyph typefaces (exit 1), the documented environment limitation. These checks are not claimed green. The focused Spiral suite did run successfully.
- `bash .github/scripts/core-guards.sh`: passed, including 424 XAML type references (exit 0). An initial sandboxed attempt could not create temporary files; rerun with writable temporary storage passed.
- `git diff --check`: passed (exit 0). `test-plan-coverage.sh` is absent on this base; a numbered tester step was added to the newly created test plan.
 Windows native preference access is compiled on Linux but cannot execute here. Native WebKit/WebView2 boot timing, process loss, dark painting and browser UI switches cannot be exercised by headless tests.


## Build commands and exit codes

Every command used `dotnet build <project> -c Release`. All completed with zero errors. The WPF test build also used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; it was compiled, not run.

| Project | Exit |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 |

## Assumptions

- Prefer a tested motion improvement within the cap over an incomplete authenticated Descent lifecycle. Leave the row stub and identify every remaining dependency.
- No cross-platform OS preference is invented: non-Windows keeps the existing configured motion level.
