# companion-core-b: five pure companion rules move to CCP.Core

Unit B of the `ConditioningControlPanel/Services/Companion` move. This unit stacks on `avalonia-port/leash-gate-card`.

## Already in CCP.Core/Services/Companion before this unit
`ActivePersonaPromptText`, `AiEffectControlGate`, `CompanionContentResolver`, `CompanionExperience`,
`CompanionLinkIndex`, `CompanionPages`, `CompanionPerks`, `CompanionTitleMatcher`, `ConversationDelivery`,
`ConversationRelationship`, `ModCompanionContent`, `ModPersonalityPicks`, `PersonalitySamples`,
`PersonalityService`, `ThinkingPhrases`, plus `Asks/*` (4 files) and `Brain/*` (13 files).

## What changed
- `git mv` from `ConditioningControlPanel/Services/Companion` to `CCP.Core/Services/Companion`, unchanged
  (same namespace; Core's `InternalsVisibleTo` already covers WPF, so the `internal` ones still resolve):
  - `ConversationLinks.cs`: tidies dead `[Title]` / `[PLAY THE VIDEO]` brackets in v2 conversation replies.
  - `DrainSaveThrottle.cs`: limits Brain Parasite drain saves to one every 30 s (#1311).
  - `ModAvatarLooks.cs`: remembers the look per mod. It is the twin of `ModPersonalityPicks`, which is
    already in Core, and its `<see cref>` resolves inside Core now.
  - `SpeechLatchRule.cs`: the hover-hold cap and the stale-latch rule for the tube speech latch.
  - `CompanionPreview.cs`: the Customise summary of a mod that is not in use. It only needs `ModManifest`,
    `PersonalityPresets` and `PersonalitySamples`, which are all in Core already.
- None of these five had a Core or Avalonia copy, so no rule exists twice. None of them used `App.*`, so no
  new seams. No embedded resources were added, so the core-guards baseline is unchanged.
- Tests:
  - `git mv` to `Tests/CCP.Core.Tests`: `DrainSaveThrottleTests`, `ModAvatarLooksTests` and
    `SpeechLatchRuleTests`.
  - The pure `ConversationLinks.Tidy` cases moved to `CCP.Core.Tests/ConversationLinksTidyTests.cs`. The
    `ConversationPageVm` line and speak tests stay in WPF `ConversationLinksTests`.
  - The four `CompanionPreview` cases moved from WPF `CustomiseWindowRulesTests` to
    `CCP.Core.Tests/CompanionPreviewTests.cs`.
- No WPF source-scan test references these paths, so no `SourceRoots` change is needed.
- No trunk commits are imported: `parity/trunk` still keeps all five files in WPF.

## Next units (Services/Companion still in WPF)
- **Awareness trio**: `Brain/AwarenessRouting`, `Brain/AwarenessSpeech` and `Brain/ReactionArbiter`. They
  need the `Services/Awareness` family first. Trunk did this in `c0b077945` and `c8f4128fc`.
- **CompanionActivities**: needs a launcher-catalogue seam and a main-window open seam (`App.MainWindowRef`).
- **PersonalityStudio.Draft**: the pure detached-copy rule. The WPF dialog stays in WPF.
- **CompanionService**: XP, drain and level-up. It is `App.*`-heavy and already calls `DrainSaveThrottle` in Core.
- **CommunityPromptService**: trunk `a9f2564cd` and `1c0cd8325`.
- **CompanionPhraseService** and **BarkService**: large and `App.*`-heavy. Trunk `abc82671e` has the barks.
- **AvatarPortraitLoader**: WPF imaging, a head concern. It stays per head.
- Avalonia consumers for the moved rules (conversation link tidy, speech latch, per-mod look). Trunk
  `057f92924` has `CompanionLinkSegments`.

## Verification limits
- `bash .github/scripts/core-guards.sh`: "All Core guards passed."
- `dotnet test Tests/CCP.Core.Tests`: 1258 passed, 5 skipped, 2 failed. Both failures also fail on the
  untouched base in this container, so they come from the environment:
  - `LoopbackOAuthTests ... host: "[::1]"`: the container has no IPv6 loopback.
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...`: runs as root, so chmod does not block
    the delete.
- Moved and split tests, filtered (`DrainSaveThrottle|ModAvatarLooks|SpeechLatchRule|ConversationLinksTidy|CompanionPreview`):
  33 passed, 0 failed.
- `dotnet build` with 0 errors: CCP.Core (via its tests), CCP.Avalonia, CCP.LinuxSmoke, CCP.VR, and the
  Linux test projects from build.yml (Core.Settings, Core.Content, Avalonia.Consent, Language,
  CatalogueStartup, Scheduler, Report, GeneralSettings, Snapshot) plus `Tests/CCP.Avalonia.Tests`.
- `Tests/ConditioningControlPanel.Tests` built with `-p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false` (0 errors). That also compiles the WPF app against
  the moved files. It was compiled but NOT run: WPF tests need Windows.
- The .NET 10 SDK came from the Ubuntu apt feed (10.0.112), because the network policy blocks
  `builds.dotnet.microsoft.com`.
