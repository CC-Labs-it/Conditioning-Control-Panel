// RACE-LOAD.md fix 2: the open-table poll stops while the page is hidden and backs off (15 s -> 60 s) while the
// answers stay the same; a press, a move, the tab coming back or a changed answer puts it back on 15 s.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { POLL_MS, POLL_MAX_MS, pollEvery, listKey, pollStep, pollReset, pollNow } from '../hud/lobbyMath.js';
import { createLobby, configureLobby } from '../hud/lobby.js';

const row = (o = {}) => ({ roomId: 'abcdefabcdef', track: 'threshold', host: { name: 'Ana' }, seats: 8, filled: 2, laps: 3, staked: false, friend: false, startsIn: 30, ...o });

test('poll math: 15, 15, 30, 60 s and capped; the countdown never counts as a change', () => {
  assert.deepEqual([0, 1, 2, 3, 4, 99].map(pollEvery), [15000, 15000, 30000, 60000, 60000, 60000]);
  assert.equal(pollEvery(undefined), POLL_MS);
  assert.equal(POLL_MAX_MS, 60000);
  const a = listKey({ rows: [row()] });
  assert.equal(listKey({ rows: [row({ startsIn: 12 })] }), a, 'startsIn moves every call');
  assert.notEqual(listKey({ rows: [row({ filled: 3 })] }), a);
  assert.notEqual(listKey({ rows: [row(), row({ roomId: 'zzzzzzzzzzzz' })] }), a);
  assert.equal(listKey({ rows: [] }), listKey(null));
  let p = { key: null, same: 0 };
  p = pollStep(p, a); assert.equal(p.same, 0, 'the first answer is news');
  p = pollStep(p, a); p = pollStep(p, a); assert.equal(p.same, 2);
  assert.equal(pollStep(p, 'other').same, 0, 'a changed answer starts over');
  assert.deepEqual(pollReset(p), { key: a, same: 0 });
  assert.equal(pollNow({ hidden: true, lastAt: -1e9, now: 0, poll: p }), false, 'never while hidden');
  assert.equal(pollNow({ hidden: false, lastAt: 0, now: 29999, poll: p }), false);
  assert.equal(pollNow({ hidden: false, lastAt: 0, now: 30000, poll: p }), true);
  assert.equal(pollNow({ hidden: false, lastAt: 0, now: 15000, poll: null }), true);
});

function fakeDom() {
  let tick = null;
  const listeners = {};
  const win = { setTimeout: () => 0, clearTimeout() {}, setInterval: (fn) => { tick = fn; return 1; }, clearInterval() { tick = null; }, localStorage: null };
  const el = () => ({ hidden: false, textContent: '', innerHTML: '', dataset: {}, classList: { contains: () => false, add() {}, remove() {}, toggle() {} },
    contains: () => false, querySelector: () => null, querySelectorAll: () => [], appendChild() {} });
  const doc = { defaultView: win, activeElement: null, hidden: false, head: el(), body: el(), getElementById: () => ({}), createElement: el,
    addEventListener: (n, fn) => { listeners[n] = fn; }, removeEventListener: (n) => { delete listeners[n]; } };
  const card = el(); card.dataset.kind = 'lobby';
  const parts = { doc, card, title: el(), note: el(), extra: el(), emit() {}, focusFirst() {}, exit() {} };
  return { doc, parts, listeners, tick: () => tick && tick() };
}
const flush = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };

