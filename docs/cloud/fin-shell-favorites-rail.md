# fin-shell-favorites-rail

- Restore the logo-face gesture caption with WPF wording, including inverted clicks. Successful toggle gestures retire it after three uses; refused session-locked toggles do not count. Re-entry and language repaint read the persisted count.
- Apply a mod author's `railChip` framing through the existing Core framing rule. Match manifest resource/surface keys without case sensitivity and normalize path separators; repaint both drawers on mod changes. Unframed overrides retain WPF's centered fill.
- Render the compact champagne v2 pill on the Studio destination from Core `V2Badges` and the head's existing `PrizeOwnership` seam. Repaint on `PrizeGrants.GrantsChanged`; preserve the separate lock mark. No settings field grants ownership.
- Add WPF's every-page favorites drawer: 22 DIP handle, 92 DIP body, 200 ms cubic slide honoring motion settings, mod colors, translated captions, the same art/order/pin menus as Home. It pushes page content, starts closed, closes on navigation, opens when a pin lands off Home, and never persists Home's open setting.
- Register both drawers and click tips with `PanicSurfaces`. Add focused headless tests and keep one honest `stub` ledger row.
- Reference provenance: WPF reference pinned at base `cc00d8e3922e85ca61b8f65b2c853cbff166affb`. Read `MainWindow.FavoritesRail.cs`, `SettingsTabView.xaml`/`.xaml.cs`, `MainWindow.Presets.cs`, `MainWindow.SectionChrome.cs`, `PageFavoritesDrawer.cs`, `MainWindow.xaml.cs` framing lookup and `PlayTabView.xaml`/`.Cards.cs`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no files or code were imported from trunk. Existing head files were merged in place; the WPF reference and Core are unchanged.

## WPF gap audit

At the base, this row lacked:

1. Logo hint host, swapped-click wording, persisted retirement after three successful toggles, and refresh from the dashboard repaint funnel. Implemented.
2. Manifest-authored `railChip` framing (including normalized keys) and a mod-change repaint. Implemented.
3. Compact v2 chip pill and grant-change repaint. UI implemented against the established ownership seam. **Missing:** live Release account grants depend on `svc-prize-ownership` (BackRoomApi snapshots feeding OwnershipService/PrizeGrants). Release still owns no prizes; this lane does not invent grants or a second service.
4. The non-Home page drawer, list mirroring, page-pushing layout, navigation closure and pin reveal. Implemented.
5. Pin targets for WPF's Back Room, DTRH, Arcademy and Racing Thoughts cards. **Missing:** those four cards are absent from this head's `views-tab-play` surface. Their WPF buttons and click handlers are live, so P122 does not apply. Adding unresolved names to the pin map would not make a user path. Existing palette game rows remain pinnable.

The existing favorites/recent rules, navigation, art fallback, premium lock repaint, language repaint, pin capacity/menu, Home handle/peek/glow, click-tips popup, click inversion, and non-overlay navigation rail behavior are preserved. WPF's retired overlay-rail hold/watchdog has no equivalent on this head's section rail.

## Verification limits

- Before implementation: the four new `FavoritesRailFinishTests` failed at the missing hint, mod repaint, pill and drawer paths. Headless font creation succeeded in this environment.
- SDK: .NET 10.x (10.0.402 at final verification; 10.0.100 also installed during bootstrap). Restore succeeded.
- `bash .github/scripts/core-guards.sh`: exit 0, tail `All Core guards passed.`
- `export DiffEngine_Disabled=true; dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`: exit 0, **3,549 passed / 5 skipped / 0 failed**. The existing five Vosk tests skipped because the model is absent; no tests were weakened or changed in Core.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter FullyQualifiedName~FavoritesRailFinishTests`: exit 0, **4 passed / 0 failed**. Real headless gestures, session-lock refusal, persisted hint retirement, normalized mod framing and repaint, prize repaint, page geometry/navigation/pin/panic.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~FavoritesRailShellTests|FullyQualifiedName~FavoritesClickChoiceTests|FullyQualifiedName~HomeLayoutSync6Tests|FullyQualifiedName~PanicSurfacesTests'`: exit 0, **15 passed / 0 failed**. All test commands used `DiffEngine_Disabled=true`.
- `git diff --cached --check`: exit 0. Added-content privacy scan found no personal paths or addresses; the ledger has exactly one row for this id.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false -m:1 -nr:false -p:UseSharedCompilation=false` with `DOTNET_PROCESSOR_COUNT=4`: exit 0, **554 existing warnings / 0 errors**. Both WPF app and WPF tests compiled; neither ran on Linux. The first parallel attempt exited 1 with **MSB4166: Child node 2 exited prematurely**; the single-node retry succeeded.
- WPF behavior is checked against pinned source; Windows UI cannot run on this Linux environment.
- No Vosk model download is attempted: the task reports that download blocked. Model-required speech coverage remains a supervisor/CI check.
- The full Avalonia suite and packaged/native desktop smoke are outside this focused lane.

All required Linux builds used `dotnet build <project> -c Release`; each ended with **0 Error(s)**. Existing warnings remain.

| Project | Exit |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 |

## Assumptions

- The every-page drawer belongs to this row because its live WPF favorites builder, refresh and pin paths are in the cited partial.
- Inverted left-click toggles count toward the learned-gesture retirement, matching WPF's `ToggleWallFeature` call site.
- The existing prize-ownership seam is the appropriate renderer input until `svc-prize-ownership` lands. The ledger remains `stub` for the two named dependencies.
