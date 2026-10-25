# fin4-svc-prize-ownership

- Base verified at `790f4bc702bbc22e68c036715e15bb795368a14b`. The supplied snapshot had a different HEAD and read-only Git metadata, so the explicitly authorized clone fallback was used.
- Added WPF's missing ownership badge consumers: dashboard Flash/Bubble Pop cards, Studio resting and selected rows, and Studio favorites/recent chips. All read the existing Core `V2Badges`; no new ownership store or settings flags.
- Repaint on grants/revokes and initial attachment; balance subscriptions on detach/close. Reuse one WPF-sized champagne badge factory, keep the Studio dot in its own column, measure selected-row caption clearance, and move locked rail-chip pills left.
- Added headless `OwnershipBadgeTests` for every qualifying grant, revocation, unrelated chips, selected/resting rows, caption clearance, and detached/reattached dashboard state. Existing test assembly uses its `CCP_USERDATA_DIR` sandbox. No new windows/overlays in the product; existing shell panic registration covers these decorations.
- Updated the single ledger row and numbered manual tester steps. Status remains **stub**, never wired/verified.
- No trunk code imported. Read-only source branch fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; implementation adapts the WPF reference on the pinned base.

## WPF gap inventory and scope decision

At the base, ownership transport/validation, account binding, revisions/deltas, DEBUG override, reset/logout and racing consumers already existed. Reading the cited Core OwnershipService/PrizeGrants, WPF ProfileSyncService.PrizesFeed and the consumer sites found these remaining gaps:

1. SettingsTabView ownership pill subscription (`:60`, `RefreshV2Badges :424`) and Bubble Pop's equivalent (`MainWindow.Presets.cs:1147`). Implemented.
2. Studio ownership pills (`StudioTabView.xaml.cs:1492-1528`, load subscription `:1583`), both row states and measured caption clearance (`:717`). Implemented.
3. Favorites/recent chip ownership repaint (`MainWindow.FavoritesRail.cs:92`) and locked/unlocked placement (`:256`). Implemented.
4. Pendulum flash motion selection/playback, Jackpot Remix's scheduled composite playback, and Bubble Rain/Spiral In selection/playback. Remain dependent on `feat-flash` and `feat-bubble-pop`.

These last paths are live in WPF, so P122 does not apply: FlashService.cs:984 calls TryShowRemixAsync on ambient events, :1040 constructs the ownership-gated director; its Pendulum path is used from LivePendulumsOn (:2430). BubbleService.cs:3332-3375 chooses Rain/Spiral In, and :4087 steps the spiral. At this base the Avalonia FlashOverlay.BuildMotion explicitly passes `ownsPendulum: false`, the card offers no Pendulum/Remix controls, and BubbleOverlay explicitly implements FloatUp only. This is missing playback, not missing grant recognition.

Full parity requires rotated flash rendering/placement and lifecycle, a Remix builder plus animated composite playback (the WPF builder alone is 449 lines, with browser renderer/media dependencies), and two additional ambient motion hosts plus settings controls and behavior tests. Combining that work with the three consumer ports within 1000 added lines would require incomplete substitutes. The user-visible badge gaps are completed first as instructed; playback stays honestly listed in the ledger. No shortcuts claim ownership grants make unsupported effects playable.

## Verification limits

All commands used .NET SDK 10.0.402 and `DiffEngine_Disabled=true`.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- `dotnet build <project> -c Release` for every Linux test project in `.github/workflows/build.yml`: exit 0, 0 errors. Projects: CCP.Avalonia.CatalogueStartup.Tests, Consent.Tests, GeneralSettings.Tests, Language.Tests, Migration.Tests, Report.Tests, Scheduler.Tests, Snapshot.Tests, CCP.Avalonia.Tests; CCP.Core.Content.Tests, CCP.Core.Settings.Tests, CCP.Core.Tests. The final badge test source was separately rebuilt successfully (0 errors).
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; **3556 passed, 5 existing Vosk skips, 0 failed**. No tests were weakened or skipped by this change.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~OwnershipBadgeTests|FullyQualifiedName~PrizeOwnership|FullyQualifiedName~FavoritesRailShellTests|FullyQualifiedName~StudioRackFxTests'`: exit 1; 1 passed, 6 failed during glyphTypeface creation (Segoe UI Black / Inter), before UI behavior assertions. The new test is compiled; its behavior is **not runtime-verified here**. No before/after behavioral failure proof is claimed. Requires the font-enabled CI runner. Full UI suite and render-all were not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on this base (exit 127); numbered coverage steps were added manually.
- `git diff --check`: exit 0.

Initial overlapping builds failed on Avalonia intermediate-file locks; they were repeated sequentially. The first test build sweep exhausted disk from repeated asset copies; generated test output was removed between projects on the successful sweep. These infrastructure failures did not require product changes. Windows desktop behavior cannot be run in this Linux environment. `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` also exited 0 (0 errors, 554 existing warnings). WPF tests were compiled but not run.

## Assumptions

- Use the WPF reference at the required base commit; preserve its badge text, sizes, placement and ownership families.
- Keep `svc-prize-ownership` stub until the named effect hosts supply the remaining user paths. This is a partial implementation, not the dependency-only NOOP route, because the existing badge surfaces could be completed in this unit.
