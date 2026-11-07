# Media slots (polish 3, MEDIA lane)

Where Scrolller media will show in the race, and the rules it will follow. This pass ships
**placeholders only**: animated procedural pictures, nothing downloaded, no Scrolller request of any kind.

Current web path (2026-10-26): the host feed also fills selected world screens through
`media.setDisplayFeed(feed, canvas)`. Scrolller pictures stay in display-only DOM elements behind
the game canvas. One instanced group of zero-alpha faces supplies normal GPU depth occlusion;
the renderer enables alpha and the post pass preserves premultiplied scene alpha. Full-screen
effects close these windows. No remote picture becomes a GPU texture, and no server copies it.
Pause freezes the seven-second picture changes; source-off, feed expiry and disposal clear them.
The origin-clean desktop clip path below remains independent.

Phone path (2026-10-31, `host/devicePictures.js`): the phone host serves cached stills from the page's own
loopback origin, so they are real textures on the same prewarmed owners as clips: `setSource` with a borrowed
canvas, then `swapStill` in place. One upload per picture, 640 px long edge, owners = `mediaClipLimit` (phone 3).

Files: `render/media.js` (three.js), `render/mediaMath.js` (three-free placement, fitting, budget, url
guard), `render/mediaPlaceholders.js` (the placeholder programs, three-free), `tracks/threshold.media.json`
(the layout), `tests/render-media.test.mjs`.

## The three frame kinds

| Kind | Where (lap fraction f) | What |
|---|---|---|
| Billboards | f 0.06, 0.2, 0.25, 0.38, 0.42, 0.52, 0.64, 0.76, 0.8, 0.92, alternating sides | portrait 9:16 or landscape 16:9 screens 8 to 14 m tall, 9 to 12 m off the road edge, on a glass stalk that fades into the void, face turned about 30 degrees from straight back down the road toward it |
| Sky halo | over the start/finish (f 0) | 12 landscape screens (8 on phone) on a 24 m ring, 22 m up, facing the axis and tipped 40 degrees down, orbiting at 0.05 rad/s |
| Screen tunnel | f 0.55 to 0.60 | a glass arch over the road (11 m high, road width plus 3.5 m each side) with a left wall, right wall and ceiling screen every 9 m (18 m on phone), turned 28 degrees toward oncoming karts, glowing ribs every half ring and lines along the glass |

The tunnel ends at f 0.60, not 0.61: the stretch at s about 515 m crosses over the exit and the roof would
pass 1.6 m under its deck. Every slot, stalk and the tunnel glass is fitted against every road stretch
(`tubeClash`: nothing within w/2 + 3 m across, 6 m below to 9 m above any surface); a billboard that clashes
steps out 4 m at a time. PROPS keeps scatter and colossi out of these volumes (the halo is a 28 m radius
disc 22 m over s = 0).

Every frame has: the picture, a soft glow border (1.6 m) in spectrum colours that cycles hue slowly, a dark
back plate, and the **credit strip** along the bottom 8.5 percent of the picture (dark glass, empty until a
source with a credit arrives).

## Slot API

```js
import { createMediaSlots } from './render/media.js';
const media = createMediaSlots(scene, track, { feel, samples });   // samples: sampleTrack(track, 4) is fine
media.update(dt, { camera, gentle, scrollMul, contrastMul });       // each frame, after the camera moves
media.setSource('bb-06', { kind: 'image', url, credit: 'scrolller' });
media.setSource('bb-06', null);                                     // back to the placeholder
media.dispose();
```

- `createMediaSlots(scene, track, opts)` adds one group to the scene and returns
  `{ object, slots, tunnel, setSource, update, dispose, stats }`. `opts.tier` (default the live `Q` tier at
  build), `opts.layout` (default `tracks/<id>.media.json`, fetched once at module load), `opts.feel` (Gentle
  is read from it when `update` is not told), `opts.make(w, h)` (canvas factory, tests).
- `slots`: `{ id, frame: 'billboard'|'halo'|'tunnel', kind, s, w, h, side, phone, C, Z }` (centre and face
  normal; halo slots move). Ids: the layout's billboard ids, `halo-<i>`, `tunnel-<ring>-wall-l|wall-r|ceil`.
