- Wire the existing pack-card template's pointer enter/leave events to the shared CardSheenAdorner, following WPF MainWindow.AssetsFx.OnPackCardHover (one card at a time, radius 8, transitions gate, stale leave protection).
- Remove the sheen and stop its clock on pointer leave, effective ancestor hide and tab unload. Keep the packs strip collapsed as in WPF; this slice does not enable its placeholder catalogue.
- Add focused headless regression tests for switching/leaving cards, hiding the strip/tab and refusing motion when MotionLevel is Off. No new pure rules or Core duplication.
- Keep only the shell-assets-fx ledger row stub. Follow-up `wire-shell-assets-fx-b`: replace seeded cards with ContentPackService-backed cards/previews/actions, then execute the new tests and capture live hover evidence. That browser work is outside this FX slice.
- No imports. The read-only `git fetch origin parity/trunk` attempt failed with `could not read Username for 'https://github.com'`; the WPF source and existing head helper supplied this adaptation.

## Verification limits

- `bash .github/scripts/core-guards.sh` passed: 424 XAML references, 1591 app types, 1880 Core types; no forbidden Core API regressions.
- Initial `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release` restored Core, Avalonia and the test project, but compilation did not finish. The environment had no SDK at startup; the shared SDK installed during this session is 10.0.402.
- Focused pre-fix test attempts (`FullyQualifiedName~AssetsTabTests.PackCard`) did not reach execution. The first capped standalone Core compiler ran over 20 minutes without completing. A restart with DOTNET_PROCESSOR_COUNT=2, DOTNET_GCHighMemPercent=0x63, -m:1 and -p:UseSharedCompilation=false also stalled at startup and was stopped. Regression fail-proof has not been observed; original UI source snapshots are retained outside the branch for that check when the runtime is usable.
- Required build attempts for Core, Avalonia and every workflow test csproj are ongoing in this environment. They use the same caps and -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true to conserve output space. WPF tests use -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false. No successful compilation or test pass is claimed.
- Core suite, complete relevant AssetsTabTests, and render-all remain pending. No PNG or live visual proof is claimed. The pack strip is deliberately hidden in the standard render as in WPF.
- No Windows execution was possible on Linux. No new panic surface is introduced: this change only decorates an existing card and owns no window/session/output.