test('the lobby card: no poll while hidden, backs off on a quiet list, any press or the tab coming back resets it', async (t) => {
  let clock = 1_000_000;
  t.mock.method(Date, 'now', () => clock);
  const d = fakeDom();
  let calls = 0, rows = [row(), row({ roomId: 'bbbbbbbbbbbb', track: 'mainframe', host: { name: 'OtherCourse' } })];
  const api = { list: async () => { calls++; return { rows, serverMs: clock }; } };
  const lobby = createLobby(d.parts);
  lobby.use({ api, identity: { displayName: 'Me', tier: 0 }, looks: async () => ({}) });
  lobby.show({ view: 'home' });
  const at = async (ms) => { clock += ms; d.tick(); await flush(); };
  await at(0);
  assert.equal(calls, 1, 'the first list goes at once');
  assert.ok(d.parts.extra.innerHTML.includes('Ana'));
  assert.ok(!d.parts.extra.innerHTML.includes('OtherCourse'), 'foreign-course rooms stay out of the selected-course list');
  await at(15000); assert.equal(calls, 2);
  await at(15000); assert.equal(calls, 3, 'two answers alike: still 15 s');
  await at(15000); assert.equal(calls, 3, 'now 30 s');
  await at(15000); assert.equal(calls, 4);
  assert.match(d.parts.note.textContent, /checked less often/);
  await at(30000); assert.equal(calls, 4, 'now 60 s');
  await at(30000); assert.equal(calls, 5);
  d.doc.hidden = true;
  await at(120000); assert.equal(calls, 5, 'never while the page is hidden');
  d.doc.hidden = false; d.listeners.visibilitychange();
  await at(0); assert.equal(calls, 6, 'the tab coming back polls on the 15 s rule');
  await at(15000); assert.equal(calls, 7);
  await at(15000); assert.equal(calls, 7, 'quiet again: 30 s');
  lobby.nav('down');
  await at(0); assert.equal(calls, 8, 'a move resets the gap to 15 s');
  rows = [row({ filled: 5 })];
  await at(15000); assert.equal(calls, 9);
  await at(15000); assert.equal(calls, 10, 'a changed answer keeps 15 s');
  lobby.dispose();
  assert.equal(d.listeners.visibilitychange, undefined, 'dispose drops the listener');
});

test('course handoff transfers its existing seat without leave/rejoin or late old-course painting',async()=>{
  const d=fakeDom(),events=[],leaves=[],joins=[];d.parts.emit=name=>events.push(name);
  const ticket={roomId:'abcdefabcdef',track:'keep',ticket:'existing-ticket',wsUrl:'ws://local',seat:1};
  const api={leave:async id=>leaves.push(id),join:async()=>{throw Error('must reuse existing ticket');}};
  let transfer,old;
  old=createLobby(d.parts);
  old.use({api,looks:async()=>({}),connect:async()=>{transfer=old.handoff();throw Object.assign(Error('course handoff'),{handoff:true});}});
  old.show({join:ticket});await flush();old.dispose();
  assert.equal(transfer.ticket,ticket.ticket);assert.deepEqual(leaves,[]);assert.deepEqual(events,[]);
  const next=createLobby(d.parts),callbacks={};
  next.use({api,track:'keep',looks:async()=>({}),connect:async join=>{
    joins.push(join);return{on(type,fn){callbacks[type]=fn;return()=>{};},send(){},close(){}};
  }});
  next.show({join:transfer});await flush();
  assert.equal(joins.length,1);assert.equal(joins[0].ticket,ticket.ticket);
  const markup=d.parts.extra.innerHTML;
  old.show({view:'home'});assert.equal(d.parts.extra.innerHTML,markup);
  next.dispose();assert.deepEqual(leaves,[ticket.roomId],'final explicit disposal still releases the owned seat');
  const afterDispose=d.parts.extra.innerHTML;
  callbacks.error({code:'host'});callbacks.finish({rows:[]});callbacks.start({countAt:1,startAt:2,seed:3});
  assert.equal(d.parts.extra.innerHTML,afterDispose);assert.deepEqual(events,[],'queued room callbacks stay silent after disposal');
});

test('a disposed lobby ignores pending table results and cannot repaint a replacement card',async()=>{
  const d=fakeDom();let resolveList;
  const lobby=createLobby(d.parts);
  lobby.use({identity:{displayName:'Me',tier:2},looks:async()=>({}),api:{list:()=>new Promise(resolve=>{resolveList=resolve;})}});
  lobby.show({view:'home'});d.tick();await flush();lobby.dispose();
  d.parts.extra.innerHTML='replacement-course';resolveList({rows:[row()],serverMs:Date.now()});await flush();
  assert.equal(d.parts.extra.innerHTML,'replacement-course');
});

test('a late cancelled seat uses its original course transport after the next course reconfigures the lobby',async t=>{
  const d=fakeDom(),oldLeaves=[],newLeaves=[];let grant;
  configureLobby({api:{join:()=>new Promise(resolve=>{grant=resolve;}),leave:async id=>oldLeaves.push(id)}});
  t.after(()=>configureLobby({api:null}));
  const lobby=createLobby(d.parts);lobby.show({roomId:'abcdefabcdef'});await flush();
  lobby.dispose();configureLobby({api:{leave:async id=>newLeaves.push(id)}});
  grant({roomId:'abcdefabcdef',ticket:'late'});await flush();
  assert.deepEqual(oldLeaves,['abcdefabcdef']);assert.deepEqual(newLeaves,[]);
});
