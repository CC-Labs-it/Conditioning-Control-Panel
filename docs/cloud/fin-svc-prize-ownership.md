# fin-svc-prize-ownership

- Reuse Core `OwnershipService` and `PrizeGrants` in the Avalonia adapter. All 16 client grant ids now use the same account/revision/delta rules and DEBUG override as WPF; a missing network token or offline mode does not erase an authenticated account's in-memory ownership.
- Adapt WPF `ProfileSyncService.PrizesFeed` and its behavioral tests into Core. Queue profile/provider updates and logout clearing in FIFO order, drop late replies for another account, preserve missing blocks, accept empty revocations, and clear on an account switch before its next response.
- Feed ownership before other successful V2 sync flags, wire all three provider-validate sinks before initialization, retain counter-reply delivery, and clear on logout/reset. No grants are written to settings, provider caches, or outgoing sync bodies.
- Repaint the existing Flashes picker and Get it row after grants/revokes without changing a saved unavailable motion preference. Subscribe on attach and unsubscribe on detach.
- Follow racing grants in the launcher and open race: additions send `race-ownership`; removal of any previously owned track closes the race and disposes its timer/player. No new surface: the existing `games` panic registration remains responsible for these windows.
- Keep the ledger row `stub` for its original broader effect scope. Pendulum and Jackpot Remix depend on the unported effect paths in `feat-flash`; Bubble Rain/Spiral-in depend on `feat-bubble-pop`. Their ids are held and answered correctly, but those effects cannot yet play. The racing track ownership path is live.
- Import credit: no code imported from `parity/trunk`. Read-only source inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` (its ownership adapter last changed in `0b2858e580915727d7d5d9cc6ba6df85cb74cda7`). The base's existing Core service wins; the ordered feed/tests are adapted from WPF on pinned base `cadbc3f87a49ff22fac87d529437878d5eff7e64`.

## WPF gap audit at the pinned base

The row's old `ConditioningControlPanel/Services/Prizes/OwnershipService.cs`, `PrizeGrants.cs`, and `Services/BackRoom/BackRoomApi.cs` citations have already moved to `CCP.Core` on this base. The WPF head consumes those shared types. Relevant WPF callers and behaviors are:

- `ConditioningControlPanel/App.xaml.cs:2745-2763`: construct the ownership service, marshal events to the UI dispatcher, attach the facade; `:404-405` installs provider validation and `:494` installs counter snapshots. The Avalonia adapter had a second store, no facade attachment, no notifications, and unnecessarily required a transport identity. Replaced with the shared service.
- `ConditioningControlPanel/Services/Settings/ProfileSyncService.cs:574-708`: logout clears ownership; the feed always queues, clones the response's grants, checks both requested/resolved accounts, notes account switches even without a prizes block, and drops obsolete account work without clearing the current account. Missing in Avalonia; now adapted and tested.
- The same file `:1653-1655` notes the account before sync and `:1882-1884` applies prizes before other flags. Missing in Core SyncPush; now connected, including post-load and subsequent pushes.
- `CCP.Core/Services/Account/ProviderSubscription.cs:300` and `DiscordAccount.cs:154`: Patreon/SubscribeStar/Discord validate already offered a prize sink, but this head did not seed it. Now seeded before provider initialization.
- `ConditioningControlPanel/Features/FlashFeatureControl.xaml.cs:28-43,81-85` and `Controls/V2GetItRow.xaml.cs`: grant-change subscriptions rebuild ownership-dependent controls. Avalonia only rebuilt after its own purchase, so sync grants and revokes could leave stale controls. Existing controls now subscribe to the shared facade.
- `ConditioningControlPanel/Services/Chaos/CaucusHostService.cs:1102-1109`: unchanged ownership is silent, new tracks update the page, losing any track closes the host. The watcher was missing; now wired alongside launcher reveal updates.
- Shared `OwnershipService` explicitly says in-memory only; `Models/PatreonModels.cs` says grants are never cached. The ledger's persistence-across-runs claim is stale, not WPF behavior. Validate/sync fetch fresh grants each run; no local persistence was added.
- Remaining original-row consumers: Pendulum, Jackpot Remix, Bubble Rain and Spiral-in effect playback/gates. These are feature dependencies listed above, rather than missing grant ids or a missing ownership feed. No P122 dead-caller exemption is claimed.

## Verification limits

- Checkout: the supplied snapshot differed from the expected commit and its fetch failed read-only. Used the explicitly authorized writable fallback; verified clean HEAD exactly `cadbc3f87a49ff22fac87d529437878d5eff7e64` before creating the unit branch.
- SDK: .NET 10.0.402. Release configuration, with `DiffEngine_Disabled=true` for tests.
- Regression proof before the fix: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~PrizeOwnershipTests` exited 1; the new shared-facade assertion failed for every client grant. This test does not create fonts, windows, or a network connection.
- After the fix: the two non-rendering regressions (`Snapshot_reaches_shared_facade` and `Sync_and_provider_validate_share`) pass, 2 passed / 0 failed. The integration test uses a fake HTTP handler and isolated test profile, including all grant ids, no outgoing prizes, missing blocks, a mismatched provider response, malformed unrelated server flags, revocation, and logout.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0, 3556 passed / 0 failed / 5 existing Vosk-model-dependent tests skipped. Includes the adapted ordered-feed tests and existing ownership/revision/override tests. Vosk model download is blocked in this environment; no tests were changed to bypass it.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML references checked). The first read-only-sandbox invocation could not create scratch files; the writable rerun passed.
- Required Release builds: Core, Avalonia, and all 12 Linux test projects listed in `.github/workflows/build.yml` exited 0 with 0 errors. The test projects are Core, Core.Settings, Core.Content, Avalonia, Avalonia.Consent, Avalonia.Language, Avalonia.CatalogueStartup, Avalonia.Migration, Avalonia.Scheduler, Avalonia.Report, Avalonia.GeneralSettings, and Avalonia.Snapshot. Outputs were removed between builds because copying the shipped web assets for every suite exceeds the cloud disk capacity. Initial matrix failure was disk exhaustion; sequential rebuilds all passed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors, 554 existing warnings. The WPF app and test project compiled; neither was run on Linux.
- Rendering/window tests are compiled but not run here, per the supplied environment limit: this base lacks the cloud test-font lane and font creation is known to fail. Relevant supervisor filters: `PrizeOwnershipTests`, `V2GetItRowTests`, `AccountSeedTests`, `GameWindowTests`, `LauncherWindowTests`, and `PanicSurfacesTests`. No real desktop or real profile is used by the tests.
- Windows-specific code cannot execute on Linux. No live production-server purchase/validate, desktop effects, or render-all was run; this row remains `stub`.

## Assumptions

- Follow WPF's explicit server-authoritative, memory-only contract instead of the stale ledger request for persistence.
- Keep the diff within the ownership unit: leave unported effect implementations in their feature rows and retain `stub` for the original row scope.
- Reuse existing popup/window panic registration; this unit adds no new surface.
