import { assetURL } from './zones.js';

const takes = new Set(['dome_0','dome_3','dome_5','hoof_3','hoof_8','keep_1','keep_3',
  'log_greg','log_nana','log_reginald','pa_arrive_1','pa_hall1_ten','pa_hall1','pa_leave','pa_shop_3']);

export function createSound(ctx) {
  const controller = new AbortController();
  const rotations = new Map(), layers = new Map(), streams = new Set();
  let dead = false, held = false, bank = {}, room = '', narration = null, depth = 0;
  const resolve = file => assetURL('audio/' + file);
  fetch(resolve('sounds.json'), { signal: controller.signal }).then(response => {
    if (!response.ok) throw new Error('Expo sound library HTTP ' + response.status);
    return response.json();
  }).then(data => {
    if (dead) return;
    bank = data; setRoom(room); setDepth(depth);
    for (const list of Object.values(bank.steps || {})) for (const f of list) ctx.audio.preload(resolve(f));
  }).catch(error => { if (!dead) console.warn(error); });

  function play(id, gain = .58) {
    if (held || dead) return;
    const path = bank.effects?.[id];
    if (path) ctx.audio.play(resolve(path), { bus: 'sfx', gain, signal: controller.signal });
  }
  function footstep(surface) {
    const list = bank.steps?.[surface] || bank.steps?.stone;
    if (held || dead || !list?.length) return;
    const i = rotations.get(surface) || 0;
    rotations.set(surface, (i + 1) % list.length);
    ctx.audio.play(resolve(list[i]), { bus: 'sfx', gain: .4, rate: i % 2 ? .98 : 1.02, signal: controller.signal });
  }
  function layer(key, file, gain) {
    const old = layers.get(key);
    if (old?.file === file && old) { old.handle.setGain(gain); return; }
    if (old) { old.handle.stop(held ? 0 : 350); streams.delete(old.handle); layers.delete(key); }
    if (!file || dead) return;
    const handle = ctx.audio.stream(resolve(file), { bus: 'ambient', loop: true, gain, autoplay: !held });
    layers.set(key, { file, handle }); streams.add(handle);
  }
  function setRoom(id) {
    room = id;
    layer('room', bank.tones?.[id], .46);
    layer('band', id === 'atrium' && bank.effects?.band, .22);
    if (id !== 'dome') setDepth(0);
  }
  function setDepth(n) {
    depth = Math.max(0, Math.min(1, n));
    for (const [name, start] of [['note', .08], ['fog', .45], ['dream', .7]]) {
      const gain = Math.max(0, Math.min(1, (depth - start) / .2));
      layer(name, gain > 0 ? bank.effects?.[name] : null, gain * .28);
    }
  }
  function speak(id) {
    narration?.stop(); streams.delete(narration); narration = null;
    if (!takes.has(id) || held || dead) return;
    const handle = narration = ctx.audio.stream(assetURL('vo/' + id + '.mp3'), { bus: 'voice', gain: .82 });
    streams.add(handle);
    const end = () => { handle.stop(); streams.delete(handle); if (handle === narration) narration = null; };
    handle.el?.addEventListener('ended', end, { once: true });
  }
  function pause(on) {
    held = on;
    for (const s of streams) { if (on) s.pause(); else s.play(); }
  }
  return { play, footstep, setRoom, setDepth, speak, pause, dispose() {
    dead = true; controller.abort(); for (const s of streams) s.stop(); streams.clear(); layers.clear();
  } };
}
