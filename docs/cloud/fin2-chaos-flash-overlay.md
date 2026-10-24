# fin2-chaos-flash-overlay

- Base verified at `a65dbbe2f4935a4797b32efd87513054dd39e1d2`; used the authorized temporary clone because the snapshot's git metadata was read-only.
- Replace repeated per-screen pictures with one UniformToFill image spanning the virtual stage, including negative origins. Wash follows WPF's DualMonitorEnabled/primary rule rather than the global content-monitor override; hero always spans every screen.
- Decode local stills separately at stage width clamped to 640..2560 (without enlarging the source); retain the existing worker-thread GIF/webp decoder's 1280 px / 40-frame budget. Worker-owned mutable Skia bitmaps are disposed before publishing the copied Avalonia Bitmap.
- Keep cleared wash and hero windows alive between pictures; hide after eight quiet seconds. Fade/stop/teardown cancel old callbacks and animation clocks. Stage geometry changes replace the old window. No ambient composition loop was added.
- Add independent local-only ShowHero/StopHero, first-frame-only playback, successful-show callback and re-stacking. The existing `chaos-flash` panic registration closes both surfaces; run end, teardown and engine stop retain that cleanup path.
- Add focused tests for spanning geometry, still resolution, window reuse, grace hide/stale grace, hero isolation/still/local-only/refusal, panic and in-flight decode cancellation. Retain fade/frame-loop/run-end coverage.
- Collapse the duplicated historical ledger prose into one honest `stub` row. This is partial completion: the port still has no live wash/hero caller.
- No trunk code imported. Read-only `parity/trunk` inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; it has no matching Avalonia ChaosFlashOverlay file. Behaviour adapted from WPF at the pinned base above.

## WPF gap inventory and remaining dependencies

At the base, the wash already picked enabled local/pack media off-thread, animated capped GIF/webp frames, applied the Core WPF fade curve and closed on panic/run end. Missing behaviours found in `ConditioningControlPanel/Chaos/ChaosFlashOverlay.cs`:

1. Real Show callers: `Services/Chaos/EffectPayloadFiring.cs:58-59` dispatches braindrain/glitch payloads; `Services/BackRoom/BackRoomFxServices.cs:154` dispatches Back Room washes. Still missing here: App seeds EffectPayload.Firer as a logging no-op; chaos/trigger-bubble effect dispatch and Back Room FX services must be ported. No substitute test button was added.
2. Independent ShowHero/StopHero, local-only validation, motion-off first frame, shown callback and all-screen placement (:91-142): implemented; the actual Back Room `gif-full` caller (:330, :339 in BackRoomFxServices) remains unported.
3. One image over the virtual stage instead of repeating it per monitor (:170-175): implemented. Real mixed-DPI physical placement/restamp convergence (:362-410) remains unverified on desktop; the existing platform shim owns native placement.
4. Higher-resolution stills (:254): implemented. WPF's warm remote still cache and Scrolller credit (:225-226, :267-273) remain dependent on the unported remote flash-media pool/cache and attribution surface. No synchronous URL/file probes or network fetching were added.
5. Keep-alive, clearing after the fade and eight-second hide grace (:185-201): implemented for both surfaces.
6. Re-stack above mandatory video and chaos free-desktop/topmost policy (:72-75, :82): re-show raises the surface and RaiseActive is available, but the live ChaosModeService video integration/free-desktop stacking policy is unported. These are live WPF callers (:774, :804), so P122 does not apply.
7. WPF fallback pool policy (:326-349) distinguishes no flash service from an explicitly empty enabled pool. The existing head path uses ChaosImagePool + AssetFolderExclusion + FlashOverlay.NextFlashPath; its public Show pool/screen enumeration is not exercised by the focused tests (which use ShowPath).

Pinned caller proof: `git grep -n -E 'ChaosFlashOverlay\.(Show|ShowHero|StopHero|RaiseActive)' a65dbbe2f4935a4797b32efd87513054dd39e1d2 -- ConditioningControlPanel` identifies the live reference callers above. The ledger cannot be `wired` until those user paths and remote media are ported.

## Verification limits

Windows-native placement, desktop click-through/topmost, mixed-DPI monitors and real GIF presentation cannot be run in this headless Linux runtime. WPF tests can be compiled with Windows targeting but cannot execute here. No desktop notification or real profile was used by tests.


- SDK: .NET 10.0.402, installed from the official build feed. Initial overlapping SDK installs left incomplete files; a clean isolated installation recovered this. Initial parallel build-output collision and disk-full attempts were recovered with serial builds and cleanup of generated outputs.
- `dotnet build <project> -c Release --disable-build-servers`: exit 0 for CCP.Core, CCP.Avalonia and every Linux-capable test project named in build.yml: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests. Final focused invocation rebuilt the final Avalonia source and test assembly with 0 errors.
- WPF test cross-build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: final retry exit 0, 2 warnings, 0 errors. Compiled, not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed (429 XAML type references checked). `git diff --check`: exit 0.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3953 passed, 0 failed, 5 existing Vosk model-dependent skips. No tests were weakened or disabled; the blocked model download was not attempted as an access workaround.
- Before the fix, the regression invocation of ChaosFlashOverlayTests exited 1: 2 passed, 2 failed because the stage was repeated in two windows.
- Final `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --disable-build-servers --filter FullyQualifiedName~ChaosFlashOverlayTests`: exit 0; 7 passed, 0 failed, 0 skipped. Covers callback-before-decode ordering, hero motion-off clock, normal animation clock, queued hero panic and pending wash pick/decode cancellation as well as the rendering/lifecycle cases above.
- Broader filter `FullyQualifiedName~ChaosFlashOverlayTests|FullyQualifiedName~ChaosRunHostTests`: exit 1; 8 passed, 13 failed. Initial failure is the documented environment font error (`Could not create glyphTypeface`, Segoe UI Black), followed by initialization failures in ChaosRunHostTests; no overlay tests failed. Run-end integration therefore compiled but was not verified successfully here. The supervisor must rerun that class with the test-font lane available.
- No render-all, native Windows execution, desktop click-through/compositing, real multi-monitor/DPI checks, remote-media playback, or live Back Room/trigger-bubble acceptance was run. This row remains stub, so no P122 dead-caller exemption is claimed.

## Assumptions

- Preserve the stronger existing head teardown policy: panic, engine stop and run teardown close both wash and hero, preventing an orphan hero surface.
- Use the existing platform placement seam for one virtual-stage window; native mixed-DPI validation belongs on the Windows/X11 desktop lanes.
- Keep `stub` for live dependency gaps rather than claiming an API alone is a user path. This is not a pure dependency NOOP because independent rendering and lifecycle gaps were implemented.

- Small still sources retain their native pixel size and are stretched by the view; the larger decode budget avoids downscaling large sources.
