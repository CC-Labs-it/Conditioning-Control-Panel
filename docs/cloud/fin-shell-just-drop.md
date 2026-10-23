# fin-shell-just-drop

- Audited `shell-just-drop` against the WPF reference at base `a954762e8d88eabe87270b5c5a758c23c858514b`.
- Kept the row `stub`, with one row for the id and an explicit Missing list. No runtime code, tests, surfaces, or panic registrations changed.
- Dependency-only result: the remaining implementation belongs to the unported native browser integration in `shell-webview2-hosts`. Existing shop/replay paths, receipts, settlement, host fullscreen strip, and panic registration remain as on the base.
- Fetched and inspected read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; its `WebHost` still documents the missing document-created seam. No code imported, so there are no imported commits to credit. Relevant inspected host commit: `4f7d56e39` (premium web doors).

## WPF behavior audit

Both ledger reference files were read in full: `MainWindow/MainWindow.JustDrop.cs` and `Services/JustDrop/JustDropHostService.cs`, plus the latter's `Chaos/ChaosWebViewHost.cs` implementation and live Takeaway/navigation callers. All paths below are repository-relative.

| Behavior | Base implementation or remaining gap |
| --- | --- |
| Server-only door, initially withheld; paint before subscription; unsubscribe on close; delayed availability check at 9 seconds; repaint Takeaway on change | Present in Core `JustDropService` and `MainShellWindow.JustDrop.cs`. |
| Unavailable shop is a no-op; shop is windowed without minimizing the panel; one live window; EMI learns each shop open | Present in `GameWindow.Launch`, `JustDropDoorOpen`, and `LoadJustDrop`. |
| Reopen focuses the native page as well as activating its window | Missing native-page focus routing: WPF `ChaosWebViewHost.FocusWeb` calls both `Activate` and `_web.Focus`; both Avalonia live-window paths only call `Activate`. This is shared WebHost integration, not another shop window. |
| Kept receipts work with the shop withheld; blank order is refused; replay opens host fullscreen, minimizes only a visible non-minimized owner, restores Normal/Maximized only when it still owes that minimize | Present in `LaunchJustDropReplay`, including its single-window early return. |
| Native ownership, always-visible host strip, translated fullscreen/exit label, host-focus Esc, Topmost rented only while fullscreen/active | Present in `InitializeJustDropChrome` / `SetJustDropFullscreen`. Native OS behavior has not been exercised here. |
| Focused page reports Esc before page code can register an earlier capture listener; report-only handling leaves the page's own Esc behavior intact | Missing document-start registration. WPF registers `EscapeBridgeScript` with `AddScriptToExecuteOnDocumentCreatedAsync` before navigating; this head injects carrier and bridge only after navigation completes. A later listener cannot beat an earlier `stopImmediatePropagation`. |
| Page element-fullscreen fills only the browser client area; the native strip remains available and only the host owns window fullscreen | Missing WebKitGTK containment integration. WPF's `HostOwnedFullscreen` branch deliberately ignores page fullscreen as a window-mode request; WebHost exposes no element-fullscreen handling. WebView2's native client-area behavior is not proof for GTK. |
| Sign-in handoff URL carries the account and destination, never the token; signed-out opens destination directly; request-header stamping precedes the handoff | Core `JustDropHandoff` and the one-accepted-request rule are wired and tested at the managed seam. Native GTK proof remains missing; see below. |
| Autoplay/no-background-throttling arguments and a dedicated browser profile are set before browser-environment creation | Present for WebView2. Compiled here, not executed on Windows; Chromium flags do not establish WebKit behavior. |
| Page frames reach the shared settlement rules; the host consumes Esc; closing invalidates the drawer and refreshes Takeaway; panic closes the host | Present. Panic uses existing `games`; no new surface was introduced. |
| Browser-process failure closes the host without automatic relaunch, restoring its minimized owner and invalidating/refetching the drawer through the normal close funnel | Missing native process-failure signal in WebHost. WPF wires `OnProcessFailed` to `DisposeAll`; closing manually/panic does not prove this crash path. |
| Drawer cache/account isolation, receipt order, three shortcuts then overflow then shop, link copy, hover lift, language repaint | Already implemented by the two preceding wire lanes and shared Core orders service. |

## Dependency evidence and next implementation boundary

The dependency is the existing **`shell-webview2-hosts`** row, which is still `stub` and explicitly lists incomplete host integrations, fullscreen, and document scripts. Implement its native lifecycle/focus routing once in `Views/Controls/WebHost.axaml.cs`, then connect the Just Drop caller; do not add a second native browser owner here.

The pinned package is `Avalonia.Controls.WebView` **12.1.0**, with nupkg SHA-256 `1249cd4016595e1168a7e0562be1330f117592ba0928fa9bf38d1172b8e76491`. Its public `NativeWebView` API and GTK adapter were inspected with ILSpy 11.1.0.9782 outside the repository:

