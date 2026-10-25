- Complete `shell-dashboard-fx` from base `e9533d4ec2a904673eb1d718d994976555551b46` and promote its single ledger row to wired (not verified).
- WPF gaps found before implementation: mystery doorbell breath/badge/date latch; reversible hover flip, skew, face content and teardown; custom still-logo drift and intermittent sheen; browser failed/unavailable status leaving Loading pulsing indefinitely. Existing card, split-card, vault, mosaic and browser-frame motion already had live callers.
- Implement mystery reveal content using the existing Core entitlement lookup, mod art loader and tier badge. First hover persists the date; teardown never spends an unopened gift. Keep input geometry stationary while the inner plate turns, eliminating WPF's phantom-leave mechanism. All new animation uses the shared beat; glow uses a sibling Border BoxShadow.
- Implement custom still-logo drift and sheen with WPF timings, randomized sheen gaps and existing motion/focus/performance/tab/window gates. Built-in animated artwork remains exempt.
- Surface localized browser initialization exception categories and failed navigation outcomes through the existing WebHost seam and status watcher. Native fallback panel retains actionable engine-install detail.
- Add focused stepped-clock headless tests and numbered WPF tester steps. Existing shell panic registration covers these in-place decorations; no new surface/window is introduced.
- Source: WPF files at pinned base `e9533d4ec2a904673eb1d718d994976555551b46`. Fetched and inspected read-only `parity/trunk` at `a4ee52d40`; no code imported from it. Existing BeatLoop provenance (`66ca21fd3`) remains unchanged.

## Verification limits

- .NET SDK 10.0.402 installed using the requested channel installer.
- `dotnet test Tests/CCP.Core.Tests -c Release` with `DiffEngine_Disabled=true`: exit 0; 4117 passed, 0 failed, 5 existing Vosk-dependent skips (model download is blocked here).
- `bash .github/scripts/core-guards.sh`: exit 0; all Core guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on this base (exit 127); numbered WPF/Rows tester steps were added directly.
- `git diff --check`: exit 0.
- Avalonia headless tests are compiled but not executed: this base has no cloud-test-fonts lane and the task documents font creation failure here. Consequently the new regression tests are not claimed fail-proven; rcheck must run `DashboardDecorationTests`, `DashboardFxLoopsTests`, `DashboardBrowserFxTests`, and `SplitFeatureCardFxTests`.
- Native Windows browser initialization/COM/DLL behavior and desktop focus/pointer rendering cannot be exercised on this Linux worker. No Windows desktop tests, real notifications, or real user profile were used.
- Retired premium rail proof at the pinned base: `rg -n 'PrepareRailArtNudge|ApplyRailHover|ApplyRailDotPulse' ConditioningControlPanel --glob '*.cs'` returns no matches. This is not a P122 promotion: the remaining live behaviors were implemented.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release -m:1 -p:BuildProjectReferences=false` (final XAML): exit 0, 0 errors, 193 existing warnings. An earlier full-reference app build also passed.
- Every Linux workflow test project built with exit 0 and 0 errors: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests, CCP.Avalonia.Tests. Commands: `dotnet build Tests/<project>/<project>.csproj -c Release`; Consent and the main headless assembly required a serial retry with `-m:1 -p:BuildProjectReferences=false` against the successfully built app. Initial overlapping builds encountered AVLN9999 and MSB4166; serial retries passed (headless assembly: 96 warnings, 0 errors; Consent: 0 warnings, 0 errors).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors, 559 existing warnings; compiled only, not run.
- Final Core guards: exit 0, all guards passed. Final staged whitespace check: exit 0.

## Assumptions

- Reuse the head's existing `TodayFreeKey` entitlement lookup and repaint cadence, rather than adding a second daily-free service.
- An unavailable platform engine maps to WPF's localized Not installed status; its existing platform fallback panel supplies installation details. WPF-specific COM/DLL exceptions map to their existing localized keys.
- Stationary input geometry replaces WPF's geometric pointer reconciliation with a smaller equivalent solution.
- Logo sheen scheduling rides the shared clock; still-logo drift updates are quantized to WPF's 10 fps cap.
