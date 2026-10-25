# FIN4 Chaster tab

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; the checkout initially pointed elsewhere and was reset as requested. Only the task branch is published.
- Added WPF's collapsible daily/backlog limits editor, including pending target/landing time, immediate reductions, 24-hour delayed increases, backlog clamping, relock preference and refresh after preset selection. Reuses Core `LimitChange` and `TabLimits`; no new engine abstraction.
- Added the four footer facts in WPF order (PANIC, LIVE, floor, unlink), with Fluent icons under the head's established icon policy.
- Added `ChasterLimitsTests` for editor/relock, delayed increase/reduction/cancellation/backlog coupling, preset refresh and footer copy. The loopback Chaster fixture now reads the same effective caps as the shipped head.
- Replaced the repeated historical ledger text with one current, honest `stub` row and numbered tester steps. Removed machine-specific historical evidence paths from that row.
- Sources adapted: frozen WPF `Views/Tabs/ChasterTabView.xaml:987-1061` and `ChasterTabView.xaml.cs:770-847,1174-1179` at the base SHA above. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## WPF gap audit

At the base, the remaining behaviors were:

1. Ground spiral, glow, fog/dust ambient layers and their visibility/performance lifecycle.
2. Hero lock artwork with fade/drift; paper balance tag with landing explanation and UNPAID/CLEAR/CREDIT stamp, receipt click and swing/tug.
3. Tonight's balance tag under its calendar cell, plus calendar draw-in, flutter, bob/ring pulse and comet.
4. Switch/consent shown/accepted Fx and the common entry, hover, press, digit count-up, sheen, linking/linked, booked, preset/key/row, receipt and chevron motion vocabulary; key nudge/hint glow.
5. Limits: editor disclosure, slider ranges/steps, effective and pending values, local landing time, immediate reductions/delayed raises/cancellation, backlog follows day, relock preference, preset refresh.
6. Row hover trailer: 150 ms opening delay, 140 ms closing grace, keeping it open over the popup, artwork/flavour/why/tier, price override awareness, browser vignette, picture drift and floating booked figure; hide/park on tab exit.
7. Four footer facts.

Items 5 and 7 are implemented. Items 1-4 and 6 remain explicitly missing. The WPF Fx file alone is 1003 lines and the unported `Controls/ChasterTrailerView.cs` browser host is another 226, before trailer event wiring, artwork/tag layouts and their tests. A faithful complete motion/browser port with lifecycle and focused tests does not fit alongside the functional editor in this 1000-added-line unit. This is a partial implementation, not a dependency-only NOOP and not a claim of complete parity. No P122 dead-caller exemption applies.

## Safety and assumptions

- The new controls live inside the existing Chaster tab. They create no window, popup, notification, media host or active session, so there is no new panic surface to register. The existing Chaster service and shell safety routes remain in charge of booking/pushes.
- Match the frozen WPF editor, including showing pending targets in sliders while printing the effective cap beside them; reuse Core's existing 24-hour delay and clamps rather than introducing another rule.
- Keep the row `stub` until the remaining artwork, trailer and motion paths are implemented and exercised.

## Verification limits

- SDK: .NET 10.0.100 installed successfully using the pinned-version installer after channel discovery returned HTTP 403.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~ChasterLimitsTests|FullyQualifiedName~ChasterBillTests|FullyQualifiedName~ChasterTab2Tests'` with `DiffEngine_Disabled=true`: exit 1, 2 passed / 19 failed / 0 skipped. Every reported failure is environment glyph-typeface creation (Segoe UI Black, Fredoka or Consolas), before behavior assertions. No headless runtime or fail-before-fix proof is claimed. The initial pre-implementation attempt did not produce valid behavioral evidence either.
- The full headless suite and render-all were not run after this font failure. Run the focused suite and visual review on the font-equipped supervisor runner.
- `.github/scripts/test-plan-coverage.sh` is absent on this base; numbered WPF/Rows tester steps were added manually.
- `git diff --check`: exit 0. No real Chaster API, desktop notifications or live profile used; the tests use the repository sandbox and loopback fake.
- Windows execution cannot be performed in this Linux environment.

### Build matrix

`dotnet build <project> -c Release`: every project below exited 0 with 0 errors. WPF used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` and was compiled, not executed. Existing compiler warnings remain.

- `CCP.Core/CCP.Core.csproj`: exit 0.
- `CCP.Avalonia/CCP.Avalonia.csproj`: exit 0.
- `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj`: exit 0.
- `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`: exit 0.
- `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`: exit 0.
- `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`: exit 0.
- `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj`: exit 0.
- `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`: exit 0.
- `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`: exit 0.
- `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`: exit 0.
- `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj`: exit 0.
- `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj`: exit 0.
- `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`: exit 0.
- `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`: exit 0.
- `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj`: exit 0.

- `dotnet test Tests/CCP.Core.Tests -c Release` with `DiffEngine_Disabled=true`: exit 0; `Passed: 3953, Failed: 0, Skipped: 5, Total: 3958` (38 s). The existing five Vosk cases skipped because the speech model is unavailable; no test was changed or disabled.
