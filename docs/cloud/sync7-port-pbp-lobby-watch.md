# sync7-port-pbp-lobby-watch

- Adapt WPF `81b59fcd4f2be7e0defec966ff49a1e30ab8b54f`: enable chess Watch row actions, enforce account/chess gates, and open or focus the existing Piece by Piece game surface.
- Send a fresh board's `spectateMatchId` once in `pbp:settings`; send an existing board `pbp:friend` / `spectate`, queued until identity if still booting. Clear pending targets on close (including panic).
- Publish `letPeopleWatch` each boot and accept only its boolean page setting, saving changed values through CoreSettings. Reuse Core's existing rules, model and localization. Leave sibling watch-effects/media/Join work outside this unit.
- Add headless protocol, persistence, real row-button and gate tests, plus numbered tester steps. No new surface: the existing PanicSurfaces `games` registration covers spectator boards.
- Reference source: WPF commit above. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## Commit disposition

`81b59fcd4`: **ported** for the available head surface:

- `CCP.Avalonia/Views/Games/GameWindow.Pbp.cs:62` (`HostSpectate`), `:152` (setting reception), `:189` (one-shot boot settings and post-identity delivery). Tests: `GameWindowTests.PbpWatch_FreshExistingAndBootingBoardsReceiveOneSpectateIntent` and `PbpWatch_PageSettingPersistsOnlyBooleanLetPeopleWatchAndNextBootReadsIt`.
- `CCP.Avalonia/Views/Tabs/AvailableSubjectsTabView.axaml:59` (enabled Watch handler), `.axaml.cs:102` (row gates and launch). Tests: `LobbyPageTests.WatchRowRequiresSignInAndChessGate` (real templated button), plus the fresh-board test above.

**Already covered** on the required base (unchanged):

- `CCP.Core/Services/Lobby/PbpLobbyApi.cs:95`: server boolean + valid match-id validation and watcher count clamp.
- `CCP.Core/Services/Lobby/LobbyModel.cs:144`, `:244`: watch target/capability, nonnegative count, localized summary after clock/moves.
- `CCP.Core/Services/PieceByPiece/PbpWatchRules.cs:40`, `:46`, `:73`, `:103`: match validation, typed setting reader/apply, default-on preference.
- `CCP.Core/Models/AppSettings.cs:8774`: persisted default-on `PbpLetPeopleWatch`; `CCP.Core/Localization/Languages/en.json:1583` and the other eight Core language files: watcher strings.

**Blocked-by-host** for end-to-end live Lobby discovery: this base's Avalonia Lobby page only paints supplied snapshots; no Avalonia Lobby polling/service lifetime populates them. Do not invent that sibling host here. The Watch action and protocol are exercised with supplied rows. Chess/Goon Host and all Join actions remain disabled.

## Verification limits

- `dotnet build <project> -c Release`: exit 0, zero errors for CCP.Core, CCP.Avalonia, and all 13 test projects named in `.github/workflows/build.yml` (Core, Core.Settings, Core.Content; Avalonia, Consent, Language, CatalogueStartup, Migration, Scheduler, Report, GeneralSettings, Snapshot; ConditioningControlPanel).
- WPF test build used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. WPF/Windows code compiled but was not run on Linux.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`: exit 0; 3953 passed, 0 failed, 5 existing Vosk model-dependent skips. No tests were weakened or disabled by this change; the model is unavailable here.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~GameWindowTests.PbpWatch_|FullyQualifiedName~LobbyPageTests'`: exit 1; 3 passed, 6 failed due to unavailable Segoe UI Black/Fredoka glyph typefaces. The three new tests compile but their behavior assertions were not executed (shell construction fails first). Supervisor rcheck must run them in the font-capable environment. A meaningful production-line revert/fail-before-fix proof is blocked by that same font failure and remains outstanding.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. The first read-only-sandbox invocation could not create temporary files; rerun with write access passed.
- `test-plan-coverage.sh` is absent on this base. Numbered WPF-referenced steps were added anyway; no sync ledger or parity ledger was edited.
- `git diff --check`: exit 0. Added-line count is recorded in the final report (below the 1000-line cap).
- An initial overlapping build collided on Avalonia's generated PDB; sequential builds passed. A missing test namespace import was corrected; the final Avalonia test build has zero errors.
- No live server or native Windows webview execution was attempted. End-to-end live Lobby discovery remains blocked by the absent polling host described above.

## Assumptions

- Keep this lane limited to the listed commit's `letPeopleWatch` setting; do not wire sibling watch-effects/media fields just because newer Core rules already expose them.
- Use the existing game window and panic registration; no separate spectator surface is needed.
- Preserve the WPF pending-intent behavior: repeated watches during boot keep the latest pending target, delivered after identity.
