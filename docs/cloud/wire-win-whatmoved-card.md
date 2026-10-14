# wire-win-whatmoved-card

- Reconcile only the `win-whatmoved-card` ledger row to wired: its sole missing item was stale. This base already has a real Monitors Settings section and `FocusSection` scrolls to it. The existing once-per-upgrade offer, silent fresh-install spend, Help replay, target glow, text/layout and shared Core rows remain the runtime implementation.
- Add `WhatMovedCardParityTests`: click all five real Show me buttons; assert destinations, Account/Monitors viewport positions, automation action names, card dismissal, read-mode counter preservation, Enter/Esc propagation, once-per-upgrade offering, silent fresh-install spending and the actual Help button replay. Use the production app builder with the shipped font setup inside the test sandbox.
- Add `WhatMovedPlanCoreTests`: fresh/unspent offer rules, exact row order, shared text, Settings targets and glow keys.
- Read the WPF card, MainWindow.WhatMoved and WPF tests as the spec. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported. No new popup or runtime logic was needed.

## Verification limits

- The initial 7 headless cases passed against the unchanged production implementation, including actual viewport coordinates; the final run also covers startup and Help (results below). This is evidence for an already-ported behavior, not a fix; there is no before-fix failure to claim.
- `bash .github/scripts/core-guards.sh` passed (424 XAML type references, 1591 app types, 1880 Core types).
- Existing `NavSearchTests.WhatMoved*` / `HelpReplaysWhatMovedInReadMode`: 3 failures before their tested behavior, because the bare test app builder does not resolve Segoe UI Black in the unchanged `PremiumSpark` shell constructor. Tests remain untouched; the new cases use the production app builder/font setup.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj --filter FullyQualifiedName~WhatMovedCardParityTests`: final run 9 passed, 0 failed (1.2295 minutes).
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj --no-build`: 2821 passed, 1 existing Debug-only `BoardTilesTests.A_frame_fits_the_budget` skip, total 2822 (1.8537 minutes). All 6 new Core cases passed; Vosk speech cases ran with `CCP_VOSK_MODEL=/workspace/shared/vosk/vosk-model-small-en-us-0.15`.
- Builds: `dotnet build` Core, Avalonia and all 11 Linux test csprojs from `.github/workflows/build.yml` passed. The WPF test project also compiled with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` under `flock /workspace/shared/ccp-wpf-build.lock`: 509 warnings, 0 errors, 3m32s. Windows code/tests were compiled, not run.
- All runs used `DiffEngine_Disabled=true`, .NET SDK 10.0.402 and task-local `DOTNET_CLI_HOME`; compilation used `-m:1 /p:UseSharedCompilation=false` and the four `CreateHardLinksFor*` build properties to limit disk use.
- No visual source changes; render-all was not required. No real desktop/profile access, Windows execution or WPF side-by-side comparison.
