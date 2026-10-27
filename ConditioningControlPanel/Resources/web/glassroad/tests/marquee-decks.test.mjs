import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadTrack } from '../track/load.js';
import { fitMarquee, MARQUEE_VIEW } from '../sim/marqueeView.js';
import { deckIndex, deckOver } from '../sim/marqueeDecks.js';
import { fakeTrack } from './sim-fake.mjs';

// Find the current physical lower decks. Broader turns moved the old reported lap stations.
const goon = loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8')));
const physicalDecks = [];
for (let s = 0; s < goon.lengthM; s += 10) {
  const height = deckOver(deckIndex(goon), goon.frameAt(s), 0, 19, s);
  if (height > 28 && height < 34) physicalDecks.push(s);
}
const dropSpots = [physicalDecks[0], physicalDecks[Math.floor(physicalDecks.length / 2)]];
const wrapS = (track, s) => ((s % track.lengthM) + track.lengthM) % track.lengthM;

/** fitMarquee as the cue transport calls it (sim/songCues.js hang), for a kart on the centre line at s0. */
function fit(track, s0, { decks = true, sides = [0], view = null, minM = 60 } = {}) {
  const index = decks ? deckIndex(track) : null;
  let probe = null, probeS = 0;
  const spot = d => { probeS = wrapS(track, s0 + d); probe = track.frameAt(probeS); return probe; };
  const lift = (d, x) => (track.canalProfile ? track.canalProfile(probeS, x, {}).height : 0) || 0;
  const r = fitMarquee({ kart: track.frameAt(wrapS(track, s0)), lane: 0, sides, minM, view, spot, lift,
    deck: index ? (d, x, radius) => deckOver(index, probe, x, radius, probeS) : null });
  return r && { ...r, s: wrapS(track, s0 + r.aheadM) };
}
const deckAt = (track, s) => deckOver(deckIndex(track), track.frameAt(s), 0, 19, s);

test('GOONWORKS: the upper deck really passes over the current physical drop spots', () => {
  assert.ok(physicalDecks.length > 10 && dropSpots.every(Number.isFinite), 'real lower-road coverage remains');
  for (const s of dropSpots) {
    const h = deckAt(goon, s);
    assert.ok(h > 28 && h < 34, `road ${s} has the deck about 30 m overhead (got ${h})`);
  }
  assert.equal(deckAt(goon, 1000), Infinity, 'open road has nothing overhead');
});

test('GOONWORKS: screens due at or near the reported spots never fall through or hang across the deck', () => {
  const { bounceM, deckGapM } = MARQUEE_VIEW;
  for (const spotS of dropSpots) {
    let cases = 0;
    for (const aspect of [16 / 9, 4 / 3, 21 / 9]) for (const minM of [60, 90, 120]) for (let s0 = spotS - 280; s0 <= spotS - 60; s0 += 5) {
      const plain = fit(goon, s0, { decks: false, view: { aspect }, minM });
      if (Math.abs(plain.s - spotS) > 40) continue;   // the pick moves in 5 m steps and jumps with the climb
      const r = fit(goon, s0, { view: { aspect }, minM });
      cases++;
      if (!Number.isFinite(r.roomM)) assert.ok(r.clear, 'moved out from under the deck to open sky');
      assert.ok(r.roomM - r.fallM >= deckGapM - 1e-9, `${spotS}: the fall starts under the deck (${r.roomM} m room, ${r.fallM} m fall)`);
      assert.ok(r.roomM >= bounceM + deckGapM, 'the hanging screen and its bounce stay under the deck');
    }
    assert.ok(cases > 0, `some approach lands its deck-blind pick near ${spotS}`);
  }
});

test('GOONWORKS: along the whole double deck, at every aspect and approach, no drop starts above the deck', () => {
  const { bounceM, deckGapM } = MARQUEE_VIEW, seen = { capped: 0 };
  for (const aspect of [16 / 9, 4 / 3, 21 / 9, 9 / 16]) for (const minM of [60, 120]) for (const s0 of physicalDecks.filter((_, i) => i % 2 === 0)) {
    const index = deckIndex(goon), view = { aspect };
    let probe = null, probeS = 0;
    const call = deck => fitMarquee({ kart: goon.frameAt(s0), minM, view, spot: d => (probe = goon.frameAt(probeS = s0 + d)), deck });
    const plain = call(null), r = call((d, x, radius) => deckOver(index, probe, x, radius, probeS));
    assert.ok(r.roomM - r.fallM >= deckGapM - 1e-9, `s0 ${s0} aspect ${aspect.toFixed(2)}: fall starts under the deck`);
    if (!r.clear) { assert.ok(r.roomM >= bounceM + deckGapM, 'a capped screen still hangs and bounces under the deck'); seen.capped++; }
    if (r.aheadM === plain.aheadM && r.scale === plain.scale && r.fallM < plain.fallM - 1e-9) assert.equal(r.clear, false, 'only a deck shortens a fall');
  }
  assert.ok(seen.capped > 0, 'the stretch has spots where the fall must be capped (the bug)');
});

test('a deck over the view pick moves the spot to the nearest clear one, later first, then earlier', () => {
  const flat = fakeTrack(), s0 = 300;
  // A downward reference view makes the optimum lie beyond minM, leaving real
  // earlier candidates for this selection-policy fixture. Live-view tests above use the current neutral.
  const call = deck => fitMarquee({ kart: flat.frameAt(s0), minM: 60, view: { neutralPitchDeg: 0 }, spot: d => flat.frameAt(s0 + d), deck });
  const plain = call(null);
  const later = call(d => Math.abs(d - plain.aheadM) <= 10 ? 20 : Infinity);
  assert.ok(later.clear && later.fits && later.aheadM === plain.aheadM + 15 && later.scale === plain.scale, 'next clear spot beyond the deck');
  const earlier = call(d => d >= plain.aheadM - 5 ? 20 : Infinity);
  assert.ok(earlier.clear && earlier.fits && earlier.aheadM < plain.aheadM, 'no clear spot later: the nearest earlier one');
  const low = call(() => 22);
  assert.ok(!low.clear && low.fallM <= low.roomM - MARQUEE_VIEW.deckGapM + 1e-9, 'deck everywhere: the fall is capped under it');
});

test('a course or stretch with no decks picks exactly the plain spot', () => {
  const flat = fakeTrack();
  for (const s0 of [300, 900]) {
    const a = fit(flat, s0, { decks: false }), b = fit(flat, s0);
    assert.deepEqual({ ...b, clear: undefined, roomM: undefined }, { ...a, clear: undefined, roomM: undefined });
    assert.ok(b.clear && b.roomM === Infinity);
  }
  for (const s0 of [700, 900, 3000]) {
    const a = fit(goon, s0, { decks: false }), b = fit(goon, s0, { sides: [0] });
    assert.deepEqual({ ...b, clear: undefined, roomM: undefined }, { ...a, clear: undefined, roomM: undefined }, `goonworks ${s0}`);
  }
});

test('the deck index is deterministic and cached per course', () => {
  const a = deckIndex(goon), b = deckIndex(goon);
  assert.equal(a, b);
  const again = deckIndex(loadTrack(JSON.parse(readFileSync(new URL('../tracks/goonworks.json', import.meta.url), 'utf8'))));
  for (const s of [...dropSpots, 1000]) assert.equal(deckOver(again, goon.frameAt(s), 0, 19, s), deckAt(goon, s));
});
