/* The ending and pause doors (lane S7a, CONTRACT.md 6.2): each sends its nudge only from a real press, the pause line is
 * mounted once, and the ending card shows its door only when main.js hands the hook (small). */
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import * as Ed from '../ui/edition.js';
import { NUDGE_COPY, endingNudge, mountPauseNudge } from '../ui/nudges.js';

const small = Ed.readEdition({ caps: { edition: 'small', platform: 'web' } }, { hostname: 'tc.cclabs.app', hosted: true });

function fakeDoc() {
  const byId = new Map();
  const make = (tag) => {
    const e = { tag, children: [], listeners: {}, textContent: '', className: '',
      set id(v) { this._id = v; byId.set(v, this); }, get id() { return this._id; },
      appendChild(c) { this.children.push(c); return c; }, addEventListener(t, f) { (this.listeners[t] ||= []).push(f); },
      click() { for (const f of this.listeners.click || []) f({}); } };
    return e;
  };
  const card = make('div');
  return { card, createElement: make, getElementById: (id) => byId.get(id) || null, querySelector: (q) => (q === '#pause .card' ? card : null) };
}

test('the pause line: once, its nudge only from a real press', () => {
  const doc = fakeDoc(), sent = [];
  let active = false;
  const win = { navigator: { userActivation: { get isActive() { return active; } } } };
  const line = mountPauseNudge({ doc, win, send: (t, f) => sent.push([t, f]), ed: small });
  assert.ok(line);
  assert.equal(mountPauseNudge({ doc, win, ed: small }), null, 'once per card');
  assert.equal(doc.card.children.length, 1);
  assert.equal(line.textContent, NUDGE_COPY.pauseLine + ' ');
  const [go] = line.children;
  assert.equal(go.textContent, NUDGE_COPY.pauseButton);
  go.click();
  assert.deepEqual(sent, [], 'a script click is not a press');
  active = true;
  go.click();
  assert.deepEqual(sent, [['nudge', { where: 'pause', want: 'account' }]]);
});

test('the ending door: a hook that nudges only from a real press', () => {
  const sent = [];
  const win = { navigator: { userActivation: { isActive: false } } };
  const go = endingNudge({ win, send: (t, f) => sent.push([t, f]), ed: small });
  assert.equal(go(), false);
  win.navigator.userActivation.isActive = true;
  assert.equal(go(), true);
  assert.deepEqual(sent, [['nudge', { where: 'ending', want: 'account' }]]);
});

test('wiring: small only, through ui/edition.js, never by itself', () => {
  const main = readFileSync(new URL('../main.js', import.meta.url), 'utf8');
  assert.match(main, /nudge: isSmall\(ed\) \? endingNudge\(/);
  assert.match(main, /if \(isSmall\(ed\)\) mountPauseNudge\(/);
  const recap = readFileSync(new URL('../ui/recap/recap.js', import.meta.url), 'utf8');
  assert.match(recap, /if \(hooks\.nudge\) \{/);
  assert.match(recap, /addEventListener\('click', \(ev\) => hooks\.nudge\(ev\)\)/, 'the ending door acts only on a click, with its event');
  const nudges = readFileSync(new URL('../ui/nudges.js', import.meta.url), 'utf8');
  assert.doesNotMatch(nudges, /caps\??\.edition|isSmall\(/, 'nothing here reads the edition');
});
