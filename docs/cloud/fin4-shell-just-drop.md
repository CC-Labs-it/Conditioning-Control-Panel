# fin4-shell-just-drop

- Dependency-only unit: application code and the `shell-just-drop` ledger row remain unchanged (`stub`). The remaining work belongs to `shell-webview2-hosts`, specifically native browser lifecycle/input and document-start integration on WebKitGTK and WebView2.
- Base inspected: `6e8cd9042577afdde4f8cfc232b12584987ebb0e`. There is exactly one `shell-just-drop` row. Its earlier lanes already supply the server-gated shop, account handoff, receipts, replay ownership/duck/restore, host fullscreen strip, Esc message routing, hover timing, language repaint, and games panic registration.
- Read-only trunk inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported. Its `CCP.Avalonia/Platform/WebView2Hosts.cs` implements Windows virtual-host resource responses, not document-start scripts, GTK fullscreen containment, process-failure reporting, or native keyboard refocus. Its WebHost still documents the missing document-created seam. Importing that unrelated native-resource host would not complete this row.

## WPF comparison: every remaining gap

All references below are pinned to the base SHA above.

| WPF behavior | Current head | Required dependency |
| --- | --- | --- |
| `Chaos/ChaosWebViewHost.cs:1575` registers `EscapeBridgeScript` with `AddScriptToExecuteOnDocumentCreatedAsync` before first navigation. The capture-phase listener reports Esc without consuming the page's key. | `GameWindow.JustDrop.cs` injects after `NavigationCompleted`; `WebHost` installs the carrier after load too. An earlier capture listener can stop the host's Esc report, and a page probing the carrier during startup can miss it. | Native document-start script registration before navigation, on every document, including carrier ordering and native callback lifetime tests. |
| `ChaosWebViewHost.OnContainsFullScreenElementChanged` keeps element fullscreen inside the client area when `HostOwnedFullscreen` is true; only the host toggle changes window mode. | Windows already contains element fullscreen. WebKitGTK has no fullscreen-element seam in this head; post-load JS cannot guarantee native fullscreen containment. | GTK enter/leave-fullscreen handling with host-owned window state and persistent reachable chrome. |
| `JustDropHostService.OnProcessFailed` dispatches `DisposeAll`, without automatic relaunch. Dispose closes the host, restores only its own minimize, invalidates the order cache, then refreshes the shelf. | The head already performs restore/invalidate/refresh on `Closed`, but no browser-process-failure event reaches it. Navigation failure or normal adapter destruction is not equivalent to browser-process failure. | Native process-failure signal, UI-thread dispatch and idempotent teardown/unsubscription, routed to the existing close funnel. |
| `JustDropHostService.Launch` and `DuckMainWindow` call `ChaosWebViewHost.FocusWeb`, which activates the window **and focuses the native page**. | Both existing-window paths and the post-minimize path call only `Activate`. | Actual native keyboard-focus transfer through the adapter; activating the window or focusing an Avalonia wrapper alone is not proof. |
| `JustDropHostService.AttachAuthHeader` stamps the desktop-session document request before the first navigation. | The head has the URL rule, header callback and accepted-stamp counting, but WebKitGTK's mutable-header path requires Soup 3 and has no real-site proof here. | Native Soup 3 adapter/runtime validation of handoff/redirect behavior. A sandbox or mock cannot establish server authentication. |

## Scope decision

The remaining behaviors have live WPF callers: `JustDropHostService.Launch` installs `OnProcessFailed`, `LaunchReplay` reaches `Launch`, and the host initializes the Esc and fullscreen hooks. P122 (dead WPF behavior) does not apply.

The pinned `Avalonia.Controls.WebView` 12.1.0 public NativeWebView surface exposes navigation, messages, resources, adapter creation/destruction and platform handles; it does not expose document-created scripts, element-fullscreen or process-failure events. `WebHost.axaml.cs` is the only existing wrapper and explicitly records these missing seams. Native methods visible in the package's internal WebView2 interop do not constitute a cross-platform public contract.

Completing the dependency requires native adapter integration on both platforms (document registration before navigation, GTK signals and Soup capabilities, keyboard focus, callback ownership and teardown), plus tests for those native paths. A Windows-only COM extension or a post-load script would leave the Linux behaviors missing. Replacing/vendoring the native adapter and its lifecycle tests is beyond this unit's 1,000-added-line scope; there is no existing complete host implementation to adapt on the base or inspected trunk. Follow the task's pure-dependency rule: no speculative partial application-code change and no ledger promotion.

No new surface or behavior is added, so no new panic registration or tester-plan row is needed. The existing game panic registration remains the route to close Just Drop. The base has no `docs/avalonia-test-plan.md` or `.github/scripts/test-plan-coverage.sh`.

## Assumptions

- The unit's explicit dependency-only outcome applies to the native `shell-webview2-hosts` port; the existing ledger's honest Missing list is retained.
- Real-site authentication and native page focus require runtime evidence; mock acceptance, fallback rendering and managed window activation are insufficient.

## Verification limits

Results are recorded below after the required checks. No Windows runtime or real-site session is available in this cloud task. No render-all claim or P122 promotion is made.

- SDK: .NET `10.0.402`. Channel installation initially reported a CI-feed 403, then successfully installed from the official builds feed.
- `dotnet build <project> -c Release`: exit 0, zero errors for `CCP.Core`, `CCP.Avalonia`, and all eleven test projects named in the Linux workflow: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`. Existing compiler/analyzer warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.`
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release` with `DiffEngine_Disabled=true`: exit 0; failed 0, passed 2837, skipped 5, total 2842. The existing Vosk tests skipped without their model; no tests were changed or disabled by this unit.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~JustDropWindowTests|FullyQualifiedName~WebHostProbeTests|FullyQualifiedName~EmiRingWiringTests'` with `DiffEngine_Disabled=true`: exit 0; failed 0, passed 45, skipped 0. Font creation worked in this snapshot. These tests exercise sandbox/fallback paths, not a live native browser or the remote shop.
- Test-plan coverage script: absent on the base, not run. No ledger row was added or promoted.
- Full Avalonia suite, Windows runtime tests, live WebKitGTK/Soup 3 authentication, native process-crash/focus/fullscreen tests and render-all: not run. This documentation-only unit cannot claim those behaviors work.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 warnings, 0 errors. WPF app and test assembly compiled for Windows; neither was run on Linux.
- `git diff --check`: exit 0. Final diff: 48 added lines versus the required base, all in this draft.

NOOP: shell-webview2-hosts native document-start, fullscreen, process-failure and keyboard-focus integration, plus real-site Soup 3 handoff validation.
