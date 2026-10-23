# fin-ctrl-spiral-embed-view

- Base verified at `80fd32632350491e00cd0fbea3d0af5d81ac65ab`; the original snapshot differed and was read-only, so the prescribed independent clone fallback was used.
- Adapted the Spiral-only profile behavior from read-only trunk commit `89258c5c4877e14415d39c9481ad8612fec2c84c` (reviewed at trunk tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`). Existing base files were merged, not replaced; unrelated game/browser profile machinery was not imported.
- Forward the native environment request through the existing WebHost. Spiral configures one `browser_data_spiral` profile for mini/map, disables devtools, and uses WPF's three Windows anti-occlusion switches. GTK/WPE get the same profile name and a cache subdirectory. The dark sink brush is forwarded to the native child to avoid an initial white flash.
- Match WPF's exception containment for startup, state publication, ready/navigation/failure handlers and teardown. A failed send or host callback cannot take down the application; disposal clears queued state and removes the host subscriptions.
- Add four callback-failure cases, three platform environment cases and a native-background propagation case to `SpiralEmbedViewTests`. All use the existing sandbox profile; environment cases create property bags, never a native engine. No new window, overlay, keyboard hook or effect surface is introduced, so no new panic registration is needed.
- Keep the single ledger row `stub`: the application still cannot reach the Spiral state with a real account block. No claim of `wired`, `verified`, live browser parity or fail-before-fix runtime proof is made.

## WPF behavior and remaining dependencies

References below are pinned to base commit `80fd32632350491e00cd0fbea3d0af5d81ac65ab`.

`ConditioningControlPanel/Controls/SpiralEmbedView.cs` supplies normalized map/mini URLs, an opaque dark sink, shared browser profile, anti-occlusion flags, disabled browser UI/devtools/error pages, an HTTPS-origin navigation fence, queued latest state until `spiral:ready`, ready-once notification, verbatim server descent tokens, host motion/performance flags, navigation-success splash cue before ready, permanent fallback after missing runtime/navigation/process failure, and idempotent teardown. The shared bridge/base head already implement the protocol, navigation fencing, navigation cue, missing-engine/navigation failure and teardown. This unit adds the profile/flags and missing exception boundaries.

The remaining WPF gaps for this row are:

1. **Real account state, updates and withdrawal.** WPF `Views/Tabs/SpiralTabView.xaml.cs` subscribes/unsubscribes `App.Descent.BlockChanged` at lines 249/268, posts non-null updates at line 331, evaluates both the block and withhold at lines 419-420, and sends the initial current block at line 892. Avalonia `SpiralTabView.Refresh` passes `spiralWithheld: false, hasBlock: false`; `EnsureEmbed` posts null. `DescentService` and `DescentMigrationService` remain WPF-only, as do their account/profile-sync lifecycle and dispatcher publication. Their port is the prerequisite also recorded by `shell-profile-spiral`, `shell-profile-vat` and `views-tab-spiral`. Local XP or migration settings cannot safely substitute for the server's block or live withhold.
2. **Carrier available before WebKit page boot.** WPF WebView2 provides `chrome.webview` before page scripts run. WebHost injects its compatibility shim only on navigation completion. A page that probes once at boot can miss it. The public Avalonia.Controls.WebView 12.1.0 API exposes `InvokeScript`, but no document-created script registration; this needs native WebHost adapter support, not a forged ready message or a page reload.
3. **Browser process loss.** WPF subscribes `CoreWebView2.ProcessFailed` and tears down permanently. The public package API has no corresponding process-failure event. `AdapterDestroyed` describes control lifecycle and cannot establish a renderer crash; the existing 10-second splash watchdog only reveals the current slab and is not process-loss detection. This needs a native WebHost process-failure seam.
4. **Remaining native presentation restrictions.** WPF disables default context menus, status bar, accelerator keys, zoom and built-in error pages and sets the native initial background to the dark sink. The environment API supports devtools and profile/arguments (handled here), but does not expose those browser settings. The native Background is forwarded in this unit; the unsupported browser UI switches still require adapter support.
5. **Windows motion preference.** WPF `MotionFx.Level` caps motion at Reduced when Windows animations are disabled; the current head reads its own motion setting. This shared head motion-preference feature remains unported. Performance-tier forwarding is already shared and unchanged.

This is a partial implementation, not a dependency-only NOOP: profile setup and failure containment were independently implementable. FIRST LIGHT, room sound/bark behavior and profile-menu doors belong to their own ledger rows and were not added to this control unit.

Reproducible caller/dependency checks:

```sh
git grep -n 'PostState\|BlockChanged\|SpiralWithheld' 80fd32632350491e00cd0fbea3d0af5d81ac65ab -- ConditioningControlPanel/Views/Tabs/SpiralTabView.xaml.cs
git grep -n 'class DescentService\|class DescentMigrationService' 80fd32632350491e00cd0fbea3d0af5d81ac65ab -- CCP.Core CCP.Avalonia ConditioningControlPanel/Services/Descent
git grep -n 'hasBlock: false\|spiralWithheld: false\|embed.PostState(null)' 80fd32632350491e00cd0fbea3d0af5d81ac65ab -- CCP.Avalonia/Views/Tabs/SpiralTabView.axaml.cs
```

These are live WPF callers, so P122's dead-code exception does not apply. No render-all proof or status promotion is claimed.

## Verification limits

- SDK: .NET 10.0.402 installed with the requested installer. Its preliminary latest-version probe received HTTP 403; the supported SDK download succeeded.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 48 warnings, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 172 warnings, 0 errors on the initial build; final changes also compiled through all Avalonia test-project builds.
- All eleven Linux test projects in `.github/workflows/build.yml` built with `-c Release`: exit 0, 0 errors. An initial batch exhausted temporary disk while duplicating native libraries; generated test outputs were removed and builds rerun serially, removing generated outputs after each.
- `bash .github/scripts/core-guards.sh`: exit 0; 424 XAML references checked against 1591 app types / 1881 Core types; all guards passed. The first read-only-sandbox attempt could not create guard scratch files; rerun with writable execution passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 2824 passed, 0 failed, 5 skipped, total 2829. The existing five Vosk cases skip without the unavailable model; no tests were changed or disabled.
- `SpiralEmbedViewTests`, `SpiralRoomShellTests` and other Avalonia tests were compiled but **not run**, following the supplied cloud font-creation limitation. Eight new cases require supervisor/GitHub execution; fail-before-fix execution is also outstanding.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 warnings, 0 errors. WPF/Windows native setup cannot execute on Linux. Native Windows, GTK/WPE engines, real accounts, notifications, render-all and real desktop integration were not run.

## Assumptions

- Preserve the real-account gate and null initial block until the descent publisher exists; never derive server state from local XP.
- Use WPF's profile folder name on Linux too, with a cache child, rather than adding separate configurable paths.
- Restrict trunk adaptation to the profile behavior needed by this unit; preserve the existing WebHost implementation.
