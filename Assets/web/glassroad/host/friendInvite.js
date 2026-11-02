/* host/friendInvite.js - "Invite a friend" from the pre-race room through the CCP desktop host (7.2, lane A3).
 *
 * Only a host that says `caps.friendInvite: true` in its init gets this; the web and phone pages never do, so the
 * lobby's own invite view stays hidden there. The host owns the friends list and the send (the CCP friends wire,
 * destination `race` carrying the room id): the page only asks and shows the host's words.
 *   page -> host  friends-list { id }                         host -> page  friends-list-reply { id, friends: [{ uid, name }] }
 *   page -> host  invite-friend { id, friendUid, roomId }     host -> page  invite-friend-reply { id, sent, note }
 * No free text leaves the page: a uid the host gave, a room id the lobby minted. A host that never answers reads as
 * an empty list or a failed send after timeoutMs. Three-free, DOM-free.
 */

export const ROOM_ID = /^[a-z0-9]{12}$/;
const MAX_FRIENDS = 50;
const MAX_NOTE = 120;

/** May this page offer the host's friends invite? */
export const hostInvites = (bridge, init) => !!(bridge && bridge.hosted && init && init.caps && init.caps.friendInvite === true);

/** The host's friends rows, cleaned: string uid and name, at most 50. */
export function cleanFriends(list) {
  if (!Array.isArray(list)) return [];
  return list.filter((f) => f && typeof f.uid === 'string' && f.uid && typeof f.name === 'string' && f.name)
    .slice(0, MAX_FRIENDS).map((f) => ({ uid: f.uid, name: f.name.slice(0, 40) }));
}

/** The host's answer to a send, cleaned: { sent, note }. */
export function cleanInviteReply(m) {
  return { sent: !!(m && m.sent === true), note: m && typeof m.note === 'string' ? m.note.slice(0, MAX_NOTE) : '' };
}

export function createHostInvite(bridge, { timeoutMs = 15000, timers = globalThis } = {}) {
  const wait = new Map();
  let next = 1;
  const settle = (m, clean) => {
    const w = m && wait.get(m.id);
    if (!w) return;
    wait.delete(m.id); timers.clearTimeout(w.t);
    w.res(clean(m));
  };
  bridge.on('friends-list-reply', (m) => settle(m, (r) => cleanFriends(r.friends)));
  bridge.on('invite-friend-reply', (m) => settle(m, cleanInviteReply));
  function ask(msg, fallback) {
    return new Promise((res) => {
      const id = 'fi' + next++;
      const t = timers.setTimeout(() => { wait.delete(id); res(fallback); }, timeoutMs);
      wait.set(id, { res, t });
      bridge.send({ ...msg, id });
    });
  }
  return {
    /** Online friends to ask: [{ uid, name }]. */
    friends: () => ask({ type: 'friends-list' }, []),
    /** One invite into our room: { sent, note } (note = the host's worded result, in the player's language). */
    invite: (friendUid, roomId) => (typeof friendUid === 'string' && friendUid && ROOM_ID.test(roomId || '')
      ? ask({ type: 'invite-friend', friendUid, roomId }, { sent: false, note: '' })
      : Promise.resolve({ sent: false, note: '' })),
  };
}
