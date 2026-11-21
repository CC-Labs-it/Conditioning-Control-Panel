import { assetURL } from './zones.js';

export function createSound(ctx) {
  const controller = new AbortController();
  const rotations = new Map(), layers = new Map(), streams = new Set();
  let dead = false, held = false, bank = {}, room = '', depth = 0;
  const meadowFootfall='assets/audio/sfx/thud.mp3';
  let footfallCount=0,lastFootfall=null;
  const resolve = file => assetURL('audio/' + file);
  fetch(resolve('sounds.json'), { signal: controller.signal }).then(response => {
    if (!response.ok) throw new Error('Expo sound library HTTP ' + response.status);
    return response.json();
  }).then(data => {
    if (dead) return;
    bank = data; setRoom(room); setDepth(depth);
    if(bank.effects?.press)ctx.audio.preload(resolve(bank.effects.press));
    ctx.audio.preload(meadowFootfall);
    for (const list of Object.values(bank.steps || {})) for (const f of list) ctx.audio.preload(resolve(f));
  }).catch(error => { if (!dead) console.warn(error); });

  function play(id, gain = .58, pan = 0) {
    if (held || dead) return;
    const path = bank.effects?.[id];
    if (path) ctx.audio.play(resolve(path), { bus: 'sfx', gain, pan, signal: controller.signal });
  }
  function footstep(surface) {
    const list = bank.steps?.[surface] || bank.steps?.stone;
    if (held || dead || !list?.length) return;
    const i = rotations.get(surface) || 0;
    rotations.set(surface, (i + 1) % list.length);
    const phase=footfallCount++%4,pan=phase%2?.08:-.08;
    const voice={bus:'sfx',source:'expo-footsteps',sourceLabel:'Footsteps',signal:controller.signal,pan};
    ctx.audio.play(meadowFootfall,{...voice,gain:surface==='carpet' ? .29 : .37,rate:[.70,.76,.67,.73][phase]});
    ctx.audio.play(resolve(list[i]),{...voice,gain:surface==='carpet' ? .34 : .49,rate:phase%2 ? .98 : 1.02});
    lastFootfall={surface,body:meadowFootfall,sole:list[i]};
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
    const sharedTone={chronology:'hall',forum:'atrium',commons:'hall',aquarium:'habitat'}[id];
    layer('room', bank.tones?.[id]||bank.tones?.[sharedTone], sharedTone ? .34 : .46);
    layer('band', id === 'atrium' && bank.effects?.band, .22);
    if (id !== 'service') layer('server', null, 0);
    if (id !== 'dome') setDepth(0);
  }
  function setListener(feet) {
    if (room !== 'service') return;
    const distance = Math.hypot(feet.x - 25, feet.z - 27.19, feet.y + 1.6 - 1.04);
    const gain = Math.max(0, 1 - distance / 4.1);
    layer('server', gain > 0 && bank.tones?.server, .47 * gain * gain);
  }
  function setDepth(n) {
    depth = Math.max(0, Math.min(1, n));
    for (const [name, start] of [['note', .1], ['fog', .5], ['dream', .75]]) {
      const gain = Math.max(0, Math.min(1, (depth - start) / .2));
      layer(name, gain > 0 ? bank.effects?.[name] : null, gain * .28);
    }
  }
  function pause(on) {
    held = on;
    for (const s of streams) { if (on) s.pause(); else s.play(); }
  }
  return { play, footstep, setRoom, setDepth, setListener, pause,footfalls:()=>({count:footfallCount,last:lastFootfall}), dispose() {
    dead = true; controller.abort(); for (const s of streams) s.stop(); streams.clear(); layers.clear();
  } };
}
