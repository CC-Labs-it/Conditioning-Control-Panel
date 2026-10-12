# friends-core-a: Friends feed + presence switch move to CCP.Core

Base: `avalonia-port/possession-core-a`. Unit A of the `ConditioningControlPanel/Services/Friends` Core move.

## Survey: what Services/Friends already had in Core

Already in `CCP.Core/Services/Friends` before this unit (one copy, WPF routes to it): `FriendsService`
(+ `.Actions`, `.Receipts`), `IFriendsService`, `FriendsApi`, `FriendsModel`, `FriendEvent`, `FriendReceipts`,
`FriendsSeen`, `FriendsSentBook`, `PresenceActivityStack`, and the rule files `FriendsFeedRules`,
`FriendsLandingRules`, `FriendsDrawerRules`, `FriendNoticeRules`, `FriendsPollRule`, `FriendsSfxRules`.

Still in WPF before this unit: `FriendsFeed.cs` (feed store + `FriendsFeedFile`), `FriendsPresenceSetting.cs`,
`FriendsFeedTray.cs`, `FriendsLanding.cs`, `FriendsServiceApp.cs`, `FriendsSfx.cs`.

## What changed

- `git mv ConditioningControlPanel/Services/Friends/FriendsFeed.cs -> CCP.Core/Services/Friends/FriendsFeed.cs`.
  App seams: `App.Logger` -> `Serilog.Log`, `App.UserDataPath` -> `CorePaths.UserData` (so the feed files land
  under `CCP_USERDATA_DIR` in sandboxes), and `CreateForApp(service)` -> `CreateForApp(service, account)`:
  the head passes the account (`BackRoomApi` is still WPF). WPF `App.xaml.cs` passes
  `() => BackRoomApi.AppIdentity()?.UnifiedId`, which is what the method read before, so WPF behaves the same.
  Same adaptation as trunk `48568f5e3` / `b85ec3348` (parity friends: Core feed), but kept in the
  `ConditioningControlPanel.Services.Friends` namespace: here the WPF copy moved instead of staying, so there is
  no second `FriendsFeed` to dodge.
- `git mv ConditioningControlPanel/Services/Friends/FriendsPresenceSetting.cs -> CCP.Core/Services/Friends/`.
  `App.Logger` -> `Serilog.Log`. The `<see cref="FriendsPresenceSetting"/>` in Core `IFriendsService` now resolves.
- Avalonia `AccountSettingsSection` had an inline copy of the presence Read/Write rule; it now calls the Core
  `FriendsPresenceSetting` (one copy of the rule). Same behaviour: a throwing/absent service falls back to
  `CoreSettings.FriendsPresenceShared` (+ `CoreSettings.Save()`), and the ask is spent either way.
- Tests: `git mv Tests/ConditioningControlPanel.Tests/FriendsFeedTests.cs -> Tests/CCP.Core.Tests/` (pure; the
  language-file check now reads `CCP.Core/Localization/Languages` instead of WPF `SourceRoots`). The pure
  no-service presence test moved out of WPF `FriendsPresenceTests.cs` into
  `Tests/CCP.Core.Tests/FriendsPresenceSettingTests.cs`, plus one new case (a failing save still spends the ask).
  The through-the-service presence test stays in WPF `FriendsServiceTests` (it uses that suite's `Make` helper).
- No embedded resources moved, so no core-guards baseline entry.

## Next units (rest of Services/Friends)

- **friends-core-b**: `FriendsSfx` -> Core over the existing `FriendsSfxRules`/`FriendsSfxGate`, with the audio,
  video-playing, Emi-hold and session probes as provider seams (CoreAudio); Avalonia then drops its own cue
  table (trunk `CCP.Avalonia/Views/Controls/Friends/FriendsSfx.cs`).
- Head-bound, stay in WPF (their rules are already Core): `FriendsLanding` (windows, dispatcher, startup
  ladder; rules in `FriendsLandingRules`), `FriendsFeedTray` (WinForms `NotifyIcon`; rule in
  `FriendsFeedRules.TrayText`), `FriendsServiceApp` (WPF wiring + `DispatcherTimer`).
- Avalonia UI: the feed host / tray line / drawer feed (trunk `FriendsFeedHost`, `FriendsFeedTray`,
  `FriendsDrawer.Feed`) can now build on the Core `FriendsFeed` directly.

## Verification limits

Run here (Linux, .NET SDK 10.0.112, `DiffEngine_Disabled=true`):

- `dotnet build` CCP.Core, CCP.Avalonia, and every Linux test csproj in build.yml (Core, Core.Settings,
  Core.Content, Avalonia.CatalogueStartup/Consent/GeneralSettings/Language/Report/Scheduler/Snapshot): all succeed.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`:
  succeeds, so the WPF head (`App.xaml.cs` call site) and the trimmed `FriendsPresenceTests.cs` compile.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1255 passed, 5 skipped, 2 failed, both from this container and unrelated to
  Friends: `LoopbackOAuthTests ... (host: "[::1]")` (no IPv6 loopback here) and
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_...` (runs as root, so the folder can be deleted).
  The moved/new tests: `FriendsFeedTests` + `FriendsPresenceSettingTests` 33/33 passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter AccountSettingsSectionTests`: 6/6 passed (includes
  `PresenceSwitchAndChasterRowWork`, which drives the Avalonia presence switch now routed to Core).

Not run: the WPF suite (`ConditioningControlPanel.Tests`, Windows-only runtime), so the remaining WPF
`FriendsServiceTests.Setting_row_goes_through_the_service_and_spends_the_ask` and the WPF app's feed start were
compiled but not executed here.
