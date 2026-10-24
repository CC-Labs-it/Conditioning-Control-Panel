# sync7-port-session-browser-teardown

- Audited WPF `18ab80220f55421cc662d180b135f2e0361057d4` against base `39260f198fad67d90bc988c63a00480830e5fefc`.
- **18ab80220: blocked-by-host.** No production behavior was changed. The Avalonia remote browser takeover host is absent; implementing it would exceed this teardown-only unit.
- Read the complete WPF diff and fetched `parity/trunk` read-only. No trunk code was imported, so there are no imported trunk commits to credit.
- Left the parity document and sync ledgers unchanged. No new surface or PanicSurfaces registration, ledger promotion, or tester step is applicable.

## Evidence and missing host

- `CCP.Avalonia/Views/Windows/MainShellWindow.Browser.cs:34` explicitly lists the missing initialization, autoplay, fullscreen/popout and remote-video lifecycle, including `EndWebVideoTakeover`, `PlayHypnotubeFromRemote`, `StopBrowserVideoFromRemote` and `_remoteBrowserVideoActive`.
- `CCP.Avalonia/Views/Windows/MainShellWindow.Browser.cs:190` accepts `autoPlayFullscreen` but logs that takeover is skipped; it only navigates the page. There is no pending autoplay continuation to invalidate.
- `CCP.Avalonia/Views/Controls/WebHost.axaml.cs:177` DOES expose `InvokeScriptAsync`. The shell's old claim of no script channel is stale, but script access alone does not supply remote ownership, media queue release, fullscreen registration, popout restoration or late-fullscreen events.
- `CCP.Avalonia/Views/Tabs/RemoteControlTabView.axaml.cs:75` connects the relay directly to `RemoteCommands.Execute`.
- `CCP.Core/Services/RemoteControl/RemoteCommands.cs:84` stops local video only; `play_hypnotube` has no case and falls through to `NotOnThisBuild` at line 105. No browser takeover can be started by this dispatcher.

The WPF change requires request-generation invalidation during initialization/navigation/autoplay, stop-script guards, remote ownership reset, fullscreen and mirror teardown, homepage restoration, remote-overlay hiding, explicit fullscreen-registration cleanup, and teardown before media/queue release. None can be honestly declared ported by adding an uncalled rule or by pausing the user's ordinary browser on every local-video stop.

## Verification limits

This is a documentation-only blocker report: no behavior was introduced, so there is no new regression test or production-line reversal proof.

- .NET SDK 10.0.402 installed. `dotnet build <project> -c Release`: exit 0, zero errors for CCP.Core, CCP.Avalonia and all 12 Linux test projects listed in `.github/workflows/build.yml` (CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings and Core.Tests).
- WPF test project build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. Windows code compiled but was not run; native browser/fullscreen/mirror behavior is unverified.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed. An initial read-only run could not create temporary files; rerunning with write access passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3,953 passed, 5 skipped, 0 failed. The existing Vosk tests skipped because the model is unavailable; no tests were changed or disabled.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~RemoteControlShellTests`: exit 1; 1 failed during shell construction with `Could not create glyphTypeface`, font family `Segoe UI Black`. This confirms the reported cloud font limitation; the remote-control assertions did not execute. The test uses the existing sandbox profile setup.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; the script is absent on this base. No ledger rows were edited.
- `git diff --check`: exit 0. Full headless suite, other test-project executions and real desktop tests were not run. GitHub verification is still needed for the focused headless test.

## Assumptions

- Follow the explicit blocked-by-host exception instead of inventing the absent remote browser playback lifecycle or re-porting sibling lanes.
- Preserve ordinary browser navigation when stopping local video; the WPF remote stop is conditional on remote browser ownership.

## Follow-up acceptance once the host exists

Test stop during initialization, readiness polling and delayed autoplay; late fullscreen events must not reopen the host. Verify media pause, fullscreen/mirror cleanup, homepage restoration, popout closure and overlay visibility before releasing the media flag and queue slot. Cover connected/disconnected overlay states and non-remote playback, with isolated profiles and no real desktop.
