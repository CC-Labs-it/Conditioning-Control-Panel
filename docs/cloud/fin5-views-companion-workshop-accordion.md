# FIN5 Workshop accordion

- Base verified at `42707759b79230af48aacd18daed69bbfa99e5d1`; branch names were not used to establish snapshot identity.
- Ported the WPF community prompt host: browse the first five uninstalled entries, install the first after confirmation, empty/all-installed messages, refresh with busy state, offline cached discovery, JSON import/export, installed rows, activation acknowledgement, and uninstall.
- Reused Core installed-prompt storage, validation and activation rather than overwriting the roster implementation. Import remains independent of assignment/activation; roster import continues to assign and reapply the active companion.
- Added panic registration for community dialogs and a generation fence for late network/file completions. Added Core tests and headless real-drawer tests, including first-five ordering and install-first behavior.
- Replaced the duplicated historical accordion ledger description with one honest `stub` row and numbered tester steps.
- Source: WPF reference at the pinned base, especially `MainWindow/MainWindow.Patreon.cs:2334-2480`, `MainWindow/MainWindow.CompanionTab.cs:175-320`, `Services/Companion/CommunityPromptService.cs`, and both cited WorkshopAccordion files. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk files/code imported.

## Gap audit and assumptions

At the base the drawer already composed six cells, expanded/deep-linked on the normal dispatcher turn, persisted its state through the room, hosted library editing and roster switch/personality actions, and implemented phrase-preset selection/save/delete. The ledger's phrase-preset omission was stale. Missing community actions and installed-list population are implemented here.

Remaining live WPF behavior (no P122 exemption):

- Camera shortcut capture/application and system-wide chat/camera shortcut routing (`MainWindow.SessionIO.cs`, `Services/Input/GlobalHotkeyService.cs`). The head has WebcamTracker, but the global toggle route is absent; configuring a binding alone would not complete that behavior.
- Browser pause/resume: WPF engine mute plus document media pause/play and the tube quick-menu state (`MainWindow.Patreon.cs:2142-2204`). WebHost has a script channel and document mute helper, but no engine-wide mute across cross-origin players; its existing mute limitation must not be reported as full WPF parity.
- Live `CompanionPhraseService` trigger timer, persisted trigger controls/editor, phrase-count refresh and saved preset active-count (`MainWindow.Patreon.cs:1336-1339,1489-1538`, `MainWindow.Assets.cs:1773-1900`). The WPF phrase service alone is 522 lines before its head/runtime wiring and tests. Those dependencies plus portable hotkeys/browser adapters do not fit with this unit's tested community work under the 1000-added-line ceiling.

Decisions: retain WPF's simple install-first browser rather than invent a catalog window; use the existing native storage-provider seam for OS pickers; keep WPF export defaults and author fallback; use an ellipse for the existing active marker. Keep the row `stub`, never `verified`, because full runtime parity remains incomplete.

## Verification limits

- .NET SDK 10.0.402 installed explicitly after channel lookup returned HTTP 403.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`, `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`, and `dotnet build Tests/CCP.Avalonia.Tests -c Release -m:1`: passed, zero errors (existing warnings).
- All Linux test projects named in `.github/workflows/build.yml` compiled: Core, Core.Settings, Core.Content, Avalonia.Consent, Language, CatalogueStartup, Migration, Scheduler, Report, GeneralSettings and Snapshot. Consent and Migration initially hit MSBuild worker crashes; serial retries passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: passed, 4121 passed / 5 skipped / 0 failed. Vosk model tests skipped because the model is unavailable; no live speech test claim.
- Final focused Core run (`CommunityPromptCatalogTests|InstalledCommunityPromptsTests`): passed, 4/4.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. This base does not contain `test-plan-coverage.sh`; numbered tester steps were added anyway.
- WPF test project compiled with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors; compiled but not run on Linux.
- Headless Avalonia workshop/roster/phrase/panic classes were attempted (final run: 5 passed, 13 failed); font creation fails in this cloud environment (Segoe UI Black / Inter). One cleanup failure also exposed removal of a missing prompt folder; restored WPF's File.Exists guard and added a Core assertion. They compile, but UI behavior is not established here; GitHub rcheck must execute them with fonts. No real desktop or user profile was used.
- No live catalog endpoint calls or native picker interaction tests; catalog tests use a fake HttpMessageHandler. No visual render-all claim. Windows runtime code cannot be exercised on Linux.
