# fin-feat-feature-card

- Added the Home drawer's localized click-choice popup and persisted `DashboardInvertClicks` control. Hover opens without capture; leaving starts a 450 ms grace timer; entering the body cancels it; clicking or changing the checkbox pins it with light dismissal. Escape closes it, consumes the surface claim and restores anchor focus. Hide/unload stops the timer and closes it. The shared panic registry closes the popup too.
- Made split dashboard tiles honor the same preference as FeatureCard, keeping the popup's promise across the mosaic. Destination tiles and non-dashboard split tiles retain their existing gestures; help controls retain their input carve-out.
- Added WPF's logo gesture hint and retirement after three wall toggles, without changing feature settings for presentation.
- Connected Flashes/Bubble Pop `ShowV2Badge` to the existing `PrizeOwnership` seam for all five WPF v2 grants. Re-read on load, Home return and wall repaint. Ownership remains independent of feature settings.
- Added focused headless behavior coverage in `FeatureCardCompletionTests`; retained existing tests. Kept `feat-feature-card` at **stub**, with the remaining ownership dependency explicit; corrected cross-references in the favorites-rail and split-card rows.
- Adaptation credits from read-only `parity/trunk` (fetched tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`): `7f1861816e545d1fd5e232b0caa1b9ea7a322d46` (click-choice popup, saved gesture inversion, split routing) and `1ee7f08aeac8c0b60cf78668ca6d696f5c6eca69` (the five-grant v2 badge rules). Existing Avalonia files won; no trunk file replaced a base file.

## WPF gap audit

Reference pinned at base `2eb5a41f865f98e53b52f906bb067185bed10df2`; audited FeatureCard.xaml/.cs, DashboardCardDepth.cs and CardMute.cs, then the live callers cited by the row.

| WPF behavior | Base / result |
| --- | --- |
| Title, icon/glyph fallback, rounded content clip, tier pill, lock veil/label and lock-safe active state | Already implemented; retained. |
| Help content, unknown-id hiding and help-click carve-out | Already implemented; retained. |
| Active/tutorial preview, inactive 0.62 dim, 0.72 title, grey art twin, lock/tease priority and 200 ms fade | Already implemented; retained. |
| Dashboard socket, face travel/tilt/release spring; off dashboard tiles remain dim under pointer | Already implemented; retained. |
| Off-dashboard hover lift/art pop, rim light; active 3.5 s shared breath; motion/performance/focus/minimize/visibility gates | Already implemented; retained. |
| Reversible tease blur, veil, question mark, tier metal and shared animated rim | Already implemented; retained. WPF currently has no live TeaseTier writer for a FeatureCard; no P122 status promotion is claimed. |
| Single-card left/right routing and inverted setting read | Already implemented; retained. The base had no user-facing setting writer. |
| Click-choice popup, persistence, hover/pin/Escape/timing, logo hint/retirement | Missing on base; implemented with the same copy and timing. |
| Dashboard split tiles follow the preference | Missing on base; routing completed so the new setting works across Home. Other split-card FX are outside this unit. |
| Flashes v2 badge: drift/bounce OR pendulum OR jackpot remix; Bubble Pop: rain OR spiral-in | Pill existed with no caller. Callers added through the existing head ownership seam. |
| Grant snapshots and repaint on GrantsChanged | Still blocked by **svc-prize-ownership**: no server/account ownership or notification exists on this head. Release `PrizeOwnership.IsGranted` returns false. Debug retains the existing override. No account grant, fake ownership or unrelated service port was introduced. |

Live WPF proof: SettingsTabView.xaml.cs constructor subscribes `PrizeGrants.GrantsChanged` to `RefreshV2Badges`; that method writes `CardFlash.ShowV2Badge = V2Badges.FlashOwned()`. MainWindow/MainWindow.Presets.cs `RefreshMosaicTierBadges` writes `CardBubblePop.ShowV2Badge = V2Badges.BubbleOwned()`. These callers are live, so P122 does not apply to the remaining ownership gap. `wired` would misstate the Release user path.

## Verification limits

Avalonia tests and render-all are not executed in this cloud snapshot: the supplied environment constraint reports font creation failure and the cloud-test-fonts lane is absent. The supervisor must run the focused tests on GitHub (rcheck). In particular, no new test is claimed fail-proven here. Windows behavior cannot be executed on Linux.

Commands use .NET 10.0.402 and Release configuration. Generated test outputs were removed between builds to fit the workspace disk limit; no tracked files were removed.

| Command | Exit / result |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 0 errors; existing warnings |
| `dotnet build Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release` | 0; 0 errors; focused tests compile |
| `dotnet build Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0; 0 errors, 512 existing warnings; WPF app and tests compiled, not executed on Linux |
| `bash .github/scripts/core-guards.sh` | 0; All Core guards passed (3 existing baseline entries) |
| `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release` | 0; 2831 passed, 0 failed, 5 existing Vosk skips, 2836 total |

The first broad build attempt exhausted disk while copying native test assets (exit 134 for affected projects); the sequential pass above supersedes those failures. The first guard attempts could not create temporary files under the read-only sandbox / full disk; the successful pass had normal temporary-file access and free space. No test was weakened or removed. The Vosk model cannot be downloaded here (supplied environment limit), so the existing suite skips its five model-dependent cases.

Pending supervisor command (not run here): `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter "FullyQualifiedName~FeatureCardCompletionTests|FullyQualifiedName~FeatureCardFxTests|FullyQualifiedName~SplitFeatureCardFxTests|FullyQualifiedName~PanicSurfacesTests"`.

## Assumptions

- Use the existing `PrizeOwnership` seam rather than expand this unit into the unported ownership service. Keep the row stub until Release ownership and grant-change notifications exist.
- Apply saved inversion to split tiles in this unit because the localized popup describes all feature tiles. Other split-card FX remain outside scope.
- Keep WPF's three-use hint threshold local to the head, matching the frozen reference's `DashboardToggleHintRule.MaxUses`; no Core setting or abstraction is needed for a UI caption.
- Use an overlay Popup for the Avalonia adaptation and register it with panic cleanup. No new icons or OS-specific implementation was needed.
