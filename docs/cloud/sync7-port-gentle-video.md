# sync7-port-gentle-video

- Wire the existing chess gentle-intro rule into the Avalonia boot settings frame. Gentle visuals caps the frame's motion flag and supplies a 0.5..1 intro speed only when the configured motion level is Full. Explicit Reduced/Off retain a zero override.
- Extend the existing Core frame builder; preserve the current host's other fields and boot order. Add seven Core cases and five headless host cases.
- No new surface; the existing games panic registration remains in use (`CCP.Avalonia/Views/Games/GameWindow.cs:236`).
- Source: WPF `b2d45bfff68e204039d96fa9a28930ef234b6b50` and `362aaff94f1c5ccbfe1d696410c753a11d843982`, read with `git show` and `git show --stat`. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Existing files and sibling behaviour retained.

## Per-commit disposition

- `b2d45bfff`: **blocked-by-host**. There is no Avalonia equivalent of `VideoService.FloatingInstances` or its per-instance browser init/tick protocol. The existing `CCP.Avalonia/Views/Overlays/MandatoryVideoOverlay.cs:122` is a fullscreen LibVLC host, not floating browser playback. Do not invent that host in this unit. Its rate/time division was superseded by `362aaff94`; a future floating host should use rate 1, the configured gentle fade, and unscaled position for its cap.
- `362aaff94`: **ported (chess host)** at `CCP.Avalonia/Views/Games/GameWindow.Pbp.cs:151` and `CCP.Core/Services/PieceByPiece/PbpHostRules.cs:130`. Tests: `PbpHostRulesTests.Settings_GentleIntroOverride_ReachesThePageOnlyWhenGentleIsTheOnlyBrake` and `GameWindowTests.PieceByPiece_GentleIntroSettingsReachPage`. **Already-covered** pure clamp/no-override rule and watch frame at `CCP.Core/Services/PieceByPiece/PbpWatchRules.cs:147` and `:169`; nine requested tooltips are already updated in `CCP.Core/Localization/Languages/` (English at `en.json:5375`). **Blocked-by-host** floating video portion and the missing gentle video-window fade surface/seam (`GentleWindowRules` has no current Core/Avalonia consumer). The existing fullscreen player already plays at normal speed without calling SetRate (`MandatoryVideoOverlay.cs:213`); no new unused playback rule added.

## Verification limits

- .NET 10.0.100 installed. `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors (existing warnings).
- `dotnet build <project> -c Release`: exit 0, 0 errors for all twelve Linux workflow test projects: Core, Core.Settings, Core.Content, Avalonia, Avalonia.CatalogueStartup, Avalonia.Consent, Avalonia.GeneralSettings, Avalonia.Language, Avalonia.Migration, Avalonia.Report, Avalonia.Scheduler, Avalonia.Snapshot.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed. `test-plan-coverage.sh` is absent on this base; not run. No parity or sync ledger edited; manual tester step 7.14 added.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~PbpHostRulesTests`: exit 0, 31 passed.
- Regression proof: remove only the production `gentleIntroSpeed` frame field and run `--filter FullyQualifiedName~Settings_GentleIntroOverride`: exit 1, all seven new cases fail (missing field). Restore the field and rerun the focused class: exit 0, 31 passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 1, 3959 passed, 1 failed, 5 skipped. Failure: untouched `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` expected Loops, got Clips. Vosk tests self-skip without the model; no tests were modified to skip. Focused `--filter FullyQualifiedName~DtrhRemoteMediaTests --no-build` recheck: exit 0, 9 passed; the full-suite failure did not reproduce in isolation.
- Avalonia headless tests compiled, not run: this cloud environment's reported font-creation limitation requires supervisor rcheck. In particular, run `--filter FullyQualifiedName~GameWindowTests.PieceByPiece` on a supported runner. No real desktop/profile was opened; the existing headless test harness supplies `CCP_USERDATA_DIR` and memory-only secrets.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors (605 warnings). Windows code compiled but cannot be executed here.

## Assumptions

- Apply the final combined WPF behaviour: normal-speed videos supersede the earlier slowdown; do not reintroduce the reverted speed/time scaling.
- Avalonia has no cross-platform OS animation-effects setting seam. Follow existing head motion surfaces and pass the configured level without gentle to the Core rule; documented with a `ponytail:` comment.
- Keep the current reduced chess host protocol; do not port sibling spectating/media features just to use the larger watch settings frame.
