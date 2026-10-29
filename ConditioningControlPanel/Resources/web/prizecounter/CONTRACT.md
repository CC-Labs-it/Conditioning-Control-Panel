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
| `preview/` | B3-W5 | per-family preview in the HUD box. |
| `index.html`, `main.js`, `dev.html` | B3-W1 creates, others add one line each | page entry; `dev.html` runs on the mock with no host. |
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

- in: `init { identity?, sp, locale, lexicon, motion:'full'|'less'|'off', gentle, reducedMotion, tab, from, platform }`.
- out: `request('shop-state')`, `request('shop-buy', {itemId, catalogVersion, week}, idem)` -> the server body as above,
  or `{ ok:false, reason:'offline'|'signed_out' }` from the host itself.
- out: `wallet { sp }` after a receipt (the WPF host adopts it with `V2WalletAdoption`, `fromBuy = true`).
- out: `close { reason:'back'|'panic' }`. Esc ladder: confirm card -> preview -> close.
- Desktop relays to the server with the identity; web and phone fetch with the session token. Echo law: nothing moves
  until the receipt lands.

Mounted inside a game (`mountPrizeCounter(host, opts)`), the same seam is an object; standalone it is `postMessage`.

## Rules

- Every effect has an IN and an OUT. Gentle visuals: fades only, no dust, no shake. Motion off: cuts.
- Copy: no em dashes, no exclamation marks; strings are loc keys `shop_*` in all ten language files.
- Nothing here writes Sparkles. Only a receipt's `sp` moves the wallet.
