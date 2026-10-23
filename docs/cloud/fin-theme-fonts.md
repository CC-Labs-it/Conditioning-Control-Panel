# fin-theme-fonts

- Finish `theme-fonts` as `wired` (not `verified`): the real application startup now guards installed Windows fonts before constructing any window, including the splash.
- Adapt WPF `Services/UI/FontGuard.cs` from trunk commit `a76a4c1ea7c59f0ff0fb28aba656d21fd214214b` (inspected through read-only `parity/trunk`, pinned at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`). Preserve the base head files; no trunk Avalonia file replaced ours.
- Probe the same five risky families once. An unreadable font produces one warning, is removed case-insensitively from the mono chain, and cannot be reopened by the row's guarded consumers. Preserve fallback order; use WPF's Segoe UI floor if every mono link fails. Missing fonts that do not throw remain harmless, as in WPF. Resource-publication errors are contained and logged.
- Bind the remaining WPF guarded consumers to `Font.Mono`: both fuse readouts/tooltips, Circe mood labels, Chaster rail/badge/booked flash, enhancement mini-timeline hover label, inviter codes and redeem input. Existing shared-resource consumers and bundled mappings are reused.
- Restore the missing mini-timeline hover label that WPF binds to `Font.Mono`: pointer-time text, clamped placement and time at either edge, hidden without a usable timeline and on pointer leave. The base ledger cited the header line, but its WPF reference line 541 is the hover label; leave the unguarded header font as on the base.
- Add managed guard tests and headless consumer coverage. The source regression failed on the unchanged base because the fuse chip still named Cascadia directly; it is rerun after the fix. The hover label belongs to the existing player; no new top-level or independently running surface was introduced, so no new panic registration is required.

## WPF gap analysis

At base `629308fe8ceacca6408b9e63146152014d21fd3f`, the ledger's only unimplemented WPF behaviors were the startup corrupt-font guard and these hardcoded consumers: `DescentFuseRailChip`, `CircesMoodMeter`, `ChasterBookedFlash`, `ChasterRailChip`, shell fuse tooltip/corner digits, enhancement mini-timeline hover label, and both invite faces. Read WPF `App.xaml`, the startup call in `App.xaml.cs`, `Services/UI/FontGuard.cs`, those controls, `MainWindow.DescentFuse.cs`, `MainWindow.xaml`, `EnhancementPlayerWindow.xaml` and its `Mission3.cs` hover caller, `InvitePanel` and `InviteRedeemBox`; existing head font and emoji tests were also reviewed. The header already existed; the guarded hover label itself was absent. Every listed gap is implemented. This is not a dependency-only unit and does not invoke P122.

The existing font policy remains: Inter default, bundled Fredoka/mono, system colour emoji with bundled fallback, and regular colour emoji under bold/italic. Other fonts' Avalonia synthesis remains an upstream native-code risk already documented in `docs/avalonia-decisions.md`, rather than an additional unported WPF behavior.

## Verification limits

- .NET SDK: `10.0.402`.
- `dotnet build <project> -c Release`: exit 0 for Core, Avalonia and all 11 Linux-compatible workflow test projects. Every build completed with 0 errors (existing warnings remain).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors. The WPF head and tests were compiled only; Windows execution and actual Windows font probing were not run here.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.”
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 0; 2,811 passed, 0 failed, 5 existing Vosk-model skips (2,816 total). The unavailable model was not downloaded; the documented download endpoint is blocked in this environment.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~FontGuardTests|FullyQualifiedName~MonoConsumerSourceTests'`: exit 0; 14 passed, 0 failed. These managed-only cases never create fonts or touch a desktop. The consumer source regression was first run on the unchanged base and failed on its private Cascadia chain (exit 1).
- Compiled, not run under the supplied cloud font-creation limit: `GuardedMonoConsumerTests`, `MiniTimelineHoverTests`, new invite resource test and booked-flash assertion; existing `AppFontsTests` and `SystemEmojiFontCollectionTests`. The supervisor's GitHub rcheck must run the font-creating classes. No render-all or WPF visual comparison was run in this environment.
- Infrastructure recovery: the initial checkout was at a different commit and its fetch was read-only; used the instructed fresh clone fallback and verified the exact expected base before edits. Generated build assets filled the temporary volume; moved ignored build outputs to the larger volume and reran the entire build matrix successfully. Kept Core test output inside the checkout for its existing source-location checks; the complete rerun passed without changing those tests.

## Assumptions

- Preserve the base's accepted portable font and emoji decisions; changing those would widen this unit beyond the remaining WPF gaps.
- Probe Windows installed fonts only on Windows. Off Windows every named mono link already maps to the bundled face; probing an installed Windows family there would test a different path.
- Dynamic resource bindings in code serve the same startup guard verdict as WPF's `FontGuard.Mono` and additionally follow subsequent resource replacement.
