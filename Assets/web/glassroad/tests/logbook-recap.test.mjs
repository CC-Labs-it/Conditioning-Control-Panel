import { test } from 'node:test';
import assert from 'node:assert/strict';
import { recapPages, recapState, hasStamps, dotsHtml, runPageHtml, stampsPageHtml, stampList, sketchSvg, medalHint, RECAP_FIXTURE } from '../hud/recap.js';

const HL = [{ key: 'driftBoosts3', label: 'Drift boosts, tier 3', value: '7' }, { key: 'airS', label: 'Seconds in the air', value: '11.4' },
  { key: 'overtakes', label: 'Overtakes', value: '9' }];
const NO_PB = { race: false, lap: false, trial: false };
const raceOnly = { mode: 'race', highlights: HL, sketch: { fallsAtS: [100], bestSector: 2, lengthM: 400 }, pb: NO_PB, medal: null, achievements: [], unlocks: [], xp: null };
const trial = { mode: 'trial', highlights: HL, sketch: { fallsAtS: [], bestSector: 0, lengthM: 400 }, pb: NO_PB,
  medal: { from: null, to: 'silver', times: { bronze: 200, silver: 185, gold: 172, glass: 165 } },
  achievements: [{ id: 'long-hold', name: 'Long Hold', line: 'Held a drift for four seconds' }], unlocks: [], xp: null };
// a square loop of 40 points, 10 m apart, evenly spaced along s (like boot's centreline)
const loop = (() => { const xz = []; for (let i = 0; i < 40; i++) { const k = Math.floor(i / 10), t = (i % 10) * 10;
  xz.push(...[[t, 0], [100, t], [100 - t, 100], [0, 100 - t]][k]); } return { xz: Float32Array.from(xz) }; })();

test('recap: a null or missing recap is one page, today\'s card', () => {
  for (const r of [null, undefined, 0]) {
    assert.deepEqual(recapPages(r), ['podium']);
    const st = recapState(r);
    assert.equal(st.last, true); assert.equal(st.next(), false); assert.equal(st.page, 'podium');
  }
  assert.equal(dotsHtml(['podium'], 0), '');
});

test('recap: a race with highlights only is two pages and stops on the second', () => {
  assert.equal(hasStamps(raceOnly), false);
  const st = recapState(raceOnly);
  assert.deepEqual(st.pages, ['podium', 'run']);
  assert.equal(st.last, false); assert.equal(st.next(), true);
  assert.equal(st.page, 'run'); assert.equal(st.last, true); assert.equal(st.next(), false); assert.equal(st.i, 1);
  const html = runPageHtml(raceOnly, loop);
  assert.equal((html.match(/<li /g) || []).length, 3);
  assert.match(html, /Drift boosts, tier 3/); assert.match(html, /class="rc-best"/);
  assert.equal((html.match(/class="rc-fall"/g) || []).length, 1);
});

test('recap: a trial with a medal and an achievement is three pages', () => {
  assert.deepEqual(recapPages(trial), ['podium', 'run', 'stamps']);
  const st = recapState(trial); st.next(); st.next();
  assert.equal(st.page, 'stamps'); assert.equal(st.last, true);
  const s = stampList(trial);
  assert.deepEqual(s.map((x) => x.head), ['Silver medal', 'Long Hold']);
  assert.equal(s[0].line, 'Gold is 2:52.00. Just saying.');
  const html = stampsPageHtml(trial);
  assert.match(html, /Held a drift for four seconds/);
  assert.match(dotsHtml(st.pages, 2), /Page 3 of 3/);
});

test('recap: each pb, the medal top and unlocks stamp in order', () => {
  const r = { ...raceOnly, pb: { race: true, lap: true, trial: true }, unlocks: [{ id: 'kart:velvet-lock', label: 'Velvet Lock' }] };
  assert.deepEqual(stampList(r).map((x) => x.head), ['New best race', 'New best lap', 'New best trial', 'Unlocked: Velvet Lock']);
  assert.equal(recapPages(r).length, 3);
  assert.equal(medalHint({ to: 'glass', times: {} }), 'Nothing above glass. Go outside.');
  assert.equal(medalHint({ to: 'bronze', times: { silver: 0 } }), '');
  assert.equal(hasStamps({ ...raceOnly, medal: { from: null, to: null } }), false);
});

test('recap: names, lines and values are HTML-escaped', () => {
  const r = { ...trial, highlights: [{ label: '<img src=x>', value: '"7"' }], achievements: [{ id: 'x', name: 'A & <b>', line: "it's <i>" }],
    unlocks: [{ id: 'u', label: '<script>' }] };
  const html = stampsPageHtml(r) + runPageHtml(r, loop);
  assert.doesNotMatch(html, /<script>|<img|<i>/); assert.equal(html.includes("A & <b>"), false);
  assert.match(html, /A &amp; &lt;b&gt;/); assert.match(html, /it&#39;s &lt;i&gt;/); assert.match(html, /Unlocked: &lt;script&gt;/);
  assert.match(html, /&quot;7&quot;/);
});

test('recap: the sketch maps s along the loop and skips without one', () => {
  assert.equal(sketchSvg(null, raceOnly.sketch), '');
  assert.equal(runPageHtml(raceOnly, null).includes('rc-sketch'), false);
  const svg = sketchSvg(loop, { fallsAtS: [0, 200, 1e9, NaN], bestSector: null, lengthM: 400 });
  assert.equal((svg.match(/class="rc-fall"/g) || []).length, 3);   // NaN dropped, past the end wraps
  assert.doesNotMatch(svg, /rc-best/);
  const cx = [...svg.matchAll(/cx="([\d.]+)" cy="([\d.]+)"/g)].map((m) => [Number(m[1]), Number(m[2])]);
  // s 0 is the loop start, s 200 (half way) is the opposite corner of the square
  assert.ok(Math.hypot(cx[0][0] - cx[1][0], cx[0][1] - cx[1][1]) > 60);
  assert.match(svg, /3 falls/);
  assert.equal(recapPages(RECAP_FIXTURE).length, 3);
});
