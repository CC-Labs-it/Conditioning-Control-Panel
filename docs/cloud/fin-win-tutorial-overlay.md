- Adapted `TutorialOverlay.Live.cs` from parity/trunk commit `e5b3976dd6ab2bf215b89c6e0719e7489dd153ed` (read-only trunk tip inspected: `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`). Existing base files win; the native spotlight window and platform input-region implementation remain.
- Connected all five automatic advance triggers, WPF text matching, edit-before-advance, selection by content/tag, inclusive slider range on release, deferred once-only button signals, and stale-callback protection.
- Added cross-window lookup, waiting cards, WindowLoaded retarget and missed-click recovery, target move/resize/close tracking, preparation callbacks and section highlighting. Owned non-activating windows replace global topmost behavior; Escape works on the host.
- Restored 300ms live appearance and 200ms completion/skip fades. Panic and owner shutdown close immediately. Parameterless render samples remain immediate.
- Connected the ten shell Help guide buttons, the Modding launch, Mod Creator section activation, and the Short Walk's one-image 1600ms flash through the existing flash overlay.
- Connected editor Help, once-only first-run coachmarks, real EffectAdded/RuleAdded/FileSaved publishers, validated Part 1 handoff, and the WPF 800ms Part 2 delay, canceled when the editor closes.
- Added focused text, live trigger/lifecycle, Help routing, Mod navigation and dialog-validation tests; preserved and extended the existing tour/panic test. No new surface: all hosts use the existing SafetyCritical `tour` panic stop.
- Kept `win-tutorial-overlay` as one honest `stub` row.

## WPF gap inventory and remaining work

Reference: pinned base `c54c2df149a903d615fe760853851d07e87931de`, `Windows/TutorialOverlay.xaml(.cs)`, `Models/TutorialStep.cs`, `MainWindow/MainWindow.Settings.cs`, `Windows/ModCreatorWindow.xaml.cs`, and `Views/Deeper/{NewEnhancementDialog,DeeperEditorWindow}.xaml.cs` in the WPF head.

| WPF behavior absent on entry | Result |
| --- | --- |
| Button preview release/Click and close-race recovery; once-only deferred advance | Connected. Avalonia has no readable DialogResult during Closing, so preview release catches confirmation before its handler closes the dialog. X/cancel alone does not count. |
| Text equality/trim/case, numeric tolerance strictly below 0.5, nonnumeric substring | Connected; a matching initial value still requires an edit. |
| Selection content/tag/any-change and slider release with optional maximum | Connected. |
| Exact named OnEvent signals and teardown of subscriptions | Connected to the existing Core bus and editor publishers. |
| WindowLoaded current/next-window recovery, waiting card, move/resize, close guards | Connected; stale deferred callbacks cannot affect a successor. |
| Target preparation and door preparation | Connected. WPF's retained ExpandDoorForTab now calls RefreshSectionRail; this head reuses that method directly. |
| Owned Z-order, nonactivation, live 300/200ms fades, shutdown cleanup | Connected; panic bypasses the closing fade. |
| Help guide starts, Mod section navigation, Short Walk demo | Connected. |
| Editor Help and interactive Part 1/Part 2 consumers | Connected, including validation before queuing Part 2 and its 800ms delay. |
| EMI tour narration | Missing: EmiDeskService has no moment-bus dispatch; importing the narrator alone would have no visible effect. |
| Awareness keyword demo | Missing: the KeywordTriggerHead stand-in has no KeywordTriggerEngine. |
| Automatic first-run editor coachmarks | Connected once per profile, deferred until layout; skipped during another tour or rendering. |

Trunk's in-window stage is an alternative architecture, not WPF behavior. Keeping the base's native window avoids replacing the completed click-through work. WPF's obsolete BtnSettings/BtnProgression targets also lack XAML declarations: `git grep -n -E 'x:Name="(BtnSettings|BtnProgression)"' c54c2df149a903d615fe760853851d07e87931de -- ConditioningControlPanel` returns no matches (exit 1). They retain WPF's missing-target centered fallback. The two editor field names in the old missing-target list are assigned dynamically by the existing RuleEditor implementation.

P122 is not used: there are live unported behaviors, so this row cannot be marked wired. This is not a dependency-only NOOP; substantial independently reachable behavior changed.

## Verification limits

- Checkout verification: supplied checkout was clean but at a different commit; its fetch failed read-only. Used the authorized cloud clone fallback, verified the exact pinned commit and a clean `work` branch, then created only the requested unit branch. The base remote-tracking ref also equals the pinned commit.
- .NET SDK: `10.0.402`.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors.
- Every Linux test project listed in `.github/workflows/build.yml` compiled: CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings, Core.Tests. Each exit 0, zero errors. The final tutorial test build also compiled the final editor integration: exit 0, zero errors.
- WPF test project: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. Windows code compiled but was not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- Text regression before implementation: six cases failed, exit 1, because the matching behavior was absent. After implementation the focused non-window filter below passed all 14 tests, exit 0.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~TutorialTextMatchingTests|(FullyQualifiedName~TutorialTourTests&FullyQualifiedName!~ATourStartsStepsClosesAndPanicEndsIt)|FullyQualifiedName~EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered'`: 14 passed, zero failed/skipped. `DiffEngine_Disabled=true` was set.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 1; 3547 passed, 1 failed, 5 skipped. Failure: `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls`, expected Loops, actual Clips, line 185. Core and Core test sources are byte-for-byte unchanged from the pinned base (`git diff --quiet` exit 0). Speech tests skipped under their existing Vosk-model availability rule; the model download is blocked in this cloud environment. No tests were weakened or disabled.
- Window-based tests (`TutorialLiveTests`, `TutorialGuideTests`, tour window lifecycle and input-region tests) compiled but were not run under the supplied cloud font limitation. No claim of rendered parity, real desktop input, Windows execution, or render-all verification is made. The supervisor must run these on GitHub.
- Initial bulk builds exhausted temporary disk capacity copying native libraries. Retried suites one at a time, deleting only generated test output between builds; the successful results above are from those retries.

## Assumptions

- Preserve the native spotlight implementation and reuse the head's current section rail; do not import trunk's alternative stage or revive removed accordions.
- Keep missing-target fallback and existing tour copy/order; do not invent replacement controls for obsolete WPF targets.
- Leave the ledger stub while the named narration and keyword-engine gaps remain.
