# Terms & Conditioning: CONTRACT (v1 draft, 2026-11-02)

The wire between a host (WPF `ChaosWebViewHost`, the phone, the website shell) and this page. If code and
this file disagree, this file wins until it is amended here first. Shape and hygiene follow
`arcademy/bridge.js` and `backroom/CONTRACT.md` section 2: one flat JSON object per frame, kebab-case `type`.

## 0. Ground rules

- The page runs unhosted (plain browser, no `window.chrome.webview`) with every feature that needs no host:
  that is the free web build. Hosted, it gains pictures, Sparkles and panic.
- One tree, two editions (section 6): a page with no host, or a host that does not say, is the small edition.
- The page never sees UnifiedId or AuthToken. It reports how the run ended, never an amount.
- Law VI: leaving is instant. Nothing waits on the network.
- Every effect the page draws asks Gentle first (section 4). No synthetic speech anywhere.

## 1. Page -> host

| type | fields | meaning |
|---|---|---|
| `ready` | `protocol:1` | Page booted. Host answers `init`. Allowed before init. |
| `log` | `level, msg` | To Serilog, tag `Fuzz`. Allowed before init. |
| `heartbeat` | | Every 5 s. Allowed before init. |
| `boot-error` | `msg` | The page could not start. Allowed before init. |
| `exit` | `reason:'back'\|'key'\|'drop'` | Player left. Host closes after `exit-done`. Allowed before init. |
| `exit-done` | | Page settled: audio released, save flushed. |
| `fuzz-ended` | `total, minutes, items, ending, route, dayUtc` | An ending happened (`ending` is its id, `route` the lanes picked). The host prices it (`SparklesClaim result.kind:'medal'`, one a day) and answers `sparkles-receipt`. |
| `scrolller-niches` | `niches:[id]` | Ask for pictures for the feed panels (same wire as Glassroad CONTRACT 292-325). Ids are the eight theme niches `trance bimbo drone sissy goon chastity censored infection` (the Locked theme is sent as `chastity`): `[]` before Pick a Theme (the host's general hypno mix), `[theme]` after it, `[theme, second]` with a Second Theme. Sent again whenever that list changes (`fork`, `taste2`, a switch); a host serves only those niches' subs, an even mix for two, and treats an unknown id as the general mix. Sub index: handoff-1108/tc-scrolller-niches/index.json (cclabs-web `src/lib/tc-niches.ts`). |
| `set-setting` | `key:'media.remoteConsent', value, ask:true` | Only from the player's own press on the consent card. |
| `set-setting` | `key:'window.fullscreen', value: bool` | Section 3.3. The Options view's Full screen row, only to a host that sent `caps.window.fullscreen`. The host answers `setting` with what the window now is. |
| `account` | | Section 3.3. The player pressed Account in Options: the host shows its own account page. Only to a host that sent `caps.account`. |
| `own-media-list` | | Section 3.3. Once at start, only to a host that sent `caps.ownMedia.folder` (the files of the folder it remembers) or `caps.ownMedia.library` (the pictures the app itself uses). Answered by `own-media`. |
| `own-media-pick` | | Section 3.3. The player pressed the folder button: the host opens its own folder window. Answered by `own-media` with `asked:true`. |
| `haptic` | `level, ms, tag?` | Optional toy pulse on crits and the Drop; hosts clamp. |
| `open-url` | `url` | The player pressed a real link. The page only ever sends https on `cclabs.app`, `www.cclabs.app` or `app.cclabs.app` (any path) or exactly `https://discord.gg/YxVAMt4qaZ`, `https://neal.fun/` or `https://neal.fun/stimulation-clicker/` (the credit, `ui/links.js`), from inside a press; the page is untrusted, so the host checks the same rule and opens it in the system browser (the website: a new tab, `noopener`). Unhosted the page opens a new tab itself. |
| `tab` | `title: string or null, icon?: 'sheet' or null` | A framed page cannot rename the browser tab: the Disguised Tab and the away titles ask the host to. `title` null = the host's own title; `icon` omitted = unchanged, null = the host's own icon, else a NAME from the host's fixed set (only `sheet`), never a URL. The host trims the title to 80 plain characters. The desktop host may ignore it (its window title is its own). |
| `achievement` | `id` | A sticker (the game's achievement, `data/content/stickers.js` id) went in this save's Sticker Book for the FIRST time, earned while playing with the Achievements row owned. Additive and optional: a host that ignores it changes nothing, and the page never waits for an answer. Never sent for the backlog the row finds when it is bought, nor on a reload or a loaded save (those go in the book quietly), nor again for a sticker the save already holds, so a host may reward each id once per save. Hidden stickers are only ever sent once earned. Pricing, if any, is the host's (no amount on the wire). |

## 2. Host -> page

| type | fields | meaning |
|---|---|---|
| `init` | `protocol, lang, reduced, motion, gentle, caps, mod` | Boot state. Protocol mismatch is fatal (`boot-error`). |
| `scrolller-feed` | `v:1, rows:[{url, kind:'image' or 'video', mime?}]` | Up to 36 approved rows. A host can set `building` while it fetches more. Device-origin videos require the clips capability. |
| `sparkles-receipt` | `credited, lines, stamp, sp, gentle` | Handed to `sparkles-slip.js` unchanged. |
| `suspend` | `on, reason:'panic'\|'focus'\|'arcade'` | Level. On: stop audio and fx now, keep state. A `focus` suspend still pays per-second income as away time (ui/away.js); `panic` and `arcade` pay nothing, and panic wins over focus. |
| `close` | `reason` | Answer `exit-done` within 300 ms; the host force-closes at 800 ms. |
| `kept-escape` | | Escape the host kept: `host-escape.js` replays it as one keydown (pause card, never close). |
| `setting` | `key, value` | A setting changed while open (`gentle`, `motion`, `media.remoteConsent`, `window.fullscreen`). |
| `own-media` | `v:1, path, rows:[{url, name, type, size, mtime, kind?}], asked?: bool, reason?` | Section 3.3. The player's own folder, or the app's own library, served by the host. |

### 3.1 `init.caps`

```
caps: {
  scrolller: { consent: bool, origin?: 'device', mediaOrigin?: 'https://ccp.assets', clips?: bool, sessionOptIn?: bool },
  sparkles: bool,              // host prices the Drop
  words: { bank: 'circe' | 'host', urls?: { [slug]: url } },  // a mod may hand another recorded bank; never TTS
  toys: bool,
  toysConnected: bool,         // true only from the CCP desktop app with a toy connected at run start: offers Toy Link + Phone Buzz (ui/platform.js)
  edition: 'small' | 'full',   // section 6.1; missing or unknown = small
  platform: 'web' | 'desktop', // missing = web
  saveSlot: bool,              // section 6.3; the host keeps one save string for this account
  expo: { state: 'ready' | 'absent', base?: url, mb?: int },  // section 6.5; desktop and full only
  window: { fullscreen: bool },  // section 3.3; the host's window can go full screen, and is or is not right now
  account: bool,               // section 3.3; the host has an account page of its own (sign in, sign out)
  ownMedia: { folder: bool, path?: string, library?: bool, mediaOrigin?: url },  // section 3.3; the host keeps and serves the player's own folder, or (library) the app's own pictures
  dev: bool,                   // the hidden dev console (ui/preview-bar-model.js): only a host that prices nothing sends it (the standalone program); never the website, never the CCP app
  unsealed: bool,              // section 6.1; the host's small edition plays now, never the sealed notice (the standalone program); never the CCP app
}
mod: 'default' | 'bambi' | 'sissy' | 'locked' | 'drone'
```

`words.bank:'host'` with `urls` replaces the mantra and Voice clips by slug; a slug with no url stays silent.
CCP Default, Locked and every web/phone build use the Circe clips in `assets/audio/words`.

### 3.2 Web host (app.cclabs.app/tc)

The site frames `https://tc.cclabs.app/fuzz/?host=web` full viewport (`allow="autoplay; fullscreen"`). Same frames as
sections 1 and 2; only the transport differs (`bridge.js`, transport `web`):

- The page is web-hosted only when it is framed, the url has `host=web`, and the parent origin
  (`location.ancestorOrigins[0]`, else the `document.referrer` origin) is on `HOST_ORIGINS` in `bridge.js`
  (`https://app.cclabs.app`; a preview origin is one more line there and in `FRAME_ANCESTORS` of the stage script).
  A page served from 127.0.0.1 or localhost also accepts `http://localhost:3000` and `http://127.0.0.1:3000`.
  Anything else stays standalone. Firefox has no `ancestorOrigins`: the parent must not send `Referrer-Policy: no-referrer`
  to the frame (default policy, or `referrerpolicy="origin"` on the iframe).
- Page -> host: `parent.postMessage(frame, parentOrigin)`, never `'*'`. Host -> page: read only when
  `event.origin === parentOrigin && event.source === parent`; objects or JSON strings. The host posts to the game's origin.
- `caps.plan` (web host, the account's effective plan): `basic` and `prime` get no CCP ads (owner 2026-11-08); missing = free.
- No `init` within 4 s = standalone (`hosted:false`, empty caps), so a broken embed never hangs the game.
- `init`: `{protocol:1, lang, reduced, motion, gentle, mod, caps:{platform:'web', edition:'small'|'full', scrolller:{consent, clips:true}, plan?:'free'|'basic'|'prime'}, identity?:{displayName}}`.
  `edition` is `small` for a visitor without an account and for a free account, `full` for a signed-in subscriber
  (Basic or Prime). `identity` is absent for a visitor without an account. The website sends no `caps.expo`.
  No `mediaOrigin`, no `origin:'device'`: `scrolller-feed` rows are remote Scrolller CDN urls (`https://*.scrolller.com/...`),
  shown as plain `<img>`/`<video>` with "via Scrolller", never fetched, cached or saved by the page. About one row in three is
  a clip, `{url, kind:'video', mime:'video/mp4'}` (one mp4 rendition, short side at most 720, served from Scrolller's rotating
  CDN shards, so the stage CSP's media-src is `https://*.scrolller.com`); stills stay on `images.scrolller.com`. Remote rows need
  no `clips` (the page accepts a remote video row either way); the web host sends `clips:true` to say it may send them. A refused
  `set-setting` echo may carry `reason` (`sign-in`, `allowance-used`, `disabled`, `declined`); the picture card says it plainly.
- `exit` (the Leave button, hosted only) and `open-url` are the host's to act on. The save stays in this origin's
  localStorage, so a standalone player who signs in on the site keeps their run.
- Standalone, the picture card's first choice is "Sign in with your CC Labs account": a real press sends the same tab to
  `https://app.cclabs.app/tc`.

### 3.3 The window and the player's own folder (Options, owner 2026-11-23)

The pause card has an Options view (`ui/options.js`): Mute, Gentle visuals, Full screen, the pictures row, the folder
row and the Expo's camera options. Two of its rows need the host. A host that sends neither capability gets neither
row; the unframed page uses the browser's own full screen instead.

- **Account.** A host with `caps.account` gets a row naming who is signed in (`identity.displayName`) and an Account
  button; the press sends `account` and the host shows its own page. The game holds no token and signs nobody out.
- **Full screen.** `caps.window.fullscreen` is the window's state at start. The row sends `set-setting` with key
  `window.fullscreen`; the host changes the window and answers `setting` with the same key and the state the window
  really has. A host whose window changes by another way (a key, a menu) sends the same `setting`.
- **The folder.** A browser forgets the player's own files on reload. A host with `caps.ownMedia.folder` keeps a
  folder instead: it opens its own folder window on `own-media-pick`, remembers the pick, and serves the files from the
  page's own origin. `own-media` carries `path` (shown to the player as it is) and up to 3000 `rows`:
  `url` (same origin as the page, anything else is dropped), `name`, `type` (a MIME type), `size`, `mtime`, and
  `kind` (`picture`, `gif` or `clip`) when the host has read the file's header itself (the rule of
  `ui/local-image-motion.js`); without `kind` the page reads the header by range. `asked:true` marks the answer to
  a pick: only that one switches the source to the player's files. A pick the player closed answers `rows:[]`,
  `reason:'cancelled'`. The page asks `own-media-list` once at start for the remembered folder. Nothing is uploaded:
  the files never leave the device.
- **The library (CCP desktop, 2026-11-24).** A host with `caps.ownMedia.library` (and no `folder`) answers "Use local
  files" with the pictures the app itself uses: the CCP app sends the ticked images of its asset library (the disk half of
  the Flash Images pool, deselected files left out) and the ticked videos, each with `kind`, from `caps.ownMedia.mediaOrigin`
  (`https://ccp.assets`), the one other origin the page accepts rows from. The page asks `own-media-list` once at start
  and keeps the answer; nothing changes source until the player presses Use local files. An empty library answers
  `rows:[]` and the press opens the browser's own file picker, as without a host. There is no row in Options.

## 4. Safety

- Wake Up (button top right) = pause + master mute + the pause card. Escape does the same via `kept-escape`.
  Nothing on this page is ever more permissive than panic: `suspend {on:true}` and `close` win over everything.
- Gentle (`init.gentle` or OS reduced motion): still spiral, no sway, no tunnel warp, Flash off, subliminals
  and trigger words fade over 250 ms or longer. Nothing anywhere flickers over 3 Hz.
- Master mute is honoured by every bus; BLANK and the Drop mute through the same gain.

## 5. Save

One localStorage key `fuzz.save.v2` (`sim/save.js`): `{v, run, life, extras}`, written every 5 s, on each buy and on
leaving. `life` outlives a run; New run writes `run: null`. A v1 save reads as no save. Hosted, the save is still local:
it is not synced and never becomes SP. Export and import appear on the pause card after a first ending. The one way a
save leaves the device is the player's own carry (section 6.3).

Every play visit opens the picture chooser before gameplay. Active feeds may report an integer `usedPercent` for the account's daily meter in Wake Up, plus `upgrade:true` when a bigger daily share exists for the account and `upgradeX` (integer, floor of the live Prime share over the plan's normal share; omitted for Prime or without live figures; the Prime line names it when 2 or more); on the web host the reading also shows as a ring beside the account chip (`ui/usage-ring.js`, Prime line from 80%), and a spent share (100) arrives with `rows:[]`. Only a player's Scrolller selection asks the authenticated
host for metered pictures. Hosts using per-visit consent advertise `sessionOptIn:true` and accept `scrolller-release` to
release this visit when its player switches away or cancels. A negative `setting` reply may include a player-facing reason
code. Local files stay on device for the visit and incur no online usage. A separate choice keeps the frames empty.
Opted-in hosts may advertise `playWarmup:true`. The page sends `scrolller-play` with `active` on gameplay and visibility transitions, and `scrolller-view` when a foreground player shows a new post. The host can warm its 36-item pool before picture features unlock. Normal replacements keep the same `poolId`; a new ID or revocation invalidates the existing media. The browser respects each row's `expiresAt`, in epoch milliseconds.

Picture screens, theme art, arcade prizes and photo effects show the selected source and its credit. Of a theme's three
starting screens one is a motion screen (a gif or a clip, still when the source has none). Revoking consent clears
the media. The slideshow and Swipe Feed deal mostly animated GIFs and muted videos, with occasional stills. An expanding feed keeps current playback and active prizes. Each motion round deals previously unseen items first. Paper prints use standalone photos. GIFs, videos and their posters are excluded from the photo pool. Prize settlement is independent of availability.

## 6. Editions (v2, owner 2026-11-22)

One game tree, two editions, never a fork. `data/editions.js` holds the lists and the fixed ids; `ui/edition.js` is the
ONE reader of the host's choice. Plan and lanes: `handoff-1122/TC-EDITIONS-PLAN.md`.

| | small | full |
|---|---|---|
| Where | the website without an account, phones, free accounts everywhere | the CCP desktop app, the standalone and the website, for any subscriber |
| Rows | 260 of 316 (`SMALL_CUT` is never offered) | all |
| Renewal | none, for anyone: the ending leads to New run | Sky, Depth, Stardust |
| Llama Expo | never; the pen and the hunt stay | opens once its files are on the PC (6.5) |
| Phone | hard caps (`PHONE_CAPS`) | not a phone target |

### 6.1 Who decides

- `init.caps.edition`: `'small'` or `'full'`. Missing, unknown, or no host at all = small. The page is untrusted: a host
  says full only for an account it has checked (any subscriber, Basic or Prime). The website says small for a visitor
  without an account and for a free account, full for a signed-in subscriber; it sends no `caps.expo`, and no
  `identity` for a visitor without an account.
- A page on 127.0.0.1 or localhost with no host is the whole game, so dev drives and the Expo preview keep working;
  `?edition=small` or `?edition=full` wins over everything there and is ignored everywhere else.
- `init.caps.platform`: `'web'` or `'desktop'` (missing = web). It picks the nudge and the seal rule.
- The seal until 22 December (`ui/seal.js`, owner 2026-11-22): the website is never sealed (owner 2026-11-24). In the
  desktop app the edition decides: full (any subscriber) plays now, small (a free account) sees the sealed notice
  until the anniversary, worded for subscribers, with one button that sends `nudge {where:'locked-tile', want:'upgrade'}`.
  The host needs no date check. A sealed page still answers the host's `close` with `exit-done`.
- Signed in = the host sent an `identity` object at all; a fresh account with no display name is still signed in.

### 6.2 Nudges (page -> host)

`nudge {where, want}`, only from the player's own press. Two fixed ids, no free text, no url.

- `where`: `ending`, `llama8`, `transmission`, `locked-tile`, `pause`, `save-sleep`.
- `want`: `upgrade` on the desktop (the plan card; the app never says "desktop only"), `account` on the web with no
  `identity`, `download` on the web signed in. The host maps `want` to its own door.
- With no host the page opens the fixed link for that `want` (`NUDGE_LINKS`, all on the `open-url` list).

### 6.3 Save carry

A host that can keep one save string for the signed-in account sends `caps.saveSlot: true`; without it the buttons do
not show. Any signed-in account may use the slot. The server never reads meaning from the string and it never becomes
Sparkles.

| direction | type | fields | meaning |
|---|---|---|---|
| page -> host | `save-offer` | `save` | The whole save string, from the player's press. Never more than 400,000 characters (the save parser's own limit, so a slot never keeps a save the page cannot read back) and never more than 512,000 bytes. A real late-game save is about 27,000. |
| page -> host | `save-ask` | | "Is there a save for me?", from the player's press. |
| host -> page | `save-result` | `ok, reason?` | `reason`: `sign-in`, `too-big`, `offline`, `refused`, `none`. |
| host -> page | `save-incoming` | `save, savedAt, from` | Only as the answer to `save-ask`. `savedAt` epoch ms; `from`: `web` or `desktop`. |

