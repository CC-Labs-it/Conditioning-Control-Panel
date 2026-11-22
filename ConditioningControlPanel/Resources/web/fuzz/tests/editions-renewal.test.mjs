/* Renewal off in the small edition (lane S4, 2026-11-22): an open Sky visit sleeps and is written back untouched, the
 * save-sleep note shows once per save (in the save's own extras) and sends its nudge only from a real press, and the
 * full edition never sees either. */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Ed from '../ui/edition.js';
import * as E from '../sim/economy.js';
import { createLife, parse, serialize, sleepingSky, sleepsSomething, saveSleepDue } from '../sim/save.js';
import { parsePrestige, parseStamp } from '../sim/prestige.js';
import { mountSaveSleep, openSkyAsleep, SAVE_SLEEP_COPY, SKY_ASLEEP_COPY } from '../ui/save-sleep.js';

const small = Ed.readEdition({ caps: { edition: 'small', platform: 'web' } }, { hostname: 'tc.cclabs.app', hosted: true });
const full = Ed.readEdition({ caps: { edition: 'full', platform: 'desktop' } }, { hostname: 'ccp.game', hosted: true });
const SKY = { caught: 3 };

function skySave() {
  const life = createLife();
  life.prestige = parsePrestige({ depth: 2, dust: 300 });
  return serialize(null, life, { sky: SKY, again: 1, audioMix: { m: 1 } });
}

test('an open Sky visit sleeps in small only, and comes back to full untouched', () => {
  const raw = skySave();
  assert.equal(sleepingSky(parse(raw, { ed: small }), small), true);
  assert.equal(sleepingSky(parse(raw, { ed: full }), full), false, 'full reopens the Sky as before');
  assert.equal(sleepingSky(parse(raw), null), false);
  const withRun = serialize(E.createRun(), createLife(), { sky: SKY });
  assert.equal(sleepingSky(parse(withRun, { ed: small }), small), false, 'a run under it is not an open visit');
  /* The player's own New run in small: the stand-in run is kept, extras.sky as it was. */
  const saved = parse(raw, { ed: small });
  const standIn = E.createRun({ ed: small });
  const extras = { ...saved.extras, again: 1 };
  const back = parse(serialize(standIn, saved.life, extras));
  assert.deepEqual(back.extras.sky, SKY, 'extras.sky written back untouched');
  assert.deepEqual(back.life.prestige, saved.life.prestige, 'the renewal stays applied');
});

test('the save-sleep rule: something asleep, once per save, small only', () => {
  const fresh = parse(serialize(E.createRun(), createLife(), {}), { ed: small });
  assert.equal(sleepsSomething(fresh, small), false, 'a plain save has nothing asleep');
  const cutRun = E.createRun(); E.grant(cutRun, 'swap');
  const owning = parse(serialize(cutRun, createLife(), {}), { ed: small });
  assert.equal(sleepsSomething(owning, small), true, 'a cut row');
  const stamped = E.createRun({ prestige: parseStamp({ depth: 1, pay: 1.2 }) });
  assert.equal(sleepsSomething(parse(serialize(stamped, createLife(), {}), { ed: small }), small), true, 'a run stamp');
  const renewed = createLife(); renewed.prestige = parsePrestige({ depth: 1 });
  assert.equal(sleepsSomething(parse(serialize(null, renewed, {}), { ed: small }), small), true, 'Renewal life state');
  assert.equal(sleepsSomething(parse(skySave(), { ed: small }), small), true, 'an open Sky visit');
  for (const raw of [serialize(cutRun, renewed, {}), skySave()]) {
    assert.equal(sleepsSomething(parse(raw, { ed: full }), full), false, 'never in full');
    assert.equal(saveSleepDue(parse(raw), null), false);
  }
  const seen = parse(serialize(cutRun, createLife(), { sleepSeen: 1 }), { ed: small });
  assert.equal(saveSleepDue(seen, small), false, 'shown once: remembered in the save extras');
  assert.equal(saveSleepDue(owning, small), true);
});

