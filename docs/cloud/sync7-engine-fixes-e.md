# sync7-engine-fixes-e

- Base: `avalonia-port/main-20261015-compat`; branch: `avalonia-port/cloud-sync7-engine-fixes-e`.
- Adapted WPF `1f3006589`: the pattern lab's All toys button uses the existing Core test-only mixer path. All three test entry points report that the master switch remains off. Mock-device headless coverage exercises their pulses and status copy.
- Adapted WPF `602509b70`: standalone corner GIFs require a running engine/session, refresh on start/resume, and close on engine stop. Admission is checked again after asynchronous decode. WPF delegates its admission rule to the same Core implementation. Existing panic registration remains in use; no new surfaces.
- Adapted WPF `1d5f754e3`: the real Core `ContentPackStore.GetPackFileTempPath` caller now streams through the already-merged encryption API. The byte-decrypt injection remains available to existing fake stores. Regression checks decrypted bytes, identity, cleanup, and allocation cost for an 8 MB video.
- Adapted WPF `b98a39cc7` and `79e765a19`: the displayed lock card clamps repeats to one; voice hints use sentence punctuation. Real keyboard tests exercise optional punctuation and dashes through the already-merged Core normalizer. `2a8a2c6b6` already reaches the shared repeat resolver; an additional test pins punctuation in its length budget.
- Test setup uses the app's registered fonts. Assertions and existing tests remain intact. No parity/trunk files imported.

## Verification limits

- SDK installed for this runtime: .NET SDK 10.0.402, runtime 10.0.13.
- Before fixes, focused headless regressions observed five failures and two passes: repeat counts displayed `0 / 0` / `0 / -10`, voice copy retained a dash, a stopped-engine corner slot appeared, and pattern lab emitted no mock pulse. Initial bare-font fixture failures were corrected before recording those behavior failures.
- Initial cold streaming regression before the caller fix: 19,928,448 managed bytes allocated for an 8 MB video. The allocation measurement warms the shared crypto buffer pool; the byte/identity/cleanup assertions remain independent of allocation accounting.
- Focused Core tests: 64 passed, zero failures/skips (`ContentPackStoreTests`, `LockCardTextTests`, `LockCardRepeatShapeTests`, `CornerGifAdmissionTests`).
- Initial full Core run: 2,855 passed, five existing missing-Vosk-model skips, one failure: `BoardTilesTests.A_frame_fits_the_budget` measured 47.04 ms under concurrent compilation (budget 12 ms). No Board code or assertions were changed.
- Final full Core retry after concurrent compilation ended: `dotnet test Tests/CCP.Core.Tests -c Release --no-build` passed: 2,856 passed, zero failed, five existing missing-model skips, total 2,861 (36 seconds).
- `bash .github/scripts/core-guards.sh`: passed. `git diff --check`: passed.
- Focused headless run: 19 passed, no failures/skips across LockCardSync7Tests, HapticsHeadTests, CornerGifOverlayTests, LockCardTypingTests, LockCardRepeatShapePanelTests. Uses the real app font builder and FONTCONFIG_FILE=/tmp/ccp-fontconfig-no-woff2.conf to avoid the runtime system fontconfig selecting unsupported OpenAI Sans WOFF2 files. Earlier font-fixture failures are not behavior results.
- `CCP_USERDATA_DIR=/tmp/ccp-root-sync7e-render-profile dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /tmp/ccp-root-sync7e-render`: exit 0, 225/225 views. Inspected HapticsTabView.png and LockCardFeatureControl.png; LockCardWindow.png and CornerGifWindow.png also rendered.
- Explicit Linux builds: all 13 succeeded (Core, Avalonia, every Linux test project named in build.yml). Each result ended with zero errors. Conditional WPF cross-build also succeeded (564 existing warnings, zero errors, 9m00s); Windows tests were compiled but cannot run on Linux.
- Native haptic hardware, real desktop click-through/z-order, OS wallpaper writes, live browser audio-sync, and microphone capture cannot be exercised by the mock/headless tests. WPF cannot run on Linux.

## Follow-up: sync7-engine-fixes-e-next

- Row 175 (`dce9652cc`): the existing Core Autonomy choice already gates Mind Wipe, but the complete head half (wallpaper write queue and companion speech/dead-link behavior) is outside this slice. Keep needs-port.
- Row 277 (`442657752`): the browser's audio-sync handlers and service are still absent; duration propagation into the already-merged Core chunk planner needs that real host. Keep needs-port.
- Row 292 (`cb8b2aacc`): dash matching is covered by the real lock-card tests; ambient comment brain routing/deduplication and the missing Remote directory status chip still need their head integration. Keep needs-port.
- Session-owned corner GIF scheduling/admission handback remains the pre-existing shell-presets lane; this branch only gates the user's standalone slots, as in the specified commit.

## Commands and observed results

Commands used `PATH=$HOME/.dotnet:$PATH`, `DOTNET_PROCESSOR_COUNT=2`, `DiffEngine_Disabled=true`; builds used `-c Release -m:1 -p:UseSharedCompilation=false` and MSBuild hardlink copy properties to fit the shared disk.

- `dotnet build CCP.Core/CCP.Core.csproj` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj`: succeeded, zero errors.
- `dotnet build Tests/<project>/<project>.csproj` for CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests: all succeeded, zero errors.
- `dotnet test Tests/CCP.Core.Tests -c Release`: `Failed: 1, Passed: 2855, Skipped: 5, Total: 2861` (limits above).
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter 'FullyQualifiedName~ContentPackStoreTests|FullyQualifiedName~LockCardTextTests|FullyQualifiedName~LockCardRepeatShapeTests|FullyQualifiedName~CornerGifAdmissionTests'`: `Failed: 0, Passed: 64, Skipped: 0`.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release -p:BuildProjectReferences=false --filter 'FullyQualifiedName~LockCardSync7Tests|FullyQualifiedName~HapticsHeadTests|FullyQualifiedName~CornerGifOverlayTests|FullyQualifiedName~LockCardTypingTests|FullyQualifiedName~LockCardRepeatShapePanelTests'` with the fontconfig override above: `Failed: 0, Passed: 19, Skipped: 0`.
- `bash .github/scripts/core-guards.sh`: `All Core guards passed.`
- `git diff --check`: passed. Added-line cap checked with `git diff -M --numstat origin/avalonia-port/main-20261015-compat`.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: `Build succeeded. 564 Warning(s), 0 Error(s)`. WPF tests compiled, not run on Linux.
