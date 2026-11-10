const SLIDE_MS = 220;
const SOURCE = { bus: 'sfx', source: 'documentary-projectors', sourceLabel: 'Museum projectors' };

// Short original mechanical textures. All output uses the host mixer input.
function mechanicalBuffer(ac, kind) {
  const seconds = kind === 'motor' ? 1.08 : .22;
  const rate = ac.sampleRate || 44100;
  const buffer = ac.createBuffer(1, Math.ceil(seconds * rate), rate);
  const samples = buffer.getChannelData(0);
  let seed = 17471, low = 0, phase = 0;
  for (let i = 0; i < samples.length; i++) {
    const t = i / rate, u = t / seconds;
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
    const noise = seed / 2147483648 - 1;
    low += .19 * (noise - low);
    const envelope = Math.min(1, t / .006, (seconds - t) / .055);
    let value;
    if (kind === 'motor') {
      phase += 2 * Math.PI * (142 + 43 * Math.sin(Math.PI * u)) / rate;
      const turning = Math.sin(phase) * .13 + Math.sin(phase * 2) * .055 + Math.sin(phase * 5) * .018;
      const friction = low * .15 * (.78 + .22 * Math.sin(t * 2 * Math.PI * 24));
      const engage = low * .25 * Math.exp(-t * 72);
      const stop = t > .96 ? low * .25 * Math.exp(-(t - .96) * 60) : 0;
      value = (turning + friction + engage + stop) * Math.min(1, t / .055, (seconds - t) / .09);
    } else {
      const click = low * .8 * Math.exp(-t * 125);
      const latch = t > .058 ? low * .55 * Math.exp(-(t - .058) * 105) : 0;
      const gear = Math.sin(2 * Math.PI * (510 * t - 380 * t * t)) * .055 * Math.exp(-t * 19);
      value = click + latch + gear;
    }
    samples[i] = Math.max(-.5, Math.min(.5, value * Math.max(0, envelope)));
  }
  return buffer;
}

