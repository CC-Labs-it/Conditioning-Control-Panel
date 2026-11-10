import { needsDomMedia } from '../unlocks/arcade/slot-reel-media.js';

/** Every exhibit uses a display-only DOM node, including same-origin and own-file pictures. */
export function createMedia(ctx, { lite = false } = {}) {
  const pictures = ctx.pictures;
  const slots = new Set();
  const clipSlots = new Set();
  const maxClips = lite ? 1 : 2;
  let paused = false;
  let disposed = false;

  function sync(slot) {
    const player = slot.player;
    if (!player || player.tagName !== 'VIDEO') return;
    const moving = !paused && !ctx.paused && !ctx.gentle && !ctx.reduced && !slot.layer.ownerDocument.hidden;
    if (moving && slot.ready) player.play().catch(() => {});
    else player.pause();
  }

  function clear(slot) {
    slot.ticket = null;
    slot.ready = false;
    clipSlots.delete(slot);
    const player = slot.player;
    if (player) {
      player.onload = player.onloadeddata = player.onerror = null;
      if (player.tagName === 'VIDEO') player.pause();
      player.removeAttribute('src');
      if (player.tagName === 'VIDEO') player.load();
    }
    slot.layer.replaceChildren();
    slot.layer.hidden = true;
    slot.lease?.release?.();
    slot.lease = null;
    slot.player = null;
  }

  async function fill(slot) {
    if (!pictures || pictures.source === 'none' || slot.ticket || slot.player) return;
    const source = pictures.source;
    const ticket = {};
    slot.ticket = ticket;
    const current = () => slot.ticket === ticket && pictures.source === source && slots.has(slot);
    const playClip = slot.exhibit.kind === 'run-clip' && !ctx.reduced && !ctx.gentle && clipSlots.size < maxClips;
    if (playClip) clipSlots.add(slot);
    try {
      let row = playClip ? await pictures.next?.({ kind: 'clip', allowScrolller: true }) : null;
      if (!current()) return;
      // A GIF in an img cannot be paused by Wake Up. Deal a still when there is no video.
      if (!row || row.kind === 'gif') {
        clipSlots.delete(slot);
        row = pictures.dealStill?.({ group: 'documentary', allowScrolller: true }) ||
          await pictures.next?.({ still: true, allowScrolller: true });
      }
      if (!current() || !row?.url || row.kind === 'gif' || (!playClip && row.kind === 'clip')) return;
      const clip = row.kind === 'clip';
      if (clip) {
        if (!clipSlots.has(slot) && clipSlots.size >= maxClips) return;
        clipSlots.add(slot);
      } else clipSlots.delete(slot);
      const lease = await pictures.acquire?.(row);
      if (!current()) { lease?.release?.(); return; }
      slot.lease = lease;
      const doc = slot.layer.ownerDocument;
      const player = doc.createElement(clip ? 'video' : 'img');
      slot.player = player;
      player.className = 'td-run-picture';
      if (source === 'scrolller' || needsDomMedia(row.url)) {
        player.referrerPolicy = 'strict-origin';
        player.setAttribute('referrerpolicy', 'strict-origin');
      }
      if (clip) {
        player.muted = player.defaultMuted = player.loop = player.playsInline = true;
        player.setAttribute('playsinline', '');
        player.preload = 'auto';
        player.disablePictureInPicture = true;
      } else {
        player.alt = '';
        player.draggable = false;
        player.decoding = 'async';
      }
      slot.layer.append(player);
      const attribution = row.credit || (source === 'scrolller' ? 'via Scrolller' : '');
      if (attribution) {
        const credit = doc.createElement('span');
        credit.className = 'td-run-credit';
        credit.textContent = attribution;
        slot.layer.append(credit);
      }
      const reveal = () => {
        if (!current() || slot.ready) return;
        slot.ready = true;
        slot.layer.hidden = false;
        sync(slot);
        pictures.noteView?.();
      };
      player.onerror = () => { if (current()) clear(slot); };
      if (clip) player.onloadeddata = reveal;
      else player.onload = reveal;
      player.src = lease?.url || row.url;
      if (!clip && player.decode) player.decode().then(reveal, () => {});
    } catch {
      if (current()) clear(slot);
    } finally {
      if (slot.ticket === ticket && !slot.player) {
        slot.ticket = null;
        clipSlots.delete(slot);
      }
    }
  }

  const offSource = ctx.on?.('pictures', () => {
    for (const slot of slots) clear(slot);
    for (const slot of slots) fill(slot);
  });
  const offAdded = ctx.on?.('pictures-added', () => { for (const slot of slots) fill(slot); });
  function unmount(slot) {
    if (!slots.delete(slot)) return;
    clear(slot);
    slot.layer.remove();
  }
  return {
    mount(container, exhibit) {
      if (disposed) return () => {};
      const layer = container.ownerDocument.createElement('div');
      layer.className = 'td-run-media';
      layer.hidden = true;
      container.append(layer);
      const slot = { layer, exhibit, player: null, lease: null, ticket: null, ready: false };
      slots.add(slot);
      fill(slot);
      return () => unmount(slot);
    },
    pause(on) {
      paused = Boolean(on);
      for (const slot of slots) sync(slot);
    },
    dispose() {
      disposed = true;
      offSource?.();
      offAdded?.();
      for (const slot of slots) unmount(slot);
    },
  };
}
