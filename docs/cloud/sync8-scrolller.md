# sync8-scrolller — PR-body draft

- Reviewed WPF reference commits `bc63c247d32e270bd74123840137cb5b88df0587` and `251b60807577014981d79efbbae4f2e99bd2d093` against base `28ab74eed692067fed4f2051791b3a6d0976b1dc`. Fetched read-only `parity/trunk` (`a4ee52d40`); no trunk implementation imported in this lane.
- Mirrored the WPF ledger regression suite into Core and added boundary cases for Core's existing copy-expiry rule. No duplicate production clock logic or new safety surfaces.
- A separate first commit, `compat-scratch: restore merged Core helper dependencies`, restores four pure helper files already referenced by the merge and replaces three inherited head references with CoreSettings/Serilog. Integration should drop that scratch commit when applying the dedicated compat lane.

## WPF commit mapping

- `bc63c247d32e270bd74123840137cb5b88df0587` = already-covered for Core copies (`CCP.Core/Services/Fyp/Online/ScrolllerDiskCache.cs:109`, called by `CCP.Core/Services/Chaos/DtrhRemoteMedia.cs:267`; test `ScrolllerClockErrorTests.Copy_day_limit_survives_clock_changes`, `Tests/CCP.Core.Tests/ScrolllerClockErrorTests.cs:24`); n/a for WPF `RemoteMediaCache` temp-folder sweep and `ScrolllerWebGate.ClearOneAsync`: neither class nor live Chromium profile-clear registry exists in Avalonia/Core. Avalonia's DtRH copier uses the assets temp folder (`CCP.Core/Services/Chaos/DtrhRemoteMedia.cs:425`), with shared copy expiry rather than WPF's remote-media cache. No unused WebView2 timeout rule imported.
- `251b60807577014981d79efbbae4f2e99bd2d093` (Scrolller only) = already-covered (`CCP.Core/Services/Fyp/Online/ScrolllerAllowance.cs:43`, recovery at line 47, restamping at line 68); tests mirrored from this WPF commit in `Tests/CCP.Core.Tests/ScrolllerClockErrorTests.cs:32`. Escape/voice changes belong to the separate panic lane.

## Regression proof

- Focused suite: 14 passed, zero failed/skipped on the base's production clock rules with compatibility fixes.
- Temporarily removed the `HeldDayIsAClockError` recovery branch and the `IsExpiredAt` future-stamp guard: five failed, nine passed (exit 1). Failures cover next-midnight recovery, both future-first-day cases and the two far-future copy cases. Restored both files byte-for-byte; neither production clock file is changed by this lane.
- The other nine cases preserve the WPF controls and boundaries; they deliberately also pass under the old behavior. There is no newly added runtime behavior in this lane.

## Assumptions

- Keep the existing Core implementations where the merge already replayed the fixes. Do not create a speculative Avalonia Chromium cache registry to mirror a WPF-only lifecycle.
- Preserve existing game PanicSurfaces registrations; this unit adds no windows or safety surfaces.

## Verification limits

- .NET SDK 10.0.402. `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors (existing warnings remain).
- `dotnet build <project> -c Release`: all 12 Linux test csproj files listed in `.github/workflows/build.yml` passed, zero errors: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, and CCP.Avalonia.{CatalogueStartup,Consent,GeneralSettings,Language,Migration,Report,Scheduler,Snapshot}.Tests.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed after scratch compatibility fixes. `git diff --check`: exit 0.
- `dotnet test Tests/CCP.Core.Tests -c Release` with `DiffEngine_Disabled=true`: exit 0; 3,967 passed, five skipped, zero failed. Skips are the existing Vosk model-dependent tests; no skip was introduced or changed here.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~WebAssetServerTests` with `DiffEngine_Disabled=true`: exit 0; six passed, zero failed/skipped. These exercise the head's loopback asset-serving counterpart without windows. Window-rendering suites were compiled but not run because this base predates the environment font fix; no real desktop/profile used.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 1, 74 errors. The WPF app compiled, but its test project sees duplicate helper types (CS0433: TickStep, PanicAftermath, LockdownExitRule) because the scratch compatibility files also exist in the frozen WPF reference. This lane leaves WPF unchanged; integration must drop scratch and use the dedicated compat lane. WPF tests were not run. Windows behavior cannot be run on this Linux worker. No browser-profile clear exists in this head to execute or test.
