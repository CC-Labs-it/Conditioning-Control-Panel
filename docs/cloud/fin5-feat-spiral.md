- Added the Spiral card's Loom launch through the existing singleton game host and its `games` panic registration. No parallel browser host or new interface.
- Adapted WPF `LoomHostService` ready/save/delete/reveal/SFX/list behavior to the existing Core store, mod audio resolver, audio seam and loopback asset server. Saved-GIF serving is token/cookie protected, slug restricted and rejects escaping symlinks. Sidecar JSON remains private.
- Subscribed the attached Spiral gallery to store changes and removed that subscription on detach. Save/delete repaint it live.
- Reused the existing shared JavaScript carrier so DtRH's bridge works on WebKit's string transport as well as native WebView2. Added a regression test that fails on the base bridge and passes with this change.
- Added a sandboxed headless user-path test for singleton launch, origin fencing, save/list/delete, gallery refresh, reveal validation, cue volume and panic. Added a numbered manual tester step. Kept `feat-spiral` **stub**.
- Source: WPF reference at base `0584d51c19463026c3ac13c25bbe6a33cf3b61e0`. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported from that branch (base files win).

## WPF gap analysis

Pinned references: `Features/SpiralFeatureControl.xaml` and `.xaml.cs` in the WPF head; supporting `Services/Chaos/LoomHostService.cs`, `Services/Notifications/OverlayService.cs`, `Services/Session/SessionEngine.cs`, `Services/Chaos/DtrhHostService.cs` and `Views/Deeper/EnhancementPlayerWindow.xaml.cs` at the base above.

The initial remaining behaviors were:

1. Loom launch/refocus, ready/list, save/overwrite/delete/result, live library changes, reveal and SFX. Implemented using the existing host and store. Browser storage is shared by the Avalonia web adapter; WPF uses a separate Loom profile. Reveal opens the GIF with the platform launcher; WPF selects it in Explorer.
2. Video spiral playback: WPF CreateSpiralVideoWindow uses muted, looping media; Avalonia SpiralOverlay only decodes images with SKCodec. Still missing.
3. Deeper opacity ownership: WPF OverlayService has timed/sustained holds, opacity ramps, pulse/bump arbitration and restoration across stop. Avalonia's overlay reads the session hold only; the Deeper action host is still absent. Still missing.
4. Spiral web-state bridge: WPF's embedded room receives live state from its host. Avalonia SpiralTabView documents unavailable Descent state providers and its embed bridge. Still missing; this is separate from the Loom bridge added here.
5. Session corner-GIF lifecycle and the card's live policy toggle: WPF SessionEngine creates a session-owned overlay with timing, pause/resume, deduplication/handback to standalone corners and live policy refresh. Core SessionRunner has no session corner-GIF host; standalone CornerGifOverlay is a different surface. Still missing.
6. WPF's ModChanged gallery/hero/side-art refresh and the post-picker Selected confirmation dialog. Avalonia drops the feature-art plates and confirms by gallery highlight. Still missing.
7. Existing documented divergences remain: absent-files session skip guard, monitor-change random repick, and continuous 500ms retry after broken pool picks. They were not changed here.

These have live WPF callers; P122 does not apply. Completing the remaining video, Deeper/Descent and session corner hosts together requires more than a small host adapter and their lifecycle tests under this unit's 1000-added-line cap. This branch prioritizes the previously inert, user-visible Loom path and reports the rest rather than promoting parity.

## Verification limits

Validation results are listed below. Windows-targeted code cannot run on Linux. No real desktop, notification or user profile is used by the added test. Native WebKit/WebView2 interaction, video playback and audible output need the numbered manual step. No PR was opened.

## Assumptions

- Reuse the existing GameWindow singleton and panic surface for Loom; preserve WPF's free access and title.
- Use the existing authenticated loopback server in place of WebView2 virtual hosts.
- Use the existing platform launcher for reveal; separate browser profiles and Explorer selection stay explicit gaps.

Validation results:

- .NET SDK 10.0.100 installed. Channel discovery returned HTTP 403; the explicit release download succeeded.
- `dotnet build CCP.Core -c Release`: exit 0, 0 errors.
- Release builds of CCP.Avalonia and all Linux workflow test projects: exit 0, 0 errors. Projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests. Existing compiler warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. The first sandboxed invocation could not create temporary files; rerun with workspace execution permissions passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 2815 passed, 5 existing Vosk model tests skipped, 0 failed. No tests changed or disabled for this result.
- `node --test Assets/web/shared/tests/dtrh-carrier.test.js Assets/web/shared/tests/carrier.test.js`: exit 0, 4 passed. The new test against the pinned base bridge: exit 1 (`__ccpRnPush` absent); restored implementation passes. An initial invocation used a removed Node option and was corrected.
- Focused Avalonia run (`LoomFeatureTests`, `SpiralRandomizeTests`, `SpiralOverlayTests`): exit 1, 14 passed / 5 failed, all failures at missing Inter/Segoe glyph typeface creation. Final Loom-only run after adding the server test: exit 1, 1 passed / 1 failed. The authenticated GIF route test passed; the UI test stops at initial window layout due to the known missing fonts, before reaching the new user path. It needs the supervisor's font-equipped runner. The new UI behavior is compiled but remains runtime-unverified here.
- `git diff --check`: exit 0. The base has no `.github/scripts/test-plan-coverage.sh`; numbered coverage was added explicitly.
- Real desktop, native browser, audible playback and Windows behavior were not run.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 existing warnings, 0 errors. WPF and its tests compiled; not run on Linux.