/** Finite projector transitions and mechanical cues. No frame loop or audio context is owned here. */
export function createProjectorEffects(ctx, options = {}) {
  const doc = options.document || globalThis.document;
  const cleanups = [], sounds = new Map(), transitions = new Map(), unrolled = new Set();
  const buffers = new WeakMap();
  let disposed = false, held = false, lastSlideKey = null, soundCount = 0, transitionCount = 0;
  const paused = () => held || ctx.paused || Boolean(doc?.hidden);

  function stopSound(kind) {
    const job = sounds.get(kind);
    if (!job) return;
    sounds.delete(kind);
    job.source.onended = null;
    try { job.source.stop(); } catch { /* It may already have ended. */ }
    job.source.disconnect?.(); job.input.stop?.();
  }
  function sound(kind) {
    if (disposed || paused()) return false;
    let input;
    try { input = ctx.audio?.input?.({ ...SOURCE, gain: ctx.gentle || ctx.reduced ? .3 : .55 }); }
    catch { return false; }
    if (!input?.ac || !input.node) { input?.stop?.(); return false; }
    const ac = input.ac;
    // A suspended context must not collect cues that all play on a later gesture.
    if (ac.state && ac.state !== 'running') { input.stop?.(); return false; }
    stopSound(kind);
    let source;
    try {
      let bank = buffers.get(ac);
      if (!bank) { bank = new Map(); buffers.set(ac, bank); }
      if (!bank.has(kind)) bank.set(kind, mechanicalBuffer(ac, kind));
      source = ac.createBufferSource();
      source.buffer = bank.get(kind); source.loop = false;
      source.connect(input.node);
      const job = { source, input };
      sounds.set(kind, job);
      source.onended = () => {
        if (sounds.get(kind) !== job) return;
        sounds.delete(kind); source.disconnect?.(); input.stop?.();
      };
      source.start(ac.currentTime);
      source.stop(ac.currentTime + source.buffer.duration);
      soundCount++;
      return true;
    } catch {
      if (sounds.get(kind)?.source === source) stopSound(kind);
      else { source?.disconnect?.(); input.stop?.(); }
      return false;
    }
  }
  function slide(key) {
    if (disposed || paused() || (key != null && key === lastSlideKey)) return false;
    lastSlideKey = key ?? null;
    return sound('slide');
  }
  function unroll(id) {
    if (disposed || paused() || unrolled.has(id)) return false;
    unrolled.add(id);
    return sound('motor');
  }

  function finishTransition(job) {
    if (transitions.get(job.incoming) !== job) return;
    transitions.delete(job.incoming);
    for (const animation of job.animations) animation.cancel?.();
    job.outgoing?.remove?.();
  }
  function transition(incoming, { outgoing = null, key, initial = false, visible = true } = {}) {
    if (!incoming) { outgoing?.remove?.(); return null; }
    // A newly decoded slide replaces any in-flight transition on this surface.
    for (const job of [...transitions.values()]) {
      if (job.incoming === incoming || job.incoming === outgoing || job.incoming.parentNode === incoming.parentNode) finishTransition(job);
    }
    if (disposed || initial || !visible || ctx.reduced) {
      outgoing?.remove?.();
      if (!disposed && !initial && visible) slide(key);
      return null;
    }
    const animate = ctx.anim ? (node, frames, settings) => ctx.anim(node, frames, settings) : (node, frames, settings) => node.animate?.(frames, settings);
    const job = { incoming, outgoing, animations: [] };
    transitions.set(incoming, job);
    const settings = { duration: SLIDE_MS, easing: 'cubic-bezier(.22,.61,.36,1)', fill: 'both' };
    const entering = ctx.gentle ? [{ opacity: 0 }, { opacity: 1 }] : [
      { opacity: 0, transform: 'translateX(10px)' }, { opacity: 1, transform: 'translateX(0)' },
    ];
    const leaving = ctx.gentle ? [{ opacity: 1 }, { opacity: 0 }] : [
      { opacity: 1, transform: 'translateX(0)' }, { opacity: 0, transform: 'translateX(-6px)' },
    ];
    try {
      const enter = animate(incoming, entering, settings);
      if (enter) job.animations.push(enter);
      if (outgoing) {
        const leave = animate(outgoing, leaving, settings);
        if (leave) job.animations.push(leave);
      }
      if (!job.animations.length) { finishTransition(job); return null; }
      if (paused()) for (const animation of job.animations) animation.pause?.();
      Promise.all(job.animations.map(animation => animation.finished)).then(
        () => finishTransition(job), () => finishTransition(job),
      );
      transitionCount++;
      slide(key);
      return { cancel: () => finishTransition(job) };
    } catch { finishTransition(job); return null; }
  }
  function sync() {
    if (disposed) return;
    if (paused()) for (const kind of [...sounds.keys()]) stopSound(kind);
    for (const job of transitions.values()) for (const animation of job.animations) {
      if (paused()) animation.pause?.(); else animation.play?.();
    }
  }
  function pause(on) { held = Boolean(on); sync(); }
  function dispose() {
    if (disposed) return;
    disposed = true;
    for (const kind of [...sounds.keys()]) stopSound(kind);
    for (const job of [...transitions.values()]) finishTransition(job);
    unrolled.clear();
    for (const off of cleanups) off?.();
    doc?.removeEventListener?.('visibilitychange', sync);
  }
  cleanups.push(ctx.on?.('pause', () => pause(true)), ctx.on?.('resume', () => pause(false)));
  cleanups.push(ctx.on?.('suspend', event => {
    if (event?.on && event.reason === 'panic') dispose();
    else pause(Boolean(event?.on));
  }));
  doc?.addEventListener?.('visibilitychange', sync);
  ctx.cleanup?.(dispose);
  return Object.freeze({
    transition, slide, unroll, pause, dispose,
    resetUnroll(id) { unrolled.delete(id); },
    debug: () => ({ disposed, sounds: sounds.size, transitions: transitions.size, soundCount, transitionCount, unrolled: unrolled.size }),
  });
}
