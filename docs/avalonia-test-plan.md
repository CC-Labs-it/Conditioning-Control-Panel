# Avalonia manual parity checks

1. WPF `MainWindow/MainWindow.Login.cs:OpenUnifiedLoginDialog` — sign in, then open Settings and gated tabs; log out and check the signed-out state. Rows: `shell-login`
2. WPF `Services/Settings/ProfileSyncService.cs:SyncProfileAsync` — toggle each privacy consent independently; inspect a sandbox sync request and confirm all six booleans, including explicit false revocation. Signed-in avatar consent must not imply public avatar consent. Rows: `shell-login`
3. WPF `Services/Settings/ProfileSyncService.cs:2193` — restore companion progress from a fake sync: higher level wins; equal level uses lifetime XP; a lower record cannot replace local. First push omits companion/time fields until read; logout disarms them. Rows: `shell-login`
4. WPF `Services/Progression/ProgressionService.cs:AnnounceFirstWebXpClaim` — deliver a new web XP claim in a sandbox: one passive success toast with the reference copy lasts nine seconds; another claim and a restart do not repeat it. Rows: `shell-login`
5. WPF `MainWindow/MainWindow.Patreon.cs:BtnLinkDiscord_Click` — link Discord from an existing account: dismiss link confirmation, then answer the separate achievement-sharing question. No, close, or a failed link must not enable sharing. Game/hidden-panel deferral uses the existing Inbox row. Rows: `shell-login`
6. WPF `Services/Settings/ProfileSyncService.cs:2175` — lift conditioning time from another device: total takes higher and the feature day log rebaselines so the gain is not booked as today's activity. Rows: `shell-login`


### FIN5 shell login

1. WPF `MainWindow.Login.cs:163-204`, `ProfileSyncService.cs:2103-2124`: sign in with cloud achievements. After the 500 ms settle, cloud unlocks appear without achievement popups or Discord posts; existing unlocks survive, including patron unlocks and server ids unknown to this version. Restart and confirm restoration persisted. Rows: `shell-login`
2. WPF `MainWindow.Login.cs:249-278`, `QuestService.EnsureOwnedBy`: advance an active quest, log out and back into the same account, and confirm its progress survives. Sign into another account and confirm the previous board is cleared before cloud restoration and the new board is generated afterwards. Rows: `shell-login`
3. WPF `MainWindow.Login.cs:163-204`: hold a profile response in flight, log out, then release it; the departed account's XP/achievements must not return. Automated fake-handler coverage also checks suppression is released. Rows: `shell-login`
