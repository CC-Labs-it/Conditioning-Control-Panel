const audioRoot = new URL('../../assets/expo/audio/', import.meta.url);
const EASE = x => x * x * (3 - 2 * x);
const clamp = x => Math.min(1, Math.max(0, x));

// The same eyelids and sound route stay alive from the YES button through the first museum frame.
export function createPortal(ctx, parent) {
  const doc = parent.ownerDocument;
  const calm = () => ctx.gentle || ctx.reduced;
  const curtain = doc.createElement('div'), lids = doc.createElement('div');
  curtain.className = 'expo-crossing'; lids.className = 'expo-eyelids';
  curtain.setAttribute('aria-hidden','true'); curtain.append(lids);
  let disposed = false, arrived = false, entered = false, input, lift, tail;
  let mode = calm() ? 'gentle' : 'standard';
  const bank = fetch(new URL('sounds.json', audioRoot)).then(r => r.ok ? r.json() : null)
    .then(data => data?.portal).catch(() => null);
  const profiles = new Map();
  function buffers(profile) {
    if (!profiles.has(profile)) profiles.set(profile, bank.then(async plan => {
      if (!plan || disposed) return null;
      const files = plan.profiles[profile];
      const [rise, arrival] = await Promise.all([files.rise, files.arrival].map(
        file => ctx.audio.preload(new URL(file, audioRoot).href)));
      return { plan, rise, arrival };
    }).catch(() => null));
    return profiles.get(profile);
  }
  void buffers(mode);
  function source(buffer) {
    const ac = input.ac, node = ac.createBufferSource(), gain = ac.createGain();
    node.buffer = buffer; node.connect(gain); gain.connect(input.node);
    node.onended = () => { node.disconnect(); gain.disconnect(); };
    return { node, gain: gain.gain };
  }
  function begin() {
    if (entered || disposed) return;
    entered = true; parent.append(curtain); mode = calm() ? 'gentle' : 'standard';
    input = ctx.audio.input({bus:'sfx',gain:.95});
    void buffers(mode).then(clips => {
      if (disposed || arrived || !input || !clips?.rise) return;
      lift = source(clips.rise);
      lift.node.loop = true; lift.node.loopStart = clips.plan.holdStartSeconds;
      lift.node.loopEnd = clips.plan.holdEndSeconds; lift.node.start();
    });
  }
  function cover(amount) {
    if (disposed) return;
    const p = clamp(amount);
    // A broad, curved slit closes to a single seam. The full wash meets the loading cover exactly.
    curtain.style.backgroundColor = 'rgba(7,24,25,' + (calm() ? p : p*p*p) + ')';
    lids.style.opacity = calm() ? '0' : String(Math.min(1,p*4));
    lids.style.setProperty('--opening', (Math.max(.03, 156 * (1-p)**1.6)) + '%');
    lids.style.setProperty('--seam',String(calm() ? 0 : clamp((p-.72)/.28)*.64));
    curtain.dataset.cover = p.toFixed(3);
  }
  function arrive() {
    if (disposed || arrived) return;
    arrived = true;
    void buffers(mode).then(clips => {
      if (disposed || !input || !clips) return;
      const t = input.ac.currentTime, blend = clips.plan.crossfadeSeconds;
      if (lift) {
        lift.gain.setValueAtTime(1,t); lift.gain.linearRampToValueAtTime(0,t+blend);
        lift.node.stop(t+blend);
      }
      if (clips.arrival) {
        tail = source(clips.arrival); tail.gain.setValueAtTime(0,t);
        tail.gain.linearRampToValueAtTime(1,t+blend); tail.node.start(t);
      }
    });
  }
  function dispose() {
    if (disposed) return;
    disposed = true; curtain.remove();
    for (const voice of [lift,tail]) if (voice) {
      try { voice.node.stop(); } catch { /* already ended */ }
    }
    input?.stop();
  }
  return { begin, cover, arrive, dispose, reveal(seconds) {
    const p = clamp(seconds/(calm() ? .46 : .67));
    cover(1 - EASE(p));
    if (p >= 1) curtain.remove();
  } };
}
