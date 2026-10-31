# 7.2 lane recut

Base: 74ac6ce3d5e0ae082c2871c00e864943e15aa2ad. Local branch work.
Fetched and read own-work diffs: c2bc2f5aaa47..7b2c2ff46d78,
c2bc2f5aaa47..aff7d0b4ffb2, 56796acbf4f7..0a82a2c2430b.
Only the feature-card gap is included in this bounded commit.
No WPF or Core source changes. No new localization keys.

| Behaviour | WPF spec | Avalonia evidence and result |
| --- | --- | --- |
| Inactive opacity and grey art | Features/FeatureCard.xaml.cs:563,584 | Views/Features/FeatureCard.axaml.cs:315,329, ported; Core CardMuteRule and Helpers/CardArtDesaturate reused |
| Hover restores art except on dashboard | Features/FeatureCard.xaml.cs:784 | Views/Features/FeatureCard.axaml.cs:444, ported |
| Lock and tease override grey; tutorial lights art | Features/FeatureCard.xaml.cs:589 | Views/Features/FeatureCard.axaml.cs:331, ported |
| Dashboard socket, travel, bevel, tilt, release | Services/UI/DashboardCardDepth.cs:32,65 | Views/Features/DashboardCardDepth.cs:44,114, ported; Core DashboardDepthLaw reused |
| Dashboard does not scale or pop art | Features/FeatureCard.xaml.cs:784 | Views/Features/FeatureCard.axaml.cs:454,464, ported |
| Opt-in v2 pill | Features/FeatureCard.xaml.cs:264; Features/FeatureCard.xaml:214 | Views/Features/FeatureCard.axaml:175; FeatureCard.axaml.cs:197, ported control API; ownership wiring remains part B |
| Inverted dashboard toggle gesture | Features/FeatureCard.xaml.cs:810 | Views/Features/FeatureCard.axaml.cs:476, already present, preserved Core DashboardClickRules |
| Motion, focus, effective visibility and tease rim | Features/FeatureCard.xaml.cs:613 | Views/Features/FeatureCard.axaml.cs:342, already present; existing tests retained |
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
press/release, active sink, lock rejection, preserved click inversion. :348 covers the reversible v2 pill.
:360 covers tilt clamping, release overshoot and endpoints. Existing tests stay intact.
Mystery and Vault opt into depth (WPF Views/Tabs/SettingsTabView.xaml:933,1041;
Avalonia Views/Tabs/SettingsTabView.axaml:806,897). Toggle tiles already opted in.
The older Just Drop tile is now the Deeper Editor in WPF (:627); that caller
is outside this feature-card recut.

Validation on Linux, .NET 10.0.403 (initial recut):
- Builds of CCP.Core, CCP.Avalonia and Tests/CCP.Avalonia.Tests: 0 errors.
- Core tests: 4635 passed, 6 skipped; Core guards and avalonia_docs self-check passed.
- Restored-source feature tests: 8 passed. Temporary opacity, grayscale, press,
  badge, tilt and release-easing mutants failed; masked failures were rerun independently.
- badge_v2 already exists in all 10 language JSON files; no new copy keys.

## Part B

FYP new-to-page tracking; every-page favorites drawer; palette pinned-first
opening rows and star. Keep asset/preset recuts and privacy/sweep with their
assigned PRs. Prize-service ownership of ShowV2Badge is still absent; this
commit ports the opt-in visual API, matching the old lane's scope.
No WPF side-by-side verification was performed.

## Review P2 follow-up

WPF Services/UI/DashboardCardDepth.cs:81 retargets tilt from its current angle.
DashboardCardDepth now reuses the tilt transition and ignores unchanged targets,
following SplitFeatureCard.SetDepthValue/TiltTo; travel easing remains unchanged.
FeatureCardFxTests.ContinuousPointerMovesRetargetOneTiltTransition sends changing
and repeated pointer positions through the real headless window. It failed with
Assert.Same on reviewed commit 2a25232c7 before the fix. No new copy keys.
Review validation: all three required builds had 0 errors; Core tests: 4635
passed, 6 skipped, 0 failed. FeatureCardFxTests plus SplitFeatureCardDepthTests:
13 passed, 0 skipped, 0 failed. Core guards and avalonia_docs self-check passed.

Review interruption P2s: travel now preserves its displayed position before replacing
a tween (WPF Services/UI/DashboardCardDepth.cs:124); mute reuses its opacity
transition (WPF Features/FeatureCard.xaml.cs:602). Hand-stepped interruption tests
failed on 5ebfa524d: release jumped from 1 to 2; fade reversal jumped from .75 to 1.
Interruption validation: required builds 0 errors; Core 4635 passed, 6 skipped;
affected head tests 19 passed, 0 skipped. Guards/docs passed; fail-proofs above.
