import test from 'node:test';
import assert from 'node:assert/strict';
import { hostInvites, cleanFriends, cleanInviteReply, createHostInvite } from '../host/friendInvite.js';

function fakeBridge() {
  const handlers = new Map(), sent = [];
  return {
    hosted: true, sent,
    on(type, fn) { handlers.set(type, fn); return () => handlers.delete(type); },
    send(m) { sent.push(m); },
    emit(type, m) { handlers.get(type)?.(m); },
  };
}
const timers = { setTimeout: (fn, ms) => ({ fn, ms }), clearTimeout() {} };

test('only a hosted page whose host sends caps.friendInvite gets the host invite', () => {
  const b = fakeBridge();
  assert.equal(hostInvites(b, { caps: { friendInvite: true } }), true);
  assert.equal(hostInvites(b, { caps: { friendInvite: 'yes' } }), false);
  assert.equal(hostInvites(b, { caps: {} }), false);
  assert.equal(hostInvites({ ...b, hosted: false }, { caps: { friendInvite: true } }), false);
  assert.equal(hostInvites(null, null), false);
});

test('friends rows keep a string uid and name, at most 50', () => {
  const rows = cleanFriends([{ uid: 'u_a', name: 'Ann', extra: 1 }, { uid: '', name: 'X' }, { uid: 'u_b' }, null,
    ...Array.from({ length: 60 }, (_, i) => ({ uid: 'u' + i, name: 'N' + i }))]);
  assert.equal(rows.length, 50);
  assert.deepEqual(rows[0], { uid: 'u_a', name: 'Ann' });
  assert.deepEqual(cleanFriends('nope'), []);
});

test('an invite reply is { sent, note } and nothing else', () => {
  assert.deepEqual(cleanInviteReply({ sent: true, note: 'Sent', html: '<b>' }), { sent: true, note: 'Sent' });
  assert.deepEqual(cleanInviteReply({ sent: 'true', note: 5 }), { sent: false, note: '' });
  assert.equal(cleanInviteReply({ note: 'x'.repeat(500) }).note.length, 120);
});

test('the friends list and an invite go through the host and come back by id', async () => {
  const b = fakeBridge(), hi = createHostInvite(b, { timers });
  const list = hi.friends();
  assert.equal(b.sent[0].type, 'friends-list');
  b.emit('friends-list-reply', { id: b.sent[0].id, friends: [{ uid: 'u_a', name: 'Ann' }] });
  assert.deepEqual(await list, [{ uid: 'u_a', name: 'Ann' }]);

  const inv = hi.invite('u_a', 'abc123def456');
  assert.deepEqual(b.sent[1], { type: 'invite-friend', friendUid: 'u_a', roomId: 'abc123def456', id: b.sent[1].id });
  b.emit('invite-friend-reply', { id: 'someone-else', sent: true, note: 'x' });
  b.emit('invite-friend-reply', { id: b.sent[1].id, sent: false, note: 'They need Prime for online races' });
  assert.deepEqual(await inv, { sent: false, note: 'They need Prime for online races' });
});

test('a malformed room never reaches the host', async () => {
  const b = fakeBridge(), hi = createHostInvite(b, { timers });
  assert.deepEqual(await hi.invite('u_a', 'ABC123DEF456'), { sent: false, note: '' });
  assert.deepEqual(await hi.invite('', 'abc123def456'), { sent: false, note: '' });
  assert.equal(b.sent.length, 0);
});

test('a host that never answers reads as an empty list', async () => {
  const b = fakeBridge(), pending = [];
  const hi = createHostInvite(b, { timers: { setTimeout: (fn) => { pending.push(fn); return 1; }, clearTimeout() {} } });
  const list = hi.friends();
  pending[0]();
  assert.deepEqual(await list, []);
});
