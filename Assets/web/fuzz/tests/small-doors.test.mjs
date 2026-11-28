/* Lane D (owner 2026-11-24, handoff-1124/tc-small-funnel/DOORS-BRIEF.md): the small edition's doors to the full game stop
 * ambushing new players. The locked tile shows only from the seed shelf on; every in-run door opens the door card before
 * any link; one outside link a run, shared by ads and doors (the recap wall exempt); no pause line in small. The full
 * edition is unchanged. */
import { test, beforeEach } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as Ed from '../ui/edition.js';
import * as E from '../sim/economy.js';
import { CONFIG } from '../data/config.js';
import { adLinkStep } from '../ui/ads.js';
import { tailTime } from '../ui/shop.js';
import { fateShelf } from '../ui/fate-look.js';
import { SEEDS } from '../data/fate.js';
import { BY_ID } from '../data/items.js';
import { linkCap, linkRecord, linkLeft, runLinks, SMALL_LINKS_PER_RUN } from '../ui/run-links.js';
import { openDoorCard, _resetDoorCard } from '../ui/door-card.js';
import { mountLockedTile, LOCKED_TILE_COPY } from '../ui/shop-locked.js';
import { DOOR_CARD_COPY, SMALL_WALL_COPY } from '../data/content/small-funnel.js';
import { mountWall } from '../ui/recap/wall.js';

const HOSTED = { hostname: 'tc.cclabs.app', hosted: true };
const small = Ed.readEdition({ caps: { edition: 'small', platform: 'web' } }, HOSTED);
const full = Ed.readEdition({ caps: { edition: 'full', platform: 'web' } }, HOSTED);
const src = (p) => readFileSync(new URL(p, import.meta.url), 'utf8');

/* ---- a fake legal window: records what the card asks for, hands back buttons by data-door ---- */
function fakeLegal() {
  const made = { closed: 0, shows: [] };
  const btn = () => ({ listeners: {}, attrs: {}, style: {}, addEventListener(t, f) { (this.listeners[t] ||= []).push(f); },
    click(ev = { isTrusted: true }) { for (const f of this.listeners.click || []) f(ev); }, setAttribute(k, v) { this.attrs[k] = v; }, focus() {} });
  made.factory = () => ({
    show(spec) {
      made.shows.push(spec);
      const parts = {};
      for (const k of ['go', 'back', 'spent']) if (spec.body.includes(`data-door="${k}"`)) parts[k] = btn();
      const x = btn();
      x.cloneNode = () => { made.x = btn(); return made.x; };
      x.replaceWith = () => {};
      made.parts = parts;
      return { classList: { add() {} }, querySelector: (q) => (q === '.fakex' ? x : parts[(/data-door=(\w+)/.exec(q) || [])[1]] || null) };
    },
    close() { made.closed += 1; },
  });
  return made;
}
function fakeDoc() {
  const keys = [];
  return { keys, getElementById: () => null, addEventListener: (t, f) => { if (t === 'keydown') keys.push(f); }, removeEventListener: (t, f) => { const i = keys.indexOf(f); if (i >= 0) keys.splice(i, 1); } };
}
const key = (doc, k) => { const ev = { key: k, prevented: false, stopped: false, preventDefault() { this.prevented = true; }, stopImmediatePropagation() { this.stopped = true; } }; [...doc.keys].forEach((f) => f(ev)); return ev; };

beforeEach(() => _resetDoorCard());

