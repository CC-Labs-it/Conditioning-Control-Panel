# 7.2 lane recut

Base: 74ac6ce3d5e0ae082c2871c00e864943e15aa2ad. Local branch work.
Fetched and read own-work diffs: c2bc2f5aaa47..7b2c2ff46d78,
c2bc2f5aaa47..aff7d0b4ffb2, 56796acbf4f7..0a82a2c2430b.
Only the feature-card gap is included in this bounded commit.
No WPF or Core source changes. No new localization keys.

| Behaviour | WPF spec | Avalonia evidence and result |
| --- | --- | --- |
| Inactive opacity and grey art | Features/FeatureCard.xaml.cs:563,584 | Views/Features/FeatureCard.axaml.cs:315,329, ported; Core CardMuteRule and Helpers/CardArtDesaturate reused |
| Hover restores art except on dashboard | Features/FeatureCard.xaml.cs:784 | Views/Features/FeatureCard.axaml.cs:443, ported |
| Lock and tease override grey; tutorial lights art | Features/FeatureCard.xaml.cs:589 | Views/Features/FeatureCard.axaml.cs:331, ported |
| Dashboard socket, travel, bevel, tilt, release | Services/UI/DashboardCardDepth.cs:32,65 | Views/Features/DashboardCardDepth.cs:43,109, ported; Core DashboardDepthLaw reused |
| Dashboard does not scale or pop art | Features/FeatureCard.xaml.cs:784 | Views/Features/FeatureCard.axaml.cs:453,463, ported |
| Opt-in v2 pill | Features/FeatureCard.xaml.cs:264; Features/FeatureCard.xaml:214 | Views/Features/FeatureCard.axaml:175; FeatureCard.axaml.cs:197, ported control API; ownership wiring remains part B |
| Inverted dashboard toggle gesture | Features/FeatureCard.xaml.cs:810 | Views/Features/FeatureCard.axaml.cs:475, already present, preserved Core DashboardClickRules |
| Motion, focus, effective visibility and tease rim | Features/FeatureCard.xaml.cs:613 | Views/Features/FeatureCard.axaml.cs:341, already present; existing tests retained |
| Scrolller launch and exit | App.xaml.cs:2567; App.xaml.cs:6284 | App.axaml.cs:624,1291; Platform/ScrolllerHead.cs:32,42, already present |
| Copy sweep, purge and first-show copies | Services/Fyp/Online/ScrolllerCopySweeper.cs:1 | Platform/ScrolllerHead.cs:58,71; Views/Windows/WelcomeShow/FirstShowStageWindow.cs:234, already present; base retained |
| Scrolller away admission, inherited row 211 gap | Services/Fyp/Online/ScrolllerAwayWatcher.cs:1 | Platform/ScrolllerAwayHost.cs:23, already has Windows and logind hooks; no older weaker replacement |
| Cache-day credit | Views/Tabs/AssetsTabView.xaml:462 | Views/Tabs/AssetsTabView.axaml:377, already present |
| FYP page freshness | Services/Fyp/FypHostService.cs:352 | Views/Games/GameWindow.Fyp.cs:232, missing, part B |
| Corner GIF source privacy | Services/Media/CornerGifMedia.cs:124 | Views/Overlays/CornerGifOverlay.cs:278,294,330, still logs paths; covered by assigned PR #3425 per triage, not duplicated |
| Every-page favorites drawer | Controls/NavRail/PageFavoritesDrawer.cs:166,187; MainWindow/MainWindow.FavoritesRail.cs:151 | Views/Windows/MainShellWindow.FavoritesRail.cs:1, missing page drawer; Home drawer exists in Views/Tabs/SettingsTabView.FavoritesDrawer.cs:1; part B |
| Favorites-first palette and pin mark | Windows/SettingsPaletteWindow.xaml.cs:259,656 | Views/Windows/SettingsPaletteWindow.axaml.cs:280, missing; part B |
| Asset fine-tune layout and preset scope | Views/Tabs/AssetsTabView.xaml:416; Views/Tabs/PresetsTabView.xaml:732 | Views/Tabs/AssetsTabView.MediaPicker.cs:99; Views/Tabs/PresetsTabView.axaml:653; triage assigns row recuts to #3409 and #3412; not duplicated |

WPF paths above are relative to ConditioningControlPanel/.
Avalonia paths above are relative to CCP.Avalonia/.
PR coverage is the supplied triage assignment, not an independent review of PR heads.

## Tests and fail-proofs

FeatureCardFxTests.cs:183 covers opt-in dim, grey pixels, live hover, active,
lock, tease, fade and motion Off. :228 covers dashboard hold, no scale,
press/release, active sink, lock rejection, preserved click inversion. :279 covers the reversible v2 pill.
:291 covers tilt clamping, release overshoot and endpoints. Existing tests stay intact.
Mystery and Vault opt into depth (WPF Views/Tabs/SettingsTabView.xaml:933,1041;
Avalonia Views/Tabs/SettingsTabView.axaml:806,897). Toggle tiles already opted in.
The older Just Drop tile is now the Deeper Editor in WPF (:627); that caller
is outside this feature-card recut.

Validation on Linux, .NET 10.0.403:
- dotnet build CCP.Core: 0 errors (50 warnings).
- dotnet build CCP.Avalonia: 0 errors (201 warnings).
- dotnet build Tests/CCP.Avalonia.Tests: 0 errors (144 warnings).
- dotnet test Tests/CCP.Core.Tests: 4635 passed, 6 skipped, 0 failed.
- bash .github/scripts/core-guards.sh: All Core guards passed.
- python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.
- Initial FeatureCardFxTests run: 7 passed, 0 skipped, 0 failed.
- Temporary mutants, restored after each run: opacity .62 -> .63 and release
  overshoot removed: 3 failures; grayscale disabled and tilt neutralized:
  3 failures; press disabled and v2 visibility disabled: 2 failures.
  The final independent depth failure reaches IsPressed, and the badge failure
  reaches visibility. Earlier failures masked these, so they were run separately.
- badge_v2 already exists in all 10 language JSON files; no new copy keys.
- Final restored-source run: dotnet test Tests/CCP.Avalonia.Tests --no-restore
  --filter FullyQualifiedName~FeatureCardFxTests: 8 passed, 0 skipped, 0 failed.

## Part B

FYP new-to-page tracking; every-page favorites drawer; palette pinned-first
opening rows and star. Keep asset/preset recuts and privacy/sweep with their
assigned PRs. Prize-service ownership of ShowV2Badge is still absent; this
commit ports the opt-in visual API, matching the old lane's scope.
No WPF side-by-side verification was performed.