- The shared API exposes `InvokeScript`, navigation/resource/message events, `AdapterCreated`/`AdapterDestroyed`, focus inherited from the control, and `TryGetPlatformHandle`. It has no public document-created script, element-fullscreen, or browser-process-failure event.
- Raw `IGtkWebViewPlatformHandle.WebKitWebView` and `IWindowsWebView2PlatformHandle.CoreWebView2` handles do exist. This is an unported integration, not a claim that the native engines cannot do it. Correct support needs platform-thread dispatch, native callback lifetimes/disconnect, and installation before navigation. The package calls `adapter.Navigate(_lastSource)` **before** raising `AdapterCreated`; merely adding a handler there while retaining the current eager `Source`/`Navigate` path would still be too late.
- GTK's internal `GtkWebViewAdapter` installs its own `invokeCSharpAction` user script at document start, but offers no public method to append CCP's carrier/Esc script there.
- GTK request headers are mutable `Soup3HttpHeaders` only when Soup 3 is available and the native request has headers. Otherwise the adapter supplies immutable headers; a managed fake accepting `TrySet` does not establish a signed-in native handoff. A live GTK/Soup 3 run against the real site is still required. No credentials were read and no live site was visited.

This is not dead WPF code, so P122 does not apply. Reproducible caller proof at the pinned base:

```sh
git grep -n -E 'LaunchShop\(|LaunchReplay\(|HostOwnedFullscreen = true|OnProcessFailed = OnProcessFailed' a954762e8d88eabe87270b5c5a758c23c858514b -- ConditioningControlPanel/MainWindow ConditioningControlPanel/Services/JustDrop/JustDropHostService.cs
```

The matches include `MainWindow.TabNavigation.cs:271` (shop), `MainWindow.Takeaway.cs:548` (replay) and `:601` (shop), and `JustDropHostService.cs:117` / `:139` (host-owned fullscreen / process-failed callback). `ChaosWebViewHost.cs:1579` registers the document-start Esc bridge and `:1612` wires browser-process failure. No `wired` or `verified` claim is made.

## Verification limits

SDK: .NET `10.0.402`. Every command below used Release configuration. There is no new behavior to test or new panic surface to register.

| Command / project | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0 | 48 warnings, 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0 | 174 warnings, 0 errors |
| `dotnet build Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj -c Release` | 0 | 0 errors |
| `dotnet build Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj -c Release` | 0 | 0 errors |
| `dotnet build Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj -c Release` | 0 | 0 errors |
| `dotnet build Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj -c Release` | 0 | 0 errors |
| `dotnet build Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj -c Release` | 0 | 0 errors |
| `dotnet build Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj -c Release` | 0 | 0 errors |
| `dotnet build Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj -c Release` | 0 | 0 errors |
| `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release` | 0 | 50 warnings, 0 errors |
| `dotnet build Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj -c Release` | 0 | 0 errors |
| `dotnet build Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj -c Release` | 0 | 0 errors |
| `dotnet build Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release` | 0 | 123 warnings, 0 errors |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 | 512 warnings, 0 errors; compiled, not run |
| `git diff --check` | 0 | No whitespace errors |
| `bash .github/scripts/core-guards.sh` | 0 | All Core guards passed; 424 XAML type references checked |
| `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build` | 0 | Failed 0, passed 2837, skipped 5, total 2842 |

- The five Core skips are the existing Vosk tests without the model, whose download is blocked in this environment. No tests were weakened or changed.
- Avalonia tests, including `JustDropWindowTests`, `WebHostSandboxTests`, `WebHostProbeTests`, and `EmiRingWiringTests`, were compiled but **not run**, as instructed for the known font-creation limitation. The cloud-test-fonts setup is absent from this base. Other isolated test assemblies were compiled, not executed.
- Windows browser flags/profile and WPF/native behavior were not run. No native GTK engine, signed-in live-site handoff, fullscreen-element, or browser-process-crash proof was attempted. No render-all run; P122 is not being used.
- Initial setup encountered the read-only checkout and used the authorized fallback clone. The first unrestricted batch of test builds exhausted disk space (exit 134); deleting generated outputs between projects allowed every Linux test project to compile successfully. The initial restricted Core-guards invocation could not write its scratch directory (exit 1); rerunning with writable scratch space passed. These were infrastructure retries, with no project-file changes.

- `git push -u origin HEAD:refs/heads/avalonia-port/cloud-fin-shell-just-drop` failed with exit 128: Git could not obtain a GitHub username/credential in the noninteractive runtime. No credential workaround or second push was attempted. The committed patch is provided in the job response.

## Assumptions

- The requested dependency-only rule applies to the shared native browser integration identified by `shell-webview2-hosts`; this unit records the boundary without inventing a second platform adapter.
- Keep the row `stub` until its native user paths and handoff are proven. After-load script execution, managed header tests, and headless fallback rendering are not equivalent evidence.