test('the tile shows only from the seed shelf on: absent before, present while the shelf shows, kept after a pick', () => {
  /* the shop's own reading: its window, split for the seed shelf (ui/fate-look.js), then tailTime */
  const due = (r) => { const picked = E.fateOf(r), w = E.shopWindow(r, null, 6), pool = E.shopWindow(r, null, 99); return tailTime(fateShelf(w, picked, picked ? w : pool, SEEDS).seeds.length, picked); };
  const fresh = E.createRun({ ed: small });
  fresh.fuzz = fresh.total = 1e6;
  assert.equal(due(fresh), false, 'a new run, even a rich one: no shelf, no tile');
  /* a run at the seed shelf (as tests/editions-ending.test.mjs atSeeds) */
  const s = E.createRun({ ed: small });
  s.fuzz = s.total = 1e12;
  E.grant(s, 'f4'); s.picks.f5 = 'trance'; E.grant(s, 'taste2');
  assert.equal(due(s), true, 'the shelf shows its seed: the tile is due');
  E.grant(s, 'ft_window');
  assert.ok(E.fateOf(s), 'a seed picked');
  assert.equal(due(s), true, 'after the pick the tile stays');
  assert.equal(tailTime(0, null), false);
  const shop = src('../ui/shop.js');
  assert.match(shop, /tailDue = tailTime\(win\.seeds\.length, picked\);/, 'draw() reads the shelf it just drew');
  assert.match(shop, /if \(tailEl && !tailDue && tailEl\.parentNode === rowsEl\) tailEl\.remove\(\);/);
  for (const id of SEEDS) assert.ok(BY_ID[id], id);
});

test('tile and seed presses open the card and send nothing; the link sends one nudge and spends the run\'s link', () => {
  const sent = [], legal = fakeLegal(), doc = fakeDoc();
  const run = E.createRun({ ed: small });
  const links = runLinks(() => run, small);
  let provider = null;
  const make = () => ({ children: [], listeners: {}, attrs: {}, dataset: {}, style: {}, append(...c) { this.children.push(...c); }, appendChild(c) { this.children.push(c); return c; },
    setAttribute(k, v) { this.attrs[k] = v; }, addEventListener(t, f) { (this.listeners[t] ||= []).push(f); }, click(ev) { for (const f of this.listeners.click || []) f(ev); } });
  const tileDoc = { head: make(), createElement: make, createElementNS: make, getElementById: () => null };
  const open = (spec) => openDoorCard({ ...spec, doc, makeLegal: legal.factory });
  const tile = mountLockedTile({ doc: tileDoc, win: {}, send: (t, f) => sent.push([t, f]), ed: small, links, open,
    door: { tail() {}, lockedSeeds: (fn) => { provider = fn; } } });
  tile.click({ isTrusted: true });
  assert.deepEqual(sent, [], 'the tile press sends nothing');
  assert.equal(legal.shows.length, 1);
  assert.equal(legal.shows[0].title, LOCKED_TILE_COPY.label);
  assert.equal(legal.shows[0].head, SMALL_WALL_COPY.offer);
  assert.ok(legal.parts.go && legal.parts.back && !legal.parts.spent);
  legal.parts.go.click({ isTrusted: false });
  assert.deepEqual(sent, [], 'a script click on the link is not a press');
  assert.equal(linkLeft(run, small), true, 'nothing spent');
  legal.parts.go.click({ isTrusted: true });
  assert.deepEqual(sent, [['nudge', { where: 'locked-tile', want: 'account' }]]);
  assert.equal(legal.closed, 1, 'the card closes after the link');
  assert.equal(linkRecord(run).n, 1);
  assert.equal(linkLeft(run, small), false, 'the run has had its one link');

  /* a locked seed: same card, now only Back */
  const seed = provider(Object.assign(E.createRun({ ed: small }), { store: run.store }))[0]?.make();
  assert.ok(seed, 'a locked seed');
  seed.click({ isTrusted: true });
  assert.equal(legal.shows.length, 2);
  assert.ok(!legal.parts.go && legal.parts.spent && legal.parts.back, 'spent: no link button, a line says why');
  assert.ok(legal.shows[1].body.includes(DOOR_CARD_COPY.spent));
  legal.parts.back.click();
  assert.equal(legal.closed, 2, 'Back still closes');
  assert.equal(sent.length, 1, 'nothing more went');

  /* ads are dead too: the shared record says spent */
  assert.equal(adLinkStep(linkRecord(run), linkCap(small)), 'none');
});

test('the card: Escape and the X close it, Escape is taken before the pause sees it, one card at a time', () => {
  const legal = fakeLegal(), doc = fakeDoc();
  const a = openDoorCard({ doc, title: 't', makeLegal: legal.factory });
  assert.equal(openDoorCard({ doc, title: 't', makeLegal: legal.factory }), a, 'a second press answers the open card');
  const ev = key(doc, 'Escape');
  assert.ok(ev.prevented && ev.stopped);
  assert.equal(a.open, false);
  assert.equal(doc.keys.length, 0, 'its key listener leaves with it');
  const b = openDoorCard({ doc, title: 't', makeLegal: legal.factory });
  assert.equal(key(doc, 'Enter').prevented, false, 'other keys pass');
  legal.x.click();
  assert.equal(b.open, false, 'the X works here');
  assert.equal(legal.x.attrs['aria-label'], DOOR_CARD_COPY.xLabel);
});