- `setSource(slotId, { kind: 'image'|'video'|'canvas', url | el, credit })`: `el` is an `Image`,
  `HTMLVideoElement` or canvas the caller made; `url` builds one. The picture is cover-cropped to the slot.
  `credit: 'scrolller'` (or `{ source: 'scrolller' }`) writes "via Scrolller" on the slot's strip. Returns
  false and keeps the placeholder for a refused url or an unknown kind.
- `update(dt, view)`: `view.camera` (or `view.eye`), `view.gentle`, `view.scrollMul`, `view.contrastMul`.
- `stats()`: `{ slots, programs, draws, uploads, canvasPx }`.

## Placeholders

Six programs on shared canvases (phone: four), each shown by many slots as instances of one
InstancedMesh, cropped and hue-turned per slot: two **Loom spirals** (drawn once through the Loom's own
WebGL field renderer, its 2D fallback without WebGL), a lava colour field, turning polygons, the Racing
Thoughts mark (a road running into a spiral eye) and a kaleidoscope. They are drawn **once**; the slot
shader moves them: the turn (Loom bands capped at 1.2 a second past any point), a slow lava warp, a
breathing zoom and a hue drift. Colours come from `render/spectrum.js` (`propColour`, `hueAt`) with a
local full-spectrum ring as the fallback.

Why drawn once: a canvas upload into a WebGPU texture measured about 1 ms of frame time each (headless
Edge, 1440p), so redrawing four programs at 10 Hz cost about 0.8 ms a frame. Static canvases plus shader
motion cost nothing measurable. `placeholderFps` in the tier table turns redraws back on, under the
shared budget.

## Budget and tiers (`MEDIA_DEFAULTS.tiers`, override in `LOOK.media`)

| Tier | Canvas | Loom canvas | Programs | Uploads a frame | Real sources | Halo | Tunnel |
|---|---|---|---|---|---|---|---|
| high | 256 px | 512 px | 6 | at most 2 | canvas 10 Hz, video 24 Hz | 12 | every 9 m |
| mid | 192 px | 384 px | 6 | at most 2 | 8 Hz, 20 Hz | 12 | every 9 m |
| phone | 128 px | 256 px | 4 | at most 1 | 6 Hz, 15 Hz | 8 | every 18 m, billboards flagged `phone: false` hidden |

The upload budget is shared by every live source: nearest and most overdue first, nothing past `drawM`
(900 / 700 / 450 m). The whole layer is about 12 draws (one per program, borders, back plates, strips,
stalks, tunnel glass, ribs) plus one per real source.

Measured (harness, one kart, WebGPU, 2560x1440, uncapped): media on vs off is within run noise (about
0.0 to 0.2 ms); `update` costs 0.02 to 0.04 ms of CPU.

## Safety

- Nothing brightens a large area fast: the placeholders move position and hue only, the border hue cycles
  (allowed), luminance holds. Loom turns are capped by `safeSpin` (1.2 bands a second, the law is 3 Hz).
- Gentle: half the motion (placeholder time, halo orbit, turns, hue drift) and half the contrast, the
  border glow and the rib glow.

## Scrolller rules that will apply when real media lands

- **Honest UA.** Fetches go through the host with its honest user agent, never a spoofed one.
- **Nothing cached on our servers.** Media is cached only on the player's device.
- **24 hours.** Every Scrolller copy expires 24 h from download, and the off switch deletes them; nothing
  here may keep a copy longer (no IndexedDB, Cache Storage or service worker copies of media).
- **Never stream an uncached url.** `setSource` only takes local or same-origin urls (`urlAllowed`:
  `https://ccp.assets/...` on the desktop, the same-origin relay on the web, `blob:` and image or video
  `data:` urls). A remote Scrolller url is refused; the host fetches and caches, then hands the local copy.
- **Credit shown.** A Scrolller source always carries `credit: 'scrolller'`, which writes "via Scrolller"
  on its slot's strip.
- **Per-account daily share.** Every Scrolller byte the race shows counts against the account's daily share
  (the host's meter, real bytes); the race never fetches on its own.

## Phone pack note

`render/mediaPlaceholders.js` imports the Loom from its own tree by relative path:
`../../arcademy/engine/loom/loomField.js` (which imports `loomSpiral.js`). The phone web pack must ship
`arcademy/engine/loom/` beside `glassroad/`, and a dev server must serve `Resources/web/` (not only
`glassroad/`) or the import 404s. Without the Loom the two spiral programs fall back to the colour field;
the spiral is never forked here.
