/* The small edition as a funnel, what the player sees (owner 2026-11-24, handoff-1124/tc-small-funnel/BRIEF.md, lane B):
 * locked seeds on the shelf, the recap wall, the small ad cards. Every press is the existing nudge frame with fixed ids,
 * only from a real press; counts come from the data; the full edition is untouched. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Ed from '../ui/edition.js';
import { SMALL_CUT, NUDGE_WHERE, NUDGE_WANT, SMALL_ENDING_SET } from '../data/editions.js';
import { FATE_SETS, setsFor } from '../data/fate.js';
import { BY_ID } from '../data/items.js';
import { LANES } from '../data/prestige.js';
import { ADS } from '../data/ads.js';
import { adPool, createAds } from '../ui/ads.js';
import { endingNudge, sendNudgeAs } from '../ui/nudges.js';
import { lockedSets, wallModel, endingName } from '../ui/small-funnel.js';
import { mountLockedTile } from '../ui/shop-locked.js';
import { SMALL_WALL_COPY, SMALL_CCP_ADS, LOCKED_SEED_COPY } from '../data/content/small-funnel.js';

const HOSTED = { hostname: 'tc.cclabs.app', hosted: true };
const small = Ed.readEdition({ caps: { edition: 'small', platform: 'web' } }, HOSTED);
const smallDesk = Ed.readEdition({ caps: { edition: 'small', platform: 'desktop' } }, HOSTED);
const unhosted = Ed.readEdition(null, { hostname: 'tc.cclabs.app' });

test('locked sets: what a full run with these picks is offered, minus Go Outside', () => {
  for (const f5 of [null, 'bimbo', 'drone', 'goon', 'trance']) {
    const got = lockedSets({ picks: { f5 } });
    const want = setsFor(f5, 0).filter((s) => s.id !== SMALL_ENDING_SET);
    assert.deepEqual(got.map((s) => s.id), want.map((s) => s.id));
    for (const s of got) assert.equal(s.seed, want.find((w) => w.id === s.id).cards[0]);
    assert.ok(!got.some((s) => s.id === SMALL_ENDING_SET));
  }
  assert.deepEqual(lockedSets({ picks: { f5: 'bimbo' } }).map((s) => s.id), ['emi', 'button', 'doll']);
  assert.equal(endingName('go_end'), 'EMI Takeover');
});

test('the wall reads every number from the data', () => {
  const { endings, parts } = wallModel();
  assert.deepEqual(endings.map((e) => e.id), FATE_SETS.filter((s) => s.id !== SMALL_ENDING_SET).map((s) => s.end));
  const n = (k) => parts.find((p) => p.key === k)?.n;
  for (const k of ['arcade', 'quiz', 'popups', 'combos']) assert.equal(n(k), SMALL_CUT[k].filter((id) => BY_ID[id]).length, k);
  assert.equal(n('renewal'), LANES.reduce((t, l) => t + l.stars.length, 0));
  for (const p of parts) assert.ok(!/\{n\}/.test(p.label), p.label);
  assert.match(parts.find((p) => p.key === 'quiz').label, new RegExp('^' + n('quiz') + ' '));
});

test('explicit-want nudges: fixed ids only, real press only, the same frame', () => {
  const sent = [];
  const send = (t, f) => sent.push([t, f]);
  assert.equal(sendNudgeAs('locked-tile', 'upgrade', { inPress: () => false, send, ed: small }), false);
  assert.equal(sendNudgeAs('nowhere', 'upgrade', { inPress: () => true, send, ed: small }), false);
  assert.equal(sendNudgeAs('ending', 'money', { inPress: () => true, send, ed: small }), false);
  assert.equal(sendNudgeAs('locked-tile', 'upgrade', { inPress: () => true, send, ed: small }), true);
  assert.deepEqual(sent, [['nudge', { where: 'locked-tile', want: 'upgrade' }]]);
  for (const [, f] of sent) { assert.ok(NUDGE_WHERE.includes(f.where)); assert.ok(NUDGE_WANT.includes(f.want)); }
  const opened = [];
  const win = { open: (u) => opened.push(u) };
  assert.equal(sendNudgeAs('ending', 'download', { inPress: () => true, win, ed: unhosted }), true);
  assert.deepEqual(opened, ['https://cclabs.app/ccp.html'], 'unhosted: the fixed link for that want');
});

test('the ending hook: with a want it sends that want, without one the platform\'s; no download button in the app', () => {
  const sent = [];
  const go = endingNudge({ win: {}, send: (t, f) => sent.push(f), ed: small });
  go({ isTrusted: true }, 'upgrade');
  go({ isTrusted: true }, 'download');
  go({ isTrusted: true });
  go({ isTrusted: false }, 'upgrade');
  assert.deepEqual(sent, [{ where: 'ending', want: 'upgrade' }, { where: 'ending', want: 'download' }, { where: 'ending', want: 'account' }]);
  assert.equal(go.offersDownload, true);
  assert.equal(endingNudge({ ed: smallDesk }).offersDownload, false);
});

function fakeDoc() {
  const byId = new Map();
  const make = (tag) => ({ tag, children: [], listeners: {}, attrs: {}, dataset: {}, style: {}, textContent: '', className: '', innerHTML: '',
    set id(v) { this._id = v; byId.set(v, this); }, get id() { return this._id; },
    append(...cs) { this.children.push(...cs); }, appendChild(c) { this.children.push(c); return c; }, setAttribute(k, v) { this.attrs[k] = v; },
    addEventListener(t, f) { (this.listeners[t] ||= []).push(f); }, click(ev = {}) { for (const f of this.listeners.click || []) f(ev); } });
  const head = make('head');
  return { head, createElement: make, createElementNS: (_ns, tag) => make(tag), getElementById: (id) => byId.get(id) || null };
}

test('locked seeds: handed to the shop with the tile, named, never engine rows, press = locked-tile upgrade', () => {
  const doc = fakeDoc(), sent = [];
  let provider = null;
  const door = { tail() {}, lockedSeeds: (fn) => { provider = fn; } };
  mountLockedTile({ doc, win: {}, door, send: (t, f) => sent.push([t, f]), ed: small });
  assert.equal(typeof provider, 'function');
  const list = provider({ picks: { f5: 'drone' } });
  assert.deepEqual(list.map((x) => x.id), ['emi', 'button', 'unit']);
  const el = list[2].make();
  assert.match(el.className, /\bfate\b/);
  assert.match(el.className, /\bfate-locked\b/);
  assert.equal(el.dataset.id, undefined, 'no row id');
  assert.equal(el.attrs['aria-label'], `Unit Complete. ${LOCKED_SEED_COPY.aria}`);
  assert.ok(el.children.some((c) => c.textContent === 'Unit Complete'));
  el.click({ isTrusted: false });
  assert.deepEqual(sent, []);
  el.click({ isTrusted: true });
  assert.deepEqual(sent, [['nudge', { where: 'locked-tile', want: 'upgrade' }]]);
});

test('ads: small swaps only the real cards, the full pool is the same object as before', () => {
  assert.equal(adPool(null), ADS);
  assert.equal(adPool('drone', false), adPool('drone'));
  const pool = adPool(null, true);
  assert.equal(pool.length, ADS.length);
  pool.forEach((ad, i) => {
    assert.equal(ad.ccp, ADS[i].ccp, 'same places');
    if (!ad.ccp) assert.equal(ad, ADS[i], 'joke ads untouched');
    else assert.ok(SMALL_CCP_ADS.some((c) => c.id === ad.id), ad.id);
  });
  assert.equal(adPool('bimbo', true), pool, 'a theme does not bring the mod cards back in small');
  const ids = new Set();
  const ads = createAds({ smallEdition: () => true });
  for (let i = 0; i < 12; i++) ids.add(ads.next({ ccpOnly: true }).id);
  assert.deepEqual([...ids].sort(), SMALL_CCP_ADS.map((c) => c.id).sort());
  assert.ok(createAds().next({ ccpOnly: true }).id.startsWith('c_'), 'no reading (node) = full');
});

test('copy: drafts in one file, house style, no Prime, no V2, no dash, no exclamation', () => {
  const all = [];
  const walk = (v) => { if (typeof v === 'string') all.push(v); else if (v && typeof v === 'object') Object.values(v).forEach(walk); };
  walk(SMALL_WALL_COPY); walk(LOCKED_SEED_COPY); walk(SMALL_CCP_ADS.map(({ headline, sub, cta }) => ({ headline, sub, cta })));
  for (const s of all) {
    assert.doesNotMatch(s, /[!–—]/, s);
    assert.doesNotMatch(s, /\bPrime\b|\bV2\b/, s);
  }
  assert.match(SMALL_WALL_COPY.offer, /full game is in CCP, for subscribers/);
});