test('one link a run in small, ads and doors together; the full edition keeps AD_TABS_PER_RUN', () => {
  assert.equal(SMALL_LINKS_PER_RUN, 1);
  assert.equal(linkCap(small), 1);
  assert.equal(linkCap(full), CONFIG.AD_TABS_PER_RUN);
  assert.equal(linkCap(Ed.readEdition(null, { noPage: true })), CONFIG.AD_TABS_PER_RUN, 'no page (node) = full');
  /* small: the first ad press opens with no ask, after it nothing opens or asks */
  const rec = linkRecord(E.createRun({ ed: small }));
  assert.equal(adLinkStep(rec, linkCap(small)), 'open');
  rec.n = 1;
  assert.equal(adLinkStep(rec, linkCap(small)), 'none');
  /* a door spent it first: the ad is dead */
  const run = E.createRun({ ed: small });
  runLinks(() => run, small).spend();
  assert.equal(adLinkStep(linkRecord(run), linkCap(small)), 'none');
  /* full: two tabs as before, ask from the second */
  const f = linkRecord(E.createRun());
  assert.equal(adLinkStep(f, linkCap(full)), 'open');
  f.n = 1;
  assert.equal(adLinkStep(f, linkCap(full)), 'ask');
  /* the record is the ads' own on the run store, so it saves and resets with the run */
  assert.match(src('../ui/run-links.js'), /\.adtabs \|\|= \{ n: 0 \}/);
  assert.match(src('../ui/context.js'), /const used = linkRecord\(engine\.run\), cap = linkCap\(\);/);
});

test('wiring: small mounts the tile with the shared cap, no pause line, the menu door opens the card', () => {
  const main = src('../main.js');
  assert.match(main, /const doorLinks = runLinks\(\(\) => engine\.run, ed\);/);
  assert.match(main, /if \(isSmall\(ed\)\) mountLockedTile\(\{ doc: document, win: window, send: bridge\.send, ed, links: doorLinks, gentle: doorGentle \}\);/);
  assert.doesNotMatch(main, /mountPauseNudge\(/, 'no pause line in small');
  assert.match(main, /doorOpener\(\{ doc: document, win: window, send: bridge\.send, ed, links: doorLinks, gentle: doorGentle \}\)\(\(o\) => sendNudge\('pause', o\)\)\(\);/);
  const tile = src('../ui/shop-locked.js');
  assert.doesNotMatch(tile, /addEventListener\('click', \(ev\) => sendNudge/, 'no press sends a nudge by itself');
});

test('the recap wall is exempt: it still links after the run\'s link is spent', () => {
  const wall = src('../ui/recap/wall.js');
  assert.doesNotMatch(wall, /run-links|linkLeft|linkCap/, 'the wall never asks the cap');
  assert.equal(typeof mountWall, 'function');
  /* the wall's door is the ending hook, which sends from any real press whatever the run spent */
  const run = E.createRun({ ed: small });
  runLinks(() => run, small).spend();
  const sent = [];
  return import('../ui/nudges.js').then(({ endingNudge }) => {
    const go = endingNudge({ win: {}, send: (t, f) => sent.push(f), ed: small });
    assert.equal(go({ isTrusted: true }, 'upgrade'), true);
    assert.deepEqual(sent, [{ where: 'ending', want: 'upgrade' }]);
  });
});

test('copy: the card\'s drafts in house style; approved lines reused, untouched', () => {
  for (const s of Object.values(DOOR_CARD_COPY)) {
    assert.doesNotMatch(s, /[!–—]/, s);
    assert.doesNotMatch(s, /\bPrime\b|\bV2\b|llama|Expo/i, s);
  }
  assert.equal(LOCKED_TILE_COPY.label, 'More in the full game');
  assert.equal(SMALL_WALL_COPY.offer, 'The full game is in CCP, for subscribers.');
});
