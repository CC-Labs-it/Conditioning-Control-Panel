# Prize Counter (the 7.2 shop) - page contract

Scope and owner calls: `handoff-1029/B3-SHOP-DECISIONS.md` (sections 5 and 6 are binding here). Art brief:
`handoff-1029/B3-ASTRA-3D-BRIEF.md`. One 3D counter, Sparkles only, weekly stock, no refunds, owned stays stamped.

## Files and who owns them

| path | lane | what |
|---|---|---|
| `assets/` | B3-ART (Astra) | exported GLBs, copied from `blender-scripting/counter/prize-counter/out/`. Never edited here; re-export there and copy again. `assets/manifest.json` is the slim runtime manifest (scratchpad `slim-manifest.mjs` makes it from Astra's). |
| `assets/items/ATTRIBUTION.txt` | B3-ART | Rain is CC BY 4.0: it travels with every Rain-derived GLB. |
| `mock/state.sample.json` | shared | one `/v2/shop/state` body: every tab, three owned rows, Prime rows, Back Room rows. Tests and `dev.html` read it. |
| `state/` | B3-W1 | pure, no DOM, no three: read the server body, tabs, shelves, focus walk, confirm, refusals. |
| `host.js` | B3-W1 | the host seam below. |
| `scene/` | B3-W2 | three.js: load, place, cameras, input. |
| `fx/` | B3-W3 | springs, dust, settle, sfx bus, Gentle / reduced / motion off. |
| `hud/` | B3-W4 | tabs, wallet, preview box, confirm card, stamps. |
| `preview/` | B3-W5 | the HUD preview box: `catalog.js` (item id -> spec over `assets/preview/index.json`), `loader.js` (rests 140 ms, drops stale loads, disposes on leave, frees a kind's shared art when the focus leaves it), `stage.js` (3D turntable: Rain, kart bodies, outfits, trail, chess), `pictures.js` (frames, avatars, EMI desk sheets, paints). |
| `assets/preview/` | B3-W5 (art is Astra's) | byte copies from `out/preview` and `out/art` (only what the box draws: the knight variants, idle desk overlays, one Rain attribution) plus the approved `piecebypiece` knight and the arcademy EMI idle body; `index.json` = one spec per item id. Budget: no file over 8 MB, the tree under 10 MB (`tests/preview.test.mjs`). Kart outfits and the lace umbrella preview WORN on the rigged kart base (F5: Astra's decoded `sources-decoded/emi-teacup-kart.glb`, PNG textures, since r169 here has no KTX2Loader), paints on its shell (glassroad `art/paints` masks, flat swatch when there is no 3D); Rain coverage follows the wardrobe (`preview/fit.js`). |
| `index.html`, `main.js`, `dev.html` | B3-W1 creates, others add one line each | page entry; `dev.html` runs on the mock with no host. |
| `hud/guide.js`, `state/guide.js`, `art/guide/` | G3 | the "How it works" card (see Rules). Its prints are real captures: kart shelf only, no chess, no Rain. |
| `tests/*.test.mjs` | every lane | `node --test tests/*.test.mjs` from this folder (the bare folder form fails on Node 24). Every lane runs the whole folder. |

## Assets (glTF Y up, front +Z, metres)

- `prize-counter.glb`, root `prize_counter`: the Back Room counter rebuilt with four shelf bays. Empties `bay_0..bay_3`
  (where a shelf unit hangs), `cam_wide`, `cam_bay_0..cam_bay_3` (customer-side framing per bay), `emi_mark`.
  EMI's rig groups `emi_attendant`, `shoulderL/R`, `ant0..2`, `ballpiv` are intact for the cheer.
- `shelf-unit.glb`, root `shelf_unit`, pivot at the TOP centre (it swings from there). Slots `slot_r{0..2}c{0..2}`,
  row 0 highest; price tags `tag_r?c?`; dust contact points `dust_l/c/r`. One unit per bay, cloned.
- `items/<id>.glb` and `items/<id>.lod1.glb`, root `item_<id with . as _>`, pivot at the base centre, fits the slot box
  `assets/manifest.json slotBox` (0.72 x 0.78 x 0.52). Items carry their own display support: never add a plinth.
  Shelf draws LOD1; the focused item swaps to the full GLB.
- `props/price_tag.glb`, `props/owned_stamp.glb` sit on `tag_r?c?`. The other props are Astra's kit, kept for later drops.
  Art pass 2: `tag_r?c?` is the tag's top hinge; the enamel face `tag_face` (0.694 x 0.118 m) hangs under `tag_pivot`,
  and the price plane rides on `tag_pivot` (clips `tag_approach` / `tag_retreat` ship in the GLB, unused yet). The
  stamp hangs 0.14 m under the hinge. Use the shelf and the tag from the same export.
- Catalog revision 2 (`assets/manifest.json catalogRevision`): `retired` maps each withdrawn id to its stand-in art
  (`null` = none; an owner still gets the OWNED stamp). Retired ids never return to sale and are never reused.

## Server body (read only, CCP-Server is the authority)

`GET /v2/shop/state` -> `{ ok, sp, catalogVersion, week, tier, prizes:{revision, grants[]}, tabs:[{ id,
shelves:[{ rows:[{ id, tier, priceSp, grants, needs?, owned:{at,paidSp}|null, sale:'on'|'soon' }] }] }] }`.
The server lays out the shelves; the page draws shelf `i` in `bay_i`, row `j` in slot `r{floor(j/3)}c{j%3}`.
Tabs in order: `rain-clothes rain-hair kart chess profile desk prime backroom`.

`POST /v2/shop/buy { idem, itemId, catalogVersion, week }` -> receipt `{ ok, idem, itemId, paidSp, spBefore, sp,
catalogVersion, prizes }` or HTTP 200 `{ ok:false, reason }`, reasons: `bad_input idem_mismatch catalog_changed
stock_changed not_in_stock unavailable owned needs_prime insufficient busy`. `stock_changed` and `catalog_changed`
carry a fresh state. One idem per confirm card; a retry reuses it.

## Host seam

The page talks to whoever hosts it through `host.js`, never `fetch` to the server directly:

- in: `init { identity?, sp, locale, lexicon, motion:'full'|'less'|'off', gentle, reducedMotion, tab, from, platform, companion? }`. `companion` = the active mod's name for the V2 avatar (Bambi, Bimbo, Emi; "Rain" is only the 3D model), filling `{companion}` in copy; absent = Emi. `rainV2` true (WPF `RainV2Gate.On`) shows Rain art and the portrait on the signed-out window; absent = hidden.
- out: `request('shop-state')`, `request('shop-buy', {itemId, catalogVersion, week}, idem)` -> the server body as above,
  or `{ ok:false, reason:'offline'|'signed_out' }` from the host itself.
- out: `wallet { sp }` after a receipt (the WPF host adopts it with `V2WalletAdoption`, `fromBuy = true`).
- out: `close { reason:'back'|'panic' }`. Esc ladder: how-to card -> confirm card -> preview -> close.
- out: `wear { id }` from an owned card's Wear (frame `{type:'wear', id}`). WPF: a Rain row (`cos.<id>` in
  `rain-wardrobe/items.json`) closes the counter and opens Rain's dressing room with that piece tried on. Fix wave F1:
  `init.wears` lists the families the host puts on (rain, desk, chess, profile; kart only from the garage). Desk, chess
  and profile are worn in place (the card notes "Wearing it now.") and the host answers `worn { id, ok }`; `ok:false`
  turns the note into "That did not go through". A kart away from the garage reads "Wear it in the garage" and only
  steps back; Back Room prizes have no Wear. A host whose `wears` list lacks `desk` (website, phone) shows an owned desk row with no Wear and the line "Shows on EMI's desk in the desktop app". A host that sends no `wears` keeps the plain Wear. The host caches the `cos.rain.*` prices of each shop-state for the dressing room's badges.
- out: `signIn()` when `shop-state` answered `signed_out` (owner 2026-10-29): the page greys a window display of the
  art (no prices, no tabs, no wallet) behind one "Sign in to shop" button; the button, or select/Enter/A, asks the host.
  The host opens its own sign-in and, once someone is signed in, loads the page again with a fresh `init`
  (WPF: the panel's account dialog over the counter, then the page reloads in place). Cancelled = nothing changes.
- Desktop relays to the server with the identity; web and phone fetch with the session token. Echo law: nothing moves
  until the receipt lands.

Mounted inside a game (`mountPrizeCounter(host, opts)`), the same seam is an object; standalone it is `postMessage`.
Standalone frames (`host.js`): out `{type:'ready', protocol:1}`, `{type:'request', reqId, op, body, idem}`, `{type:'wallet', sp}`,
`{type:'close', reason}`, `{type:'signin'}`, `{type:'log', level, msg}`; in `{type:'init', ...}`, `{type:'reply', reqId, body}` (body = the server body).
`stock_changed` / `catalog_changed` carry the fresh state as `state` (the page also reads it inline).

## Rules

- Every effect has an IN and an OUT. Gentle visuals: fades only, no dust, no shake. Motion off: cuts.
- Copy: no em dashes, no exclamation marks; strings are loc keys `shop_*` in all ten language files.
- Nothing here writes Sparkles. Only a receipt's `sp` moves the wallet.
- Stray keys never spend (desk bug 2026-10-30, `state/press-guard.js`): the counter opens with nothing focused and
  no card; a new card face (Buy, then Wear) focuses the card itself, never its action. Space, Enter and pad A select
  on RELEASE of a press that began after the window had focus 600 ms and the card had been up 400 ms; a repeat or a
  key held from before focus never counts, and a key's synthetic click on the action is dropped. A key's select never
  reaches an open card from the shelves. Pointer clicks act as before, after the same 400 ms on a fresh card.
- How it works (`hud/guide.js`): opens by itself once per account (`localStorage` key `pc.howto.seen.<unifiedId>`, in try/catch;
  a blocked store reads as seen) on a ready counter with no card, and its door in the top line reopens it. Skipped, and not marked seen, when
  the counter opens to put something on (`state/guide.js opensToWear`: `init.wear`, or the door is the dressing room). While it is up nothing
  reaches the shelves: select and back close it through the press guard, and it closes with nothing focused.
