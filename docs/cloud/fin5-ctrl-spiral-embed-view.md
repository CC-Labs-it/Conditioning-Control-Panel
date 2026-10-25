- Base verified: `a3d9e984fb2f2c002f3849e485129134e367a6aa`; clean local `work` snapshot before creating the task branch.
- Connected SpiralTabView to the existing shared DescentService: initial server block, live accepted blocks, withdrawal/logout, and visibility-scoped subscriptions. The existing bridge still queues only the latest pre-ready state and forwards raw server JSON unchanged.
- Applied the existing AmbientFxCanvas motion preference (including Windows client-area animation and Gentle Visuals caps) to embed state messages.
- Registered the newly reachable browser in PanicSurfaces; panic hides and suspends the tab, clears the browser, and unwires account/fuse/settings callbacks.
- Added focused headless regression coverage and retained the row as `stub`. No new native surfaces or libraries were introduced.
- Source: WPF reference at the pinned base above. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## Remaining WPF behavior

The base ledger's missing-list audit found these gaps:

1. Actual account block delivery and live updates: implemented in this branch. The shared service was already present, despite stale comments saying it was WPF-only.
2. MotionFx Windows animation cap: implemented by reusing the existing head preference helper.
3. Live migration withholding: still missing. Persisted migration settings are observed, but Core SyncPush explicitly handles only acknowledgments and does not advertise `descent_auto`. WPF's DescentMigrationService is a 448-line runtime coupled to ledger rewrite, watermark clear, immediate sync, hold/release and stage-drip delivery. Its full host migration is not implemented here; adding a fake live-offer flag would not provide its actual behavior. WPF now returns false for IsCeremonyOpen, so there is no ceremony window to port for this gate.
4. Normal profile/menu entry after the fuse retires: still missing (`shell-profile-spiral`); that host remains a stub. Existing fuse navigation can reach this tab.
5. WebKit carrier before page boot: still missing. WebHost installs CarrierShim after NavigationCompleted. Avalonia.Controls.WebView 12.1.0 exposes no public document-created script registration; a load-time-only probe can miss the handshake.
6. Native process death fallback: still missing; the public adapter does not publish ProcessFailed. Failed navigation still disposes and latches the existing waiting-room fallback.
7. WPF native context menu/status/accelerator/zoom/error-page switches: still missing; these are not settings exposed by the current public adapter. Devtools, profile, anti-occlusion arguments and dark background were already configured on the base.

No P122 claim is made: the WPF embed has live callers. Complete parity therefore remains blocked on the native adapter seams and the migration/profile hosts above; this branch is a partial implementation, not a wired promotion.

## Verification limits

- SDK: .NET 10.0.100. Channel discovery failed with HTTP 403; the pinned SDK download succeeded.
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia and all 12 Linux test projects referenced by build.yml passed (14 project builds, exit 0, zero errors). Existing warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build` with `DiffEngine_Disabled=true`: exit 0; 4475 passed, 5 existing Vosk skips, 4480 total. Vosk model-backed cases were not exercised.
- Before implementation, the two new regression tests both failed: missing initial embed (empty host), and reduced_motion false with OS animations disabled. After implementation the focused embed/migration/panic run passed 24/24.
- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter` selecting SpiralEmbedViewTests, SpiralMigrationGateTests, SpiralRoomShellTests, SpiralMotionPreferenceTests and PanicSurfacesTests: exit 0; 33 passed, 0 failed/skipped. The final build includes the panic no-resurrection assertions.
- `timeout 180 dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 124 (three-minute timeout during WPF compilation, after successful restore). WPF test assembly compilation was not completed; WPF tests were not run.
- `.github/scripts/test-plan-coverage.sh` is absent on this pinned base; numbered WPF/Rows tester steps were added manually. `git diff --check` passed.
- Native WebView2/WebKit engines, Windows system preference APIs and WPF UI behavior cannot be run on this Linux headless worker. Headless tests use the existing isolated CCP_USERDATA_DIR harness, with no real desktop or account requests.

## Assumptions

- Reuse the shared head motion preference, including its Gentle Visuals cap, rather than duplicate the OS probe.
- Preserve current WPF block-event ordering: post a non-null block to an existing embed before re-evaluating the room; withdrawal tears down rather than sending fabricated zero data.
- Panic parks this tab until explicit navigation returns to it; late account callbacks must not restore a browser.
