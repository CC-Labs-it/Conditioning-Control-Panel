// THE LLAMA HOLE (owner 2026-11-24): the full edition on the web has no Expo (the host sends no caps.expo there), so a full
// web run must never reach the Llama wormhole, a door that cannot open. It gets the small edition's treatment: the Llama
// set (The Panel, Llama Expo 2037) is never offered and the llama-8 card says where it lives. The desktop is unchanged.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import * as Ed from '../ui/edition.js';
import * as E from '../sim/economy.js';
import { BY_ID } from '../data/items.js';
import { SMALL_CUT } from '../data/editions.js';
import { llama8Due, autoCardDue } from '../ui/transmission.js';

const HOSTED = { hostname: 'tc.cclabs.app', hosted: true };
const fullWeb = Ed.readEdition({ caps: { edition: 'full', platform: 'web' }, identity: { id: 'x' } }, HOSTED);
const fullWebAsksExpo = Ed.readEdition({ caps: { edition: 'full', platform: 'web', expo: { state: 'ready', base: 'https://x.example/expo/' } } }, HOSTED);
const deskAbsent = Ed.readEdition({ caps: { edition: 'full', platform: 'desktop', expo: { state: 'absent', mb: 300 } } }, HOSTED);
const deskReady = Ed.readEdition({ caps: { edition: 'full', platform: 'desktop', expo: { state: 'ready', base: 'https://x.example/expo/' } } }, HOSTED);
const deskBare = Ed.readEdition({ caps: { edition: 'full', platform: 'desktop' } }, HOSTED);
const localWhole = Ed.readEdition(null, { hostname: '127.0.0.1' });

/** A full pen, rich, theme picked, past the second taste: every open fate seed is on offer. */
function run(ed) {
  const s = E.createRun({ herd: 12, ed });
  s.fuzz = 1e12; s.total = 1e12;
  E.grant(s, 'f4'); s.picks.f4 = 'sell';
  s.picks.f5 = 'trance';
  E.grant(s, 'taste2');
  return s;
}
const offered = (s) => E.openItems(s).map((it) => it.id);
/** Pick the Llama set and finish the main path: the run then holds and offers its ending, where the set can play. */
function atTheEnd(s) {
  E.grant(s, 'ft_panel');
  for (const q of E.needsOf(s, BY_ID.drop).req) E.grant(s, q);
  return s;
}

test('the hole: a full web reading only; the desktop and a local page are never holed', () => {
  assert.equal(Ed.expoHole(fullWeb), true);
  assert.equal(Ed.expoHole(fullWebAsksExpo), true, 'only the desktop host may name the Expo');
  for (const ed of [deskAbsent, deskReady, deskBare, localWhole]) assert.equal(Ed.expoHole(ed), false);
  const small = Ed.readEdition({ caps: { edition: 'small', platform: 'web' } }, HOSTED);
  assert.equal(Ed.expoHole(small), false, 'small has its own rule, already cutting the set');
  for (const id of SMALL_CUT.expo) { assert.equal(Ed.isCut(id, fullWeb), true, id); assert.equal(Ed.isCut(id, deskAbsent), false, id); }
  assert.equal(Ed.isCut('ft_window', fullWeb), false, 'nothing else is cut in full');
  assert.ok(!Ed.reqFor('overlay', fullWeb).any.includes('ft_panel'));
  assert.deepEqual(Ed.reqFor('overlay', deskAbsent).any, BY_ID.overlay.any);
});

test('a full web run without caps.expo never offers ft_panel or ll_end; a full desktop run still does', () => {
  for (const ed of [deskAbsent, deskReady, deskBare]) {
    const s = run(ed);
    assert.ok(offered(s).includes('ft_panel'), 'desktop: The Panel is offered to a full pen');
    atTheEnd(s);
    assert.equal(E.heldEnding(s), 'll_end');
    assert.ok(offered(s).includes('ll_end'), 'desktop: the Llama ending is offered');
    assert.equal(E.runEdition(s), null, 'a full desktop run still carries no reading');
  }
  const s = run(fullWeb);
  assert.equal(E.runEdition(s), null, 'runEdition still means small only');
  assert.ok(!offered(s).includes('ft_panel'));
  assert.ok(offered(s).includes('ft_window'), 'the other seeds are offered as before');
  atTheEnd(s);
  assert.equal(E.has(s, 'ft_panel'), false, 'never granted either');
  assert.notEqual(E.heldEnding(s), 'll_end');
  assert.ok(!offered(s).includes('ll_end') && !offered(s).includes('ft_panel'));
  /* A save that already owns the set (carried from the desktop) keeps it, asleep, like small. */
  const carried = run(fullWeb);
  carried.owned.ft_panel = 1; carried.order.push('ft_panel');
  assert.equal(E.sleeps(carried, 'ft_panel'), true);
  assert.equal(E.fateOf(carried), null, 'its seed picks nothing here');
});

test('the llama-8 card: shown in the hole as in small, never on the desktop', () => {
  assert.equal(llama8Due(8, {}, fullWeb), true);
  assert.equal(llama8Due(7, {}, fullWeb), false);
  assert.equal(llama8Due(8, { llama8Seen: 1 }, fullWeb), false);
  for (const ed of [deskAbsent, deskReady, deskBare, localWhole]) assert.equal(llama8Due(12, {}, ed), false);
  assert.equal(autoCardDue({ clock: 1e9, llamas: 8, extras: {}, ed: fullWeb }), 'llama8');
  assert.equal(Ed.nudgeFrame('llama8', fullWeb).want, 'download', 'a signed-in web player is pointed at the app');
});
