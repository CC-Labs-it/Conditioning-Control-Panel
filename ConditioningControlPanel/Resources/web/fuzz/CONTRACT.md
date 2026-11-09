# Terms & Conditioning: CONTRACT (v1 draft, 2026-11-02)

The wire between a host (WPF `ChaosWebViewHost`, the phone, the website shell) and this page. If code and
this file disagree, this file wins until it is amended here first. Shape and hygiene follow
`arcademy/bridge.js` and `backroom/CONTRACT.md` section 2: one flat JSON object per frame, kebab-case `type`.

## 0. Ground rules

- The page runs unhosted (plain browser, no `window.chrome.webview`) with every feature that needs no host:
  that is the free web build. Hosted, it gains pictures, Sparkles and panic.
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
| `haptic` | `level, ms, tag?` | Optional toy pulse on crits and the Drop; hosts clamp. |
| `open-url` | `url` | The player pressed a real link. The page only ever sends https on `cclabs.app`, `www.cclabs.app` or `app.cclabs.app` (any path) or exactly `https://discord.gg/YxVAMt4qaZ` (`ui/links.js`), from inside a press; the page is untrusted, so the host checks the same rule and opens it in the system browser (the website: a new tab, `noopener`). Unhosted the page opens a new tab itself. |
| `tab` | `title: string or null, icon?: 'sheet' or null` | A framed page cannot rename the browser tab: the Disguised Tab and the away titles ask the host to. `title` null = the host's own title; `icon` omitted = unchanged, null = the host's own icon, else a NAME from the host's fixed set (only `sheet`), never a URL. The host trims the title to 80 plain characters. The desktop host may ignore it (its window title is its own). |

## 2. Host -> page

| type | fields | meaning |
|---|---|---|
| `init` | `protocol, lang, reduced, motion, gentle, caps, mod` | Boot state. Protocol mismatch is fatal (`boot-error`). |
| `scrolller-feed` | `v:1, rows:[{url, kind:'image' or 'video', mime?}]` | Up to 36 approved rows. A host can set `building` while it fetches more. Device-origin videos require the clips capability. |
| `sparkles-receipt` | `credited, lines, stamp, sp, gentle` | Handed to `sparkles-slip.js` unchanged. |
| `suspend` | `on, reason:'panic'\|'focus'\|'arcade'` | Level. On: stop audio and fx now, keep state. A `focus` suspend still pays per-second income as away time (ui/away.js); `panic` and `arcade` pay nothing, and panic wins over focus. |
| `close` | `reason` | Answer `exit-done` within 300 ms; the host force-closes at 800 ms. |
| `kept-escape` | | Escape the host kept: `host-escape.js` replays it as one keydown (pause card, never close). |
| `setting` | `key, value` | A setting changed while open (`gentle`, `motion`, `media.remoteConsent`). |

### 3.1 `init.caps`

```
caps: {
  scrolller: { consent: bool, origin?: 'device', mediaOrigin?: 'https://ccp.assets', clips?: bool, sessionOptIn?: bool },
  sparkles: bool,              // host prices the Drop
  words: { bank: 'circe' | 'host', urls?: { [slug]: url } },  // a mod may hand another recorded bank; never TTS
  toys: bool,
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
- `init`: `{protocol:1, lang, reduced, motion, gentle, mod, caps:{platform:'web', scrolller:{consent, clips:true}, plan?:'free'|'basic'|'prime'}, identity:{displayName}}`.
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

## 4. Safety

- Wake Up (button top right) = pause + master mute + the pause card. Escape does the same via `kept-escape`.
  Nothing on this page is ever more permissive than panic: `suspend {on:true}` and `close` win over everything.
- Gentle (`init.gentle` or OS reduced motion): still spiral, no sway, no tunnel warp, Flash off, subliminals
  and trigger words fade over 250 ms or longer. Nothing anywhere flickers over 3 Hz.
- Master mute is honoured by every bus; BLANK and the Drop mute through the same gain.

## 5. Save

One localStorage key `fuzz.save.v2` (`sim/save.js`): `{v, run, life, extras}`, written every 5 s, on each buy and on
leaving. `life` outlives a run; New run writes `run: null`. A v1 save reads as no save. Hosted, the save is still local:
it is not synced and never becomes SP. Export and import appear on the pause card after a first ending.

Every play visit opens the picture chooser before gameplay. Active feeds may report an integer `usedPercent` for the account's daily meter in Wake Up, plus `upgrade:true` when a bigger daily share exists for the account and `upgradeX` (integer, floor of the live Prime share over the plan's normal share; omitted for Prime or without live figures; the Prime line names it when 2 or more); on the web host the reading also shows as a ring beside the account chip (`ui/usage-ring.js`, Prime line from 80%), and a spent share (100) arrives with `rows:[]`. Only a player's Scrolller selection asks the authenticated
host for metered pictures. Hosts using per-visit consent advertise `sessionOptIn:true` and accept `scrolller-release` to
release this visit when its player switches away or cancels. A negative `setting` reply may include a player-facing reason
code. Local files stay on device for the visit and incur no online usage. A separate choice keeps the frames empty.
Opted-in hosts may advertise `playWarmup:true`. The page sends `scrolller-play` with `active` on gameplay and visibility transitions, and `scrolller-view` when a foreground player shows a new post. The host can warm its 36-item pool before picture features unlock. Normal replacements keep the same `poolId`; a new ID or revocation invalidates the existing media. The browser respects each row's `expiresAt`, in epoch milliseconds.

Picture screens, theme art, arcade prizes and photo effects show the selected source and its credit. Of a theme's three
starting screens one is a motion screen (a gif or a clip, still when the source has none). Revoking consent clears
the media. The slideshow and Swipe Feed deal mostly animated GIFs and muted videos, with occasional stills. An expanding feed keeps current playback and active prizes. Each motion round deals previously unseen items first. Paper prints use standalone photos. GIFs, videos and their posters are excluded from the photo pool. Prize settlement is independent of availability.
