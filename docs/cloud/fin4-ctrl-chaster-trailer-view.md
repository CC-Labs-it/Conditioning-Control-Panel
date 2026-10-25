# fin4-ctrl-chaster-trailer-view

- Completed the trailer-only native policy through Avalonia's public platform handles. Windows borrows the WebView2 Core wrapper and disables the same seven settings as WPF: devtools, default context menus, status bar, browser accelerators, zoom controls, built-in error pages and web messages. Only the SDK Core assembly is referenced; no WPF/WinForms controls enter the Avalonia head.
- Windows `ProcessFailed` and WebKitGTK/WPE `web-process-terminated` now immediately publish the existing fallback signal, including while the first mount awaits. Late navigation/mount completion cannot publish Ready after failure. WebKit's native context menu and default load-error document are suppressed. GObject signal delegates stay rooted until adapter destruction; the seam holds a native reference until disconnection.
- Kept the existing menu path, shared browser, hidden warm host, reparenting scopes, edited scene figures, hover timing and `chaster-trailer` panic registration. Promoted the single ledger row to wired, not verified, and added numbered native tester steps.
- Added focused headless crash tests: during mount, after Ready, duplicate signals and after shutdown. Disconnecting the production failure subscription makes both immediate-fallback cases fail; restoring it passes all three new cases.
- No new trunk code imported. Read-only `parity/trunk` was fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Existing adapted trailer work credits `d5052645908a78cf990a3b80f8c6d1bcb5c1cc4f` and `2997ed7bcecc1f412714d51477aa2d52794e92b0`.

## WPF gap audit

Reference pinned to base `7182dfba9c6dc95974aa4d333a1e9ce3f8c81d6d`:
`Controls/ChasterTrailerView.cs`, `Views/Tabs/ChasterTabView.xaml(.cs)` and `Views/Tabs/ChasterTabView.Fx.cs` in the frozen WPF head.

1. Immediate native process-crash fallback was absent: the old head noticed only a later failed script. Implemented native event routing to the existing Failed state.
2. Native context-menu, status-bar, accelerator, zoom-control and built-in-error-page settings were absent. WPF also disables web messages, beyond the old row's list. Implemented the Windows settings and corresponding WebKit native menu/error suppression; WebKit has no surrounding browser chrome or built-in app accelerator actions.
3. Hidden-host prewarm and native reparenting were already implemented, but lacked live proof. Preserved that implementation and made the remaining desktop validation explicit in the tester plan. This is a verification limit, not a missing caller or host.
4. The remaining WPF row behavior already has a real caller: menu hover, grace timers, retarget, live dressing/edit/reset, pending latest scene, navigation fencing, missing-engine/page/script fallback, popup suppression, dedicated Windows profile/arguments, fallback drift/figures and hide/detach/unlink/panic cleanup. No other service port or P122 dead-code exception is needed.

## Verification limits

- SDK 10.x; Release builds of Core, Avalonia and all twelve Linux-buildable test projects from the build workflow succeeded with zero errors. The WPF test project also built successfully with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 554 warnings, 0 errors. WPF was compiled, not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. The base has no `test-plan-coverage.sh`; numbered WPF/Rows steps were added directly.
- Full `ChasterTrailerTests`: exit 1, 9 passed / 2 failed / 0 skipped. Both failures are existing render tests that cannot create the Fredoka glyph typeface in this environment. All new crash tests pass.
- Regression proof without failure subscription: exit 1, 2 failed / 0 passed (the expected `HasFailed` assertion). Restored subscription: exit 0, 3 passed / 0 failed / 0 skipped.
- Core suite: initial relocated build outputs broke source-fixture root discovery; outputs were restored beneath the repository and the full suite rerun. That run: 3539 passed / 2 failed / 5 existing skips. Failures were the untouched remote-media Loops/Clips expectation and board frame budget (23.35 ms). With concurrent compilers finished, the full Core suite rerun passed: exit 0, 3541 passed / 0 failed / 5 existing Vosk skips. No Core source or tests changed.
- The supplied checkout was read-only and on the wrong commit. Used the requested independent fallback clone, verified the exact expected SHA, and created only the requested unit branch. Generated outputs were moved to the larger workspace volume after the temporary filesystem filled; all required builds were rerun.
- Native Windows settings/process subscription compile but cannot run on Linux. This environment has no live desktop/browser engine; Windows/Linux native settings, process termination, hidden-host prewarm and popup reparenting require the numbered desktop checks. No render-all/native-engine proof is claimed.

## Assumptions

- Keep existing shared-clock drift and lifecycle behavior; native completion does not need a new surface or panic entry.
- Treat prewarm/reparenting live proof as a validation requirement, since their existing production path is implemented. The row is wired, never verified.
- Preserve the shared WebHost default behavior; only its existing trailer constructor opts into the new native policy.
