# wire-views-tab-remote-control

Ledger row `views-tab-remote-control`: stub -> wired. Base `avalonia-port/quiz-core-a`.

- **Directory opt-in chain** (WPF `MainWindow.RemoteControl.cs:515-738`; ported from parity/trunk `61daf0358`):
  - The form fills from saved settings.
  - Tags have a soft cap of 5. A 6th tick is undone and shows `msg_optin_directory_max_tags` for 2.5 s.
  - The status box has an `n/80` counter.
  - `RunOptInChainAsync` runs after the session starts. It calls Core `RemoteRelay.OptInToDirectoryAsync` (POST `/v2/directory/opt-in` with code, PIN, tags and `status_text`). A failure shows `msg_optin_directory_failed` for 4 s and leaves the session running.
  - "Remember" saves the details, or clears them, only after a successful listing.
  - The listed banner (`ListedConfirmationPanel`) shows once the listing succeeds.
- **Core `RemoteRelay`:** adds `OptInToDirectoryAsync` and `DirectoryOptedIn`. It re-publishes the listing when a controller leaves (disconnect and the 120 s idle auto-disconnect), and forgets it when the session ends.
- **Title-bar directory pill** (`MainShellWindow.DirectoryPill.cs`; after parity/trunk `d7a46b6e4`):
  - States as in WPF: hidden, Private only, Listed, Claimed.
  - Texts are WPF's own literals, because WPF has no loc keys for them.
  - The tab refreshes the pill when the session starts or ends, when the listing succeeds, and when a controller connects or leaves.
- **Share media sources toggle** (`ChkRemoteShareMediaSources`, WPF `RemoteControlTabView.xaml.cs:122`): saves the setting, then pushes status at once. The status push sends nothing for it yet: the screen-feed preview that uses it belongs to row `shell-remote-control`.
- **Tube emote bubble** (`AvatarTubeWindow.ShowEmoteFeedback`, WPF `AvatarTubeWindow.ChatInput.cs:957`):
  - Shows "Sending..." while pending, then `Sent: "<40 chars>..."`.
  - Never covers an AI bubble or a pending reply.
  - Plays no sound and stays up 1 s longer than a preset line.
  - Fired from `SendEmoteAndReportAsync`, so both the tab picker and the shell's big picker use it.
- **Hero art:** `HeroArtPlate` paints `features/remote_control.png` through `ModArt` (a mod's copy first) and repaints on `CoreMods.ModChanged`.
  - The side-art column stays out on purpose: WPF's `AdaptiveSideArt` hides it at this head's width, and that behaviour is row `feat-adaptive-side-art`.
- **Waiver:** now the WPF 7.1.5 text. No tier lists strict lock, and it says "Your panic key always works. A controller cannot switch it off or turn Strict Lock on." (as parity/trunk `61daf0358`).
- **Panic:** nothing new needs closing. WPF's panic does not touch the listing, and the feedback line is a label on the tab.
- Added lines vs base: 722 (cap 1000).

## Verification limits

- `dotnet build`: CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, Tests/CCP.Avalonia.Tests and every Linux test csproj in build.yml. 0 errors.
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 2781 passed, 6 skipped, 2 failed. The 2 failures also fail on the untouched base in this container:
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...`: root can delete the folder.
  - `LoopbackOAuthTests ... [::1]`: the container has no IPv6 loopback.
  - The new `RemoteDirectoryOptInTests` (4) pass.
- `dotnet test Tests/CCP.Avalonia.Tests` filtered to Tube, Remote, Header, Panic and Emote: 118 passed, 5 skipped, 0 failed. This includes the new `RemoteDirectoryTabTests` (4), `RemoteControlTabTests` and `RemoteControlShellTests`.
- Fail-proven by mutation:
  - Dropping the pill refresh on a controller change fails `Title_bar_pill_...`.
  - Dropping the AI-bubble guard fails `Tube_bubble_...`.
- `dotnet run --project CCP.Avalonia -- --render-all`: 221/221 views rendered. `RemoteControlTabView.png` shows the hero art plate.
- Not run live: a real directory listing or claim. The sandbox has no unified id, so only a fake relay was used.
- No Windows-only code was added.