`save-incoming` is never applied silently: the page shows a card with both dates, applies it only on accept, and first
keeps the local save as `fuzz.save.v2.bak`.

### 6.4 Sleep

Small never deletes. A save that owns cut rows or Renewal state loads in small with those asleep: not drawn, not
paying, not counted, not offered. The owned list and `life` are written back unchanged, so the same save opened in full
has everything. The first time, one card says so and offers the carry (`nudge where:'save-sleep'`).

In small a kept row never needs a cut one: a cut parent hands down its own parents (`reqFor` in `ui/edition.js`), and
`SMALL_REQ` pins a row by hand where the balance pass wants it.

### 6.5 The Expo and the transmission

The Expo's files are not in the installer and not on the website. On the desktop, in full, the host sends
`caps.expo: {state:'absent', mb}` until they are on disk, then `{state:'ready', base}`.

- At 4 llamas with the Expo absent, a card in fiction: a message from the future is transmitting data, accept the
  transmission? It states the size and where the files land in plain words. YES sends `expo-fetch`. NOT NOW is always
  there, and the pen sign offers it again. At 8 llamas with the Expo still absent, the wormhole shows the same card first.
- In small the same card is a nudge (`where:'transmission'`): the transmission does not fit here.

| direction | type | fields | meaning |
|---|---|---|---|
| page -> host | `expo-fetch` | | Start or resume the download. Only from the player's press. |
| page -> host | `expo-cancel` | | The player stopped it. The host keeps what it has. |
| host -> page | `expo-progress` | `pct` | Integer 0 to 100, at most two a second. |
| host -> page | `expo-ready` | `base` | On disk and checked. `base`: https, ends in `/`. |
| host -> page | `expo-failed` | `reason` | `offline`, `disk`, `refused`. The card says it plainly and offers again later. |

The Expo loads only when `expoAllowed()`: edition full and its files within reach (`expo.state:'ready'`, or a local
preview with the files in the tree). Every Expo file loads from `expoBase()` (default `assets/expo/`); nothing outside
`ui/expo/` imports or fetches an Expo file.
