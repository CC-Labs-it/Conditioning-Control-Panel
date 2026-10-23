- Wire the Profile tab link notice to the existing privacy-panel account flow, refresh it on tab/account changes, and disable its CTA offline.
- Offer achievement sharing after a successful Discord link or unified sign-in, with WPF prompt copy and shell ownership. Game, quiet, hidden and minimized states park one Inbox row; opening it explicitly asks. Yes saves and repaints; No, dismissal and panic do not opt in.
- Restore profile-card Discord actions: own card uses local DM consent plus the Discord id; board cards use the server-supplied id. Clear stale targets, open the Discord profile through ExternalOpener, and fall back to copying the id with 1500 ms feedback.
- Register the new achievement-sharing prompt with PanicSurfaces and add focused headless tests for notice state/clicks, offline gating, card transitions, launch/copy feedback, Ask/Inbox routing, acceptance and panic.
- Preserve the base's privacy migration seed and account-owned consent rules (decision 2026-10-22, option C).
- Keep one `views-profile-privacy-panel` ledger row, honestly `stub` pending the shared startup ladder's launcher-hold input.
- Source: WPF reference at `3a36f762eda10223f2a54b7cd8c6815c8ecdc325`, especially Views/Controls/ProfilePrivacyPanel.xaml(.cs), MainWindow.Browser.cs (login/status, card DM action and clipboard fallback), MainWindow.Patreon.cs (privacy switches and sharing offer), and MainWindow.Login.cs (post-login offer).
- Trunk credit: `git fetch origin parity/trunk` read `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no new trunk code imported. Existing panel attribution to `4f739180ca68a1e45f62c70a78d2bfbd0f1afcc0` is retained. Base files win.

## WPF gap inventory

The panel forwards its handlers into the WPF shell. Presence gating, immediate level updates, three local sharing switches, six audience switches, saved-state painting and the modal account flow already existed on the base. The migration seed resolved the row's outdated pending privacy-policy decision; it intentionally does not grant a fresh installation ownership of earlier server choices.

Remaining gaps found before implementation:

1. Profile-tab link notice never became visible and its CTA was empty; it needed status refresh and offline gating.
2. Successful Discord links and unified sign-ins did not offer achievement sharing. WPF skips an existing opt-in, asks with the panel as owner, parks while a game/launcher/hidden panel owns the screen, deduplicates Inbox rows, re-asks when explicitly opened, and saves/repaints only on Yes.
3. Own-card DM button never reflected consent or Discord identity; the click handler was empty. WPF board cards also carry a Discord id. Launch failure copies the id and paints localized feedback for 1500 ms.

This change implements those reachable paths. The shared startup modal/launcher ladder's explicit hold signal remains unavailable on this head (Platform/StartupLadder.cs documents it; ledger `shell-inbox`). Game ownership, shell visibility/minimization and the existing quiet route protect the current head. This remaining dependency prevents claiming complete WPF coverage.

## Verification limits

- Initial checkout was clean at `41d34136d3e3d4225ad48da419d3943184da8fbe` on local `work`. Required fetch/reset succeeded (exit 0), and HEAD was verified as `3a36f762eda10223f2a54b7cd8c6815c8ecdc325`. Created only the requested implementation branch.
- Installed .NET 10; `dotnet build CCP.Core/CCP.Core.csproj -c Release` passed, exit 0, tail: 48 warnings, 0 errors.
- `dotnet build Tests/CCP.Avalonia.Tests -c Release --no-restore` passed, exit 0, tail: 224 warnings, 0 errors. This compiled the final production implementation and new tests, including Windows-specific source; Windows execution was not run.
- The workflow build sweep recorded exit 0 for CCP.Core, CCP.Avalonia, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests and CCP.Avalonia.Language.Tests.
- CCP.Avalonia.CatalogueStartup.Tests initially returned exit 1: MSB4166 (MSBuild child worker exited prematurely). A retry and the rest of the sweep could not be completed after the cloud executor disconnected.
- With `DiffEngine_Disabled=true`, the relevant headless filter (ProfilePrivacyPanelTests, AccountSettingsSectionTests, DiscordRichPresenceTests, PanicSurfacesTests) started but produced no completed result before executor loss. A bounded existing-test probe could not start because the transport disconnected. No runtime pass, font diagnosis or red/green proof is claimed.
- The initial pre-fix regression attempt failed to compile a fixture model name; that fixture was corrected before the successful test-project build. This is not behavioral failure evidence.
- NOT completed: Core test suite; core-guards; remaining workflow test builds, including the conditional WPF build; headless assertions; render-all. No live OAuth, proxy, desktop, clipboard or Windows run.
- The managed executor became unavailable and subsequent shell calls returned: "No usable execution environment is available." The compiled edits were reconstructed against the pinned base using the GitHub connector so the authorized branch could preserve the work. The ledger/draft are documentation-only additions after the successful compilation.
- No PR was opened. No other branch was published or changed.

## Assumptions

- Preserve the base's modal-before-sign-in behavior and its approved account-owned privacy policy.
- Use the existing quiet route in addition to game ownership and hidden/minimized shell checks until the shared startup modal/launcher ladder is ported.
- Use English Yes/No labels, following existing Avalonia prompts where WPF gets those words from the OS and the English catalogue has no generic Yes/No keys.
- Leave the new Inbox row glyph empty; keep the existing Fluent chat icon on the DM action.
- Refresh local DM consent only for the own card; a different user's server-provided DM id must not inherit the local user's switch.

BLOCKED: Cloud executor unavailable; required verification incomplete. Full launcher-hold parity depends on the unported shared startup modal/launcher ladder.
