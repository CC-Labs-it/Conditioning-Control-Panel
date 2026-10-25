- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; the initial snapshot differed, and its read-only Git metadata required the authorized independent clone fallback.
- Added separate Companion Personality, Permissions and Links page hosts, reached through the real section pills and existing conversation Settings redirect. They borrow the existing presets, permissions, community and video-library controls and return them when hidden; no duplicate state or settings models.
- Personality includes the existing companion/perk picker and Advanced prompt-editor door. Permissions links to Takeover. Reused AiPage's borrowing implementation.
- Added the WPF portrait dust layer with its exact layer, intensity, density and tint. Reused AmbientFxCanvas's shared clock and effective-visibility/motion gates; added conversation cancellation and dust parking to the panic registry.
- Replaced the duplicated historical ledger text with one honest stub row and numbered manual tester steps. Updated the existing navigation test to assert live control identity, page visibility and restoration rather than the superseded room-scroll behavior. Added a focused portrait-layer test and panic registration coverage.
- Reference: frozen WPF files at the base SHA above: `Views/Controls/Companion/V2/ConversationPage.xaml(.cs)`, `ConversationPageVm.cs`, `PreferredNameEditor.cs`, `ConversationRecap.cs`, `PerkPicker.cs`; `Views/Controls/Companion/Pages/{Personality,Permissions,Links}Page.xaml(.cs)` and `MainWindow/MainWindow.CompanionTabs.cs`.
- Trunk was fetched read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code was imported. The adaptations use the base's WPF reference and existing Avalonia controls.

## Verification limits

- .NET SDK 10.0.402. Initial NuGet requests repeatedly returned HTTP 502, then restore succeeded.
- Core guards: passed. `git diff --check`: passed. The base has no `test-plan-coverage.sh`; its attempted invocation exits 127. Numbered WPF/Rows steps were added manually.
- Core and shipped Avalonia head compile with zero errors. Main Avalonia test assembly compiles with zero errors.
- Targeted `ConversationPageTests`, `CompanionZonePillsTests`, `PanicSurfacesTests`: 6 passed, 3 failed, 0 skipped. The failures are font creation in shell construction (the documented environment limitation), including an existing panic route test. The new portrait-layer test passes. No desktop or real-profile test was run.
- Portrait regression red/green proof: temporarily removing the portrait layer/configuration fails `PortraitHasWpfDustLayer` with `Assert.NotNull` (exit 1); restoring the port passes (1 passed, exit 0). The earlier overlapping-restore attempt was discarded as evidence. Host navigation behavior and rendering still need supervisor validation with working fonts; no red/green proof for the font-blocked host test, render-all proof or parity promotion is claimed.
- Initial isolated-project builds exhausted disk space while copying runtime assets; generated completed test outputs were removed and builds retried sequentially. Final build and Core test results follow below.

- Final Linux builds: Core, Avalonia, and all twelve Linux workflow test projects compile with zero errors (exit 0). Isolated test outputs were cleaned between builds to stay within disk capacity.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore` with `DiffEngine_Disabled=true`: exit 0; 3,953 passed, 5 skipped (Vosk model unavailable), 0 failed.
- Focused Avalonia command: `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-restore --filter 'FullyQualifiedName~ConversationPageTests|FullyQualifiedName~CompanionZonePillsTests|FullyQualifiedName~PanicSurfacesTests'` with `DiffEngine_Disabled=true`: exit 1; 6 passed, 3 font failures, 0 skipped. Separately rerun portrait test: exit 0, 1 passed.
- Build commands: `dotnet build CCP.Core -c Release`, `dotnet build CCP.Avalonia -c Release`, and `dotnet build <each test csproj listed in .github/workflows/build.yml> -c Release`. The WPF test build additionally uses `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
- Windows-targeted app/test compilation: exit 0, zero errors using the required targeting flags. Windows behavior was compiled but not run on Linux. The first attempt ran out of disk during output copying; removing completed Avalonia outputs allowed the retry to finish.

## Gap inventory and remaining work

Before this change the row lacked separate Personality/Permissions/Links pages; live adopted presets, community, permission and video hosts; the Personality picker/Advanced door; Takeover cross-links; shared KnowledgeLinksEditor; collapsed-room layout with tab-level visibility/pill handling; cell-to-page redirects; and portrait dust. Header button order, conversation interaction, PreferredNameEditor, recap and the perk picker were already present despite historical absence claims in the row.

Still missing: the shared KnowledgeLinksEditor on Links and in the prompt dialog; full V2 collapsed-room layout and its tab-level visibility / pill-unhide behavior; Takeover-to-Permissions navigation; cell-to-page redirects; the PersonalityStudio callback (the existing prompt editor is used instead). The preview-only fallback settings sheets (including asks/familiarity/allowance) remain absent. The row stays `stub`; this is a partial implementation, not a completed parity claim.

## Assumptions

- Keep the legacy room accessible until every remaining zone destination is ported; collapsing it now would remove working user paths. Borrowed controls therefore return on hide, matching the existing AiPage convention.
- Use the existing prompt editor for More personality options while PersonalityStudio is unported; Advanced uses the same existing gated editor path.
- No new independent windows were added; the new pages live in the existing shell. The new animated conversation surface has explicit panic registration.
