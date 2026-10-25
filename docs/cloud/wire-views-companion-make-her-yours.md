- Stacked on `wire-views-companion-workshop-community-cell` (5e10cd18a): this branch adds only the Make Her Yours (Z4) part.
- Z4's Community link now runs WPF `BtnBrowsePrompts_Click` (`MainWindow.Patreon.cs:2252-2291`) through `WorkshopCommunityCell.BrowseAsync`. It shows the first five prompts with Install/Cancel, and installing never activates a prompt.
- One `CommunityPromptService` serves the room, as WPF `App.CommunityPrompts` does. Z4 uses the Workshop COMMUNITY cell's service and creates no second, undisposed instance.
- Both views refresh together, as WPF `UpdateCommunityPromptsUI` does (`MainWindow.CompanionTab.cs:184-210`). A Z4 install refreshes the Workshop rows. A Workshop Activate or Remove re-syncs Z4, so Reset turns off once the active prompt is removed.
- The Z4 readout shows the installed prompt's name, or its id when the file is missing.
- Ledger row `views-companion-make-her-yours` is now **wired**. The stale `MainShellWindow.CompanionTab.cs` header note is updated.

## Verification limits

- Tests: `MakeHerYoursCommunityTests`, which clicks the link from the shell's Companion tab, and `WorkshopCommunityCellTests`, which covers the Z4 re-sync and Reset turning off after Remove. Both were fail-proven on remote rcheck runs 39697099639 and 39697185669; the clean run 39697273212 passed. Details: `~/ccp-port/evidence/review-wire-views-companion-make-her-yours/fail-proofs.txt`.
- The Reset assertion is not fail-proven on its own: the break that tests it fails one line earlier.
- No Keincheck live run (P92). No side-by-side comparison with WPF, so the row is not verified.