/* A tiny DOM, enough for the note. */
function fakeDoc() {
  const byId = new Map();
  const el = (tag) => {
    const e = { tag, children: [], listeners: {}, attrs: {}, className: '', textContent: '', parent: null,
      set id(v) { this._id = v; byId.set(v, this); }, get id() { return this._id; },
      setAttribute(k, v) { this.attrs[k] = v; }, append(...xs) { for (const x of xs) this.appendChild(x); },
      appendChild(x) { x.parent = this; this.children.push(x); return x; },
      addEventListener(t, f) { (this.listeners[t] ||= []).push(f); },
      remove() { if (this.parent) this.parent.children = this.parent.children.filter((c) => c !== this); if (this._id) byId.delete(this._id); },
      click() { for (const f of this.listeners.click || []) f({}); } };
    return e;
  };
  const doc = { createElement: el, getElementById: (id) => byId.get(id) || null, head: el('head'), body: el('body') };
  return doc;
}
const buttonsOf = (note) => note.children.find((c) => c.className === 'row').children;

test('the note: once, never a backdrop, its nudge only from a real press, still under Gentle', () => {
  const doc = fakeDoc(), sent = [], saves = [];
  let active = false;
  const win = { navigator: { userActivation: { get isActive() { return active; } } } };
  const extras = {};
  const close = mountSaveSleep({ doc, win, extras, ed: small, send: (t, f) => sent.push([t, f]), gentle: () => true, save: () => saves.push(1) });
  const note = doc.getElementById('save-sleep');
  assert.ok(note, 'mounted');
  assert.equal(extras.sleepSeen, 1, 'remembered in the save extras');
  assert.equal(saves.length, 1, 'and saved');
  assert.equal(note.className, '', 'no motion under Gentle');
  assert.equal(note.attrs.role, 'status', 'a status note, not a modal dialog');
  const [go] = buttonsOf(note);
  assert.equal(go.textContent, SAVE_SLEEP_COPY.nudge);
  /* A script click carries no activation: no frame. */
  go.click();
  assert.deepEqual(sent, []);
  assert.equal(doc.getElementById('save-sleep'), null, 'the note closes either way');
  close();
  /* Once per save: a second mount on the same extras does nothing. */
  mountSaveSleep({ doc, win, extras, ed: small, send: () => sent.push(1) });
  assert.equal(doc.getElementById('save-sleep'), null);
  /* A real press sends the fixed frame. */
  const extras2 = {};
  mountSaveSleep({ doc, win, extras: extras2, ed: small, send: (t, f) => sent.push([t, f]) });
  assert.equal(doc.getElementById('save-sleep').className, 'fade', 'a plain fade with Gentle and reduced motion off');
  active = true;
  buttonsOf(doc.getElementById('save-sleep'))[0].click();
  assert.deepEqual(sent, [['nudge', { where: 'save-sleep', want: 'account' }]]);
});

test('the asleep Sky card: New run first, the nudge keeps the card open, placeholders only', () => {
  let spec = null, runs = 0;
  const cards = { open: (s) => { spec = s; return {}; } };
  const sent = [];
  openSkyAsleep({ cards, win: { navigator: { userActivation: { isActive: true } } }, send: (t, f) => sent.push([t, f]), ed: small, newRun: () => { runs += 1; } });
  assert.equal(spec.title, SKY_ASLEEP_COPY.title);
  assert.equal(spec.buttons[0].label, SKY_ASLEEP_COPY.newRun);
  spec.buttons[0].onPress();
  assert.equal(runs, 1, 'New run only from the press');
  assert.equal(spec.buttons[1].keepOpen, true);
  spec.buttons[1].onPress();
  assert.deepEqual(sent, [['nudge', { where: 'ending', want: 'account' }]]);
  for (const copy of [SAVE_SLEEP_COPY, SKY_ASLEEP_COPY]) for (const v of Object.values(copy)) assert.match(v, /^\[PLACEHOLDER S4-/);
});
