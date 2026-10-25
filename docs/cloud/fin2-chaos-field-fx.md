# fin2-chaos-field-fx

- Recreated the previous field-FX change on compatibility base `39260f198fad67d90bc988c63a00480830e5fefc`; it was absent on that base. No other parity IDs edited; eight chaos-field-fx rows consolidated into one stub row.
- WPF spec: `ConditioningControlPanel/Chaos/ChaosFieldFxOverlay.cs`, `ChaosWindowZ.cs`, `Services/Video/GentleWindowRules.cs`, `Services/BubbleService.cs`, and `Services/Chaos/ChaosModeService.cs` at that exact base.
- Add Aftermath gentle residue: rise linearly to 0.8 over 300 ms, hold, then the configured gentle fade. Negative hold becomes zero, extending short residues like WPF RiseHoldFade.
- Pre-create hidden after the banner at run start, respect ChaosPinOnTop, reuse the surface, raise on show through the existing X11 seam, and close before results.
- Dispatch worker-thread tether removal and closure onto the UI thread; clear platform refusal at teardown. Reuse the existing chaos-field-fx panic registration, with no new surface.
- Recreate focused Core timing tests and headless lifecycle/dispatcher regression coverage. No trunk code imported; the original read-only trunk reference was `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.

## Remaining dependencies

Already ported: Size Queen ring, Ripple linear front/eased echo/eight shards, Aftermath flicker, 90 recycled pink/amber trail sparks, elastic Bound tethers, stage/pixel mapping, idle hiding and clock parking, composited X11 refusal, panic/cleanup closure. This unit adds the gaps listed above.

Still missing: the live bubble hazard, Bound-pair and rabbit/shimmer producers, plus mandatory-video start/click re-stacking of the full game layer. Windows field placement/click-through remains unsupported by the existing X11-only surface.

Pinned proof: `git grep -n 'ChaosFieldFxOverlay' 39260f198fad67d90bc988c63a00480830e5fefc -- ConditioningControlPanel/Services/BubbleService.cs ConditioningControlPanel/Services/Chaos/ChaosModeService.cs`. BubbleService lines 496, 1663, 1676, 1688 emit shockwave/cast/ring/residue; TickFieldHazards begins at 1693; lines 1808, 1817, 1855, 1863, 1889 manage Bound threads; lines 3940, 3988 emit trails/shimmer. ChaosModeService line 745 re-stacks the band, field FX first at 751. Compare `rg -n 'ChaosFieldFxOverlay' CCP.Avalonia`: lifecycle and declarations exist, gameplay producers do not. These are live WPF paths, so P122 does not apply and the row remains stub.

## Verification limits

Results below are from the recreated change, not copied from the previous checkout. SDK: 10.0.402; Release configuration; DiffEngine_Disabled=true for tests. Test build outputs are removed between projects to fit the cloud disk.

Avalonia window tests remain compile-only under the documented cloud font-creation limitation; supervisor execution of ChaosFieldFxOverlayTests is required. They use the existing CCP_USERDATA_DIR sandbox. Native X11 stacking/click-through and Windows behavior were not run. WPF test compilation is attempted with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false; WPF cannot execute on Linux.

## Assumptions

Reuse the existing X11 Raise seam on show, matching other Chaos overlays. Full video-band ordering and bubble-hazard integration remain dependencies; preserve platform refusal rather than inventing replacement engines or a Windows field overlay.

## Commands and results

- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~ChaosFieldFxModelTests`: before fix, exit 1 (2 new cases failed, 5 passed); after fix, exit 0 (7 passed, 0 skipped).
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls`: exit 0, 1 passed.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, 3955 passed, 0 failed, 5 existing Vosk skips.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release -m:1`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release -m:1`: exit 0, 192 warnings, 0 errors.
- `dotnet build <project> -c Release -m:1` for every Linux test project in build.yml: exit 0, 0 errors. Projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests. Avalonia.Tests: 74 warnings, 0 errors.
- Avalonia window tests were not run under the documented font limitation, matching the previous attempt; their lifecycle/dispatcher cases compiled. No tests were deleted, weakened or newly skipped.
- Ledger audit: exactly one chaos-field-fx row; excluding that ID, the ledger is byte-for-byte identical to the compatibility base. All five code/test file contents match the previous field-FX change; only ledger reconciliation and analysis/verification text adapt to the new base.
- Patch portability: the earlier zero-context output needed a special apply option. The replacement includes one context line and is checked with standard `git apply --cached --check` against a separate index loaded from the exact compatibility commit.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings, 0 errors; compiled, not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (3 baselines; 429 XAML references against 1611 app types / 2151 Core types).
- `git diff --check`: exit 0. The printed mail patch is also checked against a clean compatibility-base index before delivery. No push attempted, as requested.
