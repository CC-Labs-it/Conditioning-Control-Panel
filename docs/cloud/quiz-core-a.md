# quiz-core-a: move the AI quiz (QuizService) into CCP.Core

Unit A of the `ConditioningControlPanel/Services/Quiz` Core move.

## Audit of `ConditioningControlPanel/Services/Quiz` before this unit

| WPF file | State on base | This unit |
|---|---|---|
| `PopQuizService.cs` | already a thin WPF host over Core `PopQuizScheduler` (`IPopQuizHost`) | untouched (head-only: windows, interaction queue) |
| `IntakeNiche.cs` | `Resolve` already delegates to Core `IntakeRun.ResolveNiche`; `Current`/`PassCardImage` read `App.Mods` and return a WPF `ImageSource` | untouched (head-only) |
| `IntakeHostService.cs` / `.Speech.cs` | WebView2/DispatcherTimer host; the run rules already live in Core `IntakeRun`, `IntakeProfiler`, `IntakeSpeechPolicy` | untouched (head-only) |
| `QuizService.cs` | **not mirrored**: the AI round trip, prompts, wire parsers, archetype fix and session-content parser existed only in WPF (the Avalonia `QuizWindow` carries ponytails for it) | **moved to Core** |

## What changed

- `git mv ConditioningControlPanel/Services/Quiz/QuizService.cs -> CCP.Core/Services/Quiz/QuizService.cs`
  (shows as a rename). Same namespace (`ConditioningControlPanel.Services`) and public API, so every WPF
  caller (`QuizWindow`, `GamificationBridge`, `IntakeHostService`, the category editor and report) compiles
  unchanged against the Core copy. There is one copy of the quiz rules now.
- `App.*` replaced by the Core seams WPF already seeds at startup (no new seams):
  - `App.Logger` -> Serilog `Log`
  - `App.UnifiedUserId` -> `CoreAccount.UnifiedUserId` (WPF's property is a pass-through to it)
  - `App.Settings.Current` (UseLocalAi, AuthToken) -> `CoreSettings.Service?.Current` (null before startup, as before)
  - `App.ModerationGuard` / `App.ModerationLog` -> `CoreModerationLog.Guard` / `.Record` (seeded from the same App instances)
  - `UpdateService.AppVersion` -> `CoreReleaseContent.AppVersion` (seeded from `UpdateService.AppVersion`)
  - `MergedAccountRecovery.TryHandleAsync` -> `V2AuthService.MergedRecovery` (seeded with that same method)
- Base URL follows Core `AiService.ResolveBaseUrl` (same proxy URL in production; a `CCP_USERDATA_DIR`
  sandbox only reaches a loopback `CCP_AI_BASE_URL`, otherwise never sends). An internal
  `QuizService(string? baseUrl)` ctor serves the tests, as `AiService` has.
- `FixArchetypeIfWrong` and `ParseSessionContent` went from `private` to `internal` so they can be tested.
- New `Tests/CCP.Core.Tests/QuizServiceTests.cs` (8 tests): question parser (last Q / last four answers,
  point clamp, template-echo and short-answer rejection), wire round trip, archetype/range rewrite, session
  content parsing, and the cloud round trip against a loopback fake proxy: signed-in post to `/v2/ai/chat`,
  an output-moderated (but parseable) reply serving the canned question without a re-ask, and nothing sent
  without an identity or without a proxy.
- No trunk import: `parity/trunk` still has `QuizService` in WPF, byte-identical to our base. The existing Core
  quiz files on the base came from trunk `32dde9571` (PopQuizScheduler) and `51b9e5bf5` (IntakeRun).

Behaviour note: before WPF startup set `App.ModerationGuard`, the WPF quiz skipped output moderation
(`guard != null`); `CoreModerationLog.Guard` falls back to the stock guard, so it now fails closed. No quiz
can run before startup, so this is not user-visible.

## Next units

- **quiz-core-b (Avalonia):** wire `CCP.Avalonia/Views/Windows/QuizWindow.axaml.cs` to Core `QuizService`
  (StartQuizAsync / SubmitAnswer / final result / Reset / GenerateSessionContentAsync) and retire its
  ponytails (AI round trip, `RaiseQuizCompleted`, `Reset`).
- **quiz-core-c:** a `QuizCompleted` / `QuizAbandoned` subscriber in the Avalonia head (WPF: `GamificationBridge`),
  feeding Core `AchievementEngine.TrackQuizCompleted` once, and the intake walk-out raise.
- **quiz-core-d (audit):** `IntakeHostService.OnQuizResult` payload handling vs Core `IntakeRun`; anything pure
  that is not yet delegated. `IntakeNiche.PassCardImage`, `PopQuizService` and the WebView2 host stay head-side.

## Verification limits

Run here (Linux container, .NET SDK 10.0.112 from the Ubuntu archive):

- `dotnet build` succeeded for CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, Tests/CCP.Core.Settings.Tests,
  Tests/CCP.Core.Content.Tests, Tests/CCP.Avalonia.Tests and the Avalonia Consent, Language,
  CatalogueStartup, Scheduler, Report, GeneralSettings and Snapshot test projects.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`
  succeeded, so the WPF head compiles against the moved type. **Not run**: WPF needs Windows.
- `bash .github/scripts/core-guards.sh`: "All Core guards passed."
- `dotnet test Tests/CCP.Core.Tests`: 1145 passed, 5 skipped, 2 failed. Both failures are environmental and
  outside this change: `LoopbackOAuthTests...(host: "[::1]")` (the container has no IPv6) and
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (tests run as root, so the
  read-only folder is deletable anyway). `QuizServiceTests`: 8/8 passed.
- The moderation test was checked to fail with the guard temporarily nulled, then restored.
- `dotnet test Tests/CCP.Avalonia.Tests --filter "Quiz|Intake"`: 31/31 passed.
- Not exercised: the local-Ollama transport (`LocalAiService`, unchanged Core code) and the real proxy.
