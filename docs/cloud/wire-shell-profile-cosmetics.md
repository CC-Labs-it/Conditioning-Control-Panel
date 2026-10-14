# wire-shell-profile-cosmetics

BASE: `avalonia-port/goongame-core-c` at `0e1aa0473e7a5b6120544923e94bb04cc62a7e50`. Owned branch: `avalonia-port/cloud-wire-shell-profile-cosmetics`.

The Trainer Card now paints and clears its banner, accent border and sibling shadow, equipped achievement title, and featured pins through the existing own-card, viewed-card, and wardrobe save paths. It uses Core sanitization and the existing ModArt decoders; unknown IDs and missing art clear old paint. The original WPF avatar-slot ownership rule moved to CCP.Core, with WPF calling that same copy. Original WPF tests remain intact; four copied Core cases exercise the actual shared function.

The ledger stays `stub`. Follow-up `wire-shell-profile-cosmetics-b`: preset-avatar rendering integrated with real-photo Pending/Loaded/None paths and header bust; quick click-to-pin/unpin and its 2.5-second cap notice; viewed pin/title repaint after a mod change. No trunk files imported. Read-only parity/trunk fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; WPF on BASE remains the specification.

Changed files: MainShellWindow.ProfileCosmetics.cs, MainShellWindow.ProfileWardrobe.cs, DiscordTabView.axaml, Core Models/ProfileAvatarSlot.cs, WPF MainWindow.ProfileCosmetics.cs, ProfileAvatarSlotClaimTests.cs, ProfileCosmeticsPaintTests.cs, this hand-off, and only the shell-profile-cosmetics ledger row.

## Commands and results

SDK 10.0.402 on Linux. Builds retained nullable analysis and all analyzers, with `DOTNET_PROCESSOR_COUNT=2`, workstation GC, `-m:1 -p:UseSharedCompilation=false`, and an external compiler response file containing `/parallel-`. No tests/assertions were removed or relaxed. Missing sparse-checkout resources and fixtures were restored from exact tracked Git blobs.

- `bash .github/scripts/core-guards.sh`: passed; 415 XAML references, 1,588 WPF app types / 1,882 Core types.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release --no-restore ...`: passed, 48 warnings / 0 errors, 3m45.65s.
- Release restore of Core, Avalonia, and every one of the 11 Linux test projects named in .github/workflows/build.yml: passed.
- `dotnet build /workspace/scratch/root-validation.slnx -c Release --no-restore ... -p:CreateHardLinksForCopyLocalIfPossible=true`: passed, 351 warnings / 0 errors, 30m04.03s. That temporary solution contains Core, Avalonia, and all 11 Linux test projects. Referenced binaries were copied with hard links; mutable profile content was not.
- Checked rebuild of CCP.Avalonia.Tests after switching the new tests to production font bootstrap: completed. The initial combined ProfileWardrobe/ProfileCosmetics run had 4 failures / 1 pass from the minimal existing font setup (Fredoka/Segoe UI Black), before cosmetics assertions. The subsequent project test attempt was bounded at 180s and timed out during slow startup.
- `/workspace/dotnet/dotnet Tests/CCP.Avalonia.Tests/bin/Release/net10.0/CCP.Avalonia.Tests.dll -class CCP.Avalonia.Tests.ProfileCosmeticsPaintTests -nologo -parallel none`: passed 2 / 2, 301.611s. Real own/viewed/save/clear controls, art and bindings are asserted; the app's production font setup is used.
- `CCP_REQUIRE_VOSK=1 CCP_VOSK_MODEL=/home/agent/.cache/ccp-test/vosk/vosk-model-small-en-us-0.15 /workspace/dotnet/dotnet Tests/CCP.Core.Tests/bin/Release/net10.0/CCP.Core.Tests.dll -nologo -parallel none`: 2,819 passed / 1 failed / 0 skipped (2,820 total), 583.299s. Existing BoardTilesTests.A_frame_fits_the_budget failed at 193.72ms. An isolated rerun also failed at 67.15ms; this performance failure remains unresolved. Earlier project-suite attempt was interrupted and reported missing desk-lines.json/intake shim; exact fixtures were restored before this complete run.
- Same Core runner with `-class CCP.Core.Tests.ProfileAvatarSlotClaimTests`: passed 4 / 4, 2.468s.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false ...`: restore passed; first compiler was interrupted. A second `--no-restore` cross-build reached WPF temporary-project compilation but timed out at 900s (exit124), without a completed build result. Windows tests/UI were not run on Linux.
- `CCP_USERDATA_DIR=/workspace/scratch/root-render-profile dotnet run --project CCP.Avalonia -c Release --no-build --no-restore -- --render-all /workspace/scratch/root-render-all`: produced 78 PNGs through EnhancementsTabView.png; stopped after approximately 15 minutes without further progress. Method-only diagnostic was in native Skia SurfaceRenderTarget.Blit. No complete render sweep is claimed.
- Fresh process `--render-view DiscordTabView /workspace/scratch/root-profile-render.png`: passed exit0, 1100x780 PNG. DiscordTabView.png from the sweep was also inspected; both show the empty Profile landing state. Equipped/cleared card states have the two passing headless tests, not a screenshot claim.
- `git diff --check`: passed. Final diff adds 265 lines, including tests and this hand-off.

## Limits and publication

Before-change test builds were canceled during interrupted compiler runs; no fail-before proof is claimed. Tests/render use temporary isolated profiles; Windows UI, live user accounts, and devices were not exercised. The remaining slice above, the unresolved board performance test, the incomplete WPF cross-build and render sweep are explicit limits.

Shell pushes were denied HTTP403. Publication uses GitHub Git-data operations on the owned branch only, with one parent and `force:false` plus the expected claim SHA. No PR, merge, queue-branch write, or other branch update.
