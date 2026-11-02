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
| `scrolller-niches` | `niches:[id]` | Ask for pictures for the feed panels (same wire as Glassroad CONTRACT 292-325). |
| `set-setting` | `key:'media.remoteConsent', value, ask:true` | Only from the player's own press on the consent card. |
| `haptic` | `level, ms, tag?` | Optional toy pulse on crits and the Drop; hosts clamp. |

## 2. Host -> page

| type | fields | meaning |
|---|---|---|
| `init` | `protocol, lang, reduced, motion, gentle, caps, mod` | Boot state. Protocol mismatch is fatal (`boot-error`). |
| `scrolller-feed` | `v:1, rows:[{url, kind:'image'}]` | At most 12 rows; empty when consent is off. Desktop rows come from `https://ccp.assets`. |
| `sparkles-receipt` | `credited, lines, stamp, sp, gentle` | Handed to `sparkles-slip.js` unchanged. |
| `suspend` | `on, reason:'panic'\|'focus'\|'arcade'` | Level. On: stop audio and fx now, keep state. |
| `close` | `reason` | Answer `exit-done` within 300 ms; the host force-closes at 800 ms. |
| `kept-escape` | | Escape the host kept: `host-escape.js` replays it as one keydown (pause card, never close). |
| `setting` | `key, value` | A setting changed while open (`gentle`, `motion`, `media.remoteConsent`). |

### 3.1 `init.caps`

```
caps: {
  scrolller: { consent: bool, origin?: 'device', mediaOrigin?: 'https://ccp.assets' },
  sparkles: bool,              // host prices the Drop
  words: { bank: 'circe' | 'host', urls?: { [slug]: url } },  // a mod may hand another recorded bank; never TTS
  toys: bool,
}
mod: 'default' | 'bambi' | 'sissy' | 'locked' | 'drone'
```

`words.bank:'host'` with `urls` replaces the mantra and Voice clips by slug; a slug with no url stays silent.
CCP Default, Locked and every web/phone build use the Circe clips in `assets/audio/words`.

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

Pictures have one source (`ui/pictures.js`): `online` (signed in; `scrolller-*` frames, placeholder art until that wire
lands), `own` (the player's files, blob urls on the device, never uploaded), `none`.
