const MUSIC_ROOT = new URL('../../assets/lofi/audio/', import.meta.url);
export const MUSEUM_TRACKS = Object.freeze([
  { id: 'breathe-in', title: 'Breathe In', duration: 204.36 },
  { id: 'idle-cycle', title: 'Idle Cycle', duration: 157.72 },
  { id: 'sealed', title: 'Sealed', duration: 173.52 },
]);

/** One quiet stream for the entire visit, including the parked ending. */
export function createDocumentaryMusic(ctx, tunnel, options = {}) {
  const tracks = options.tracks || MUSEUM_TRACKS;
  const doc = options.document || tunnel.captions?.ownerDocument || globalThis.document;
  const now = options.now || (() => performance.now());
  const setTimer = options.setTimer || ((fn, ms) => setTimeout(fn, ms));
  const clearTimer = options.clearTimer || (id => clearTimeout(id));
  const cleanups = [], failed = new Set();
  let current = null, index = 0, disposed = false, started = false, held = false, speaking = false;
  let gain = .44, lastAt = now(), envelopeTimer = null, lastGain = 0;
  const paused = () => held || ctx.paused || (!tunnel.state.docked && doc?.hidden);
  const target = () => speaking ? .14 : .44;
  function cancelEnvelope() { clearTimer(envelopeTimer); envelopeTimer = null; }
  function updateGain() {
    cancelEnvelope();
    if (!current || disposed) return;
    const at = now(), dt = Math.max(0, at - lastAt) / 1000;
    lastAt = at;
    gain += (target() - gain) * (1 - Math.exp(-dt / (speaking ? .16 : .55)));
    const player = current.source.el;
    const time = Math.max(0, Number(player.currentTime) || 0);
    const duration = Number.isFinite(player.duration) && player.duration > 0 ? player.duration : current.track.duration;
    const envelope = Math.max(0, Math.min(1, time / 1.4, (duration - time) / 2.2));
    lastGain = gain * envelope;
    current.source.setGain?.(lastGain);
    if (!paused() && !player.paused && (envelope < .999 || Math.abs(gain - target()) > .001)) {
      envelopeTimer = setTimer(updateGain, 50);
    }
  }
  function clearCurrent() {
    cancelEnvelope();
    if (!current) return;
    const old = current; current = null;
    for (const [event, listener] of old.listeners) old.source.el?.removeEventListener(event, listener);
    old.source.stop?.();
  }
  function play() {
    if (!current || disposed || paused()) return;
    lastAt = now();
    try { current.source.play()?.catch?.(() => {}); } catch { /* A later gesture can retry playback. */ }
  }
  function startTrack(next) {
    clearCurrent();
    if (disposed || !tracks.length || failed.size >= tracks.length) return;
    index = ((next % tracks.length) + tracks.length) % tracks.length;
    if (failed.has(index)) { startTrack(index + 1); return; }
    const track = tracks[index];
    let source;
    try {
      const settings = { bus: 'podcast', source: 'documentary-music', sourceLabel: 'Museum music', gain: 0, autoplay: false, preload: 'metadata' };
      source = (options.createStream || ctx.audio?.stream)?.(new URL(`${track.id}.mp3`, options.root || MUSIC_ROOT).href, settings);
    } catch { /* Missing tracks are skipped without affecting the narration. */ }
    if (!source?.el) { source?.stop?.(); failed.add(index); startTrack(index + 1); return; }
    current = { source, track, listeners: [] };
    const job = current;
    function listen(event, listener) { source.el.addEventListener(event, listener); job.listeners.push([event, listener]); }
    listen('playing', () => { if (current !== job) return; if (paused()) source.pause(); else updateGain(); });
    listen('timeupdate', updateGain);
    listen('ended', () => { if (current === job) { failed.clear(); startTrack(index + 1); } });
    listen('error', () => { if (current === job) { failed.add(index); startTrack(index + 1); } });
    play();
  }
  function sync() {
    if (disposed || !started) return;
    if (paused()) { cancelEnvelope(); current?.source.pause(); }
    else play();
  }
  function retry() {
    if (disposed || !started || paused()) return;
    if (!current) { failed.clear(); startTrack(index); } else play();
  }
  function pause(on) { held = Boolean(on); sync(); }
  function dispose() {
    if (disposed) return;
    disposed = true; clearCurrent();
    for (const off of cleanups) off?.();
    doc?.removeEventListener('visibilitychange', sync);
    doc?.removeEventListener('pointerdown', retry, true);
    doc?.removeEventListener('keydown', retry, true);
  }
  cleanups.push(ctx.on?.('pause', () => pause(true)), ctx.on?.('resume', () => pause(false)));
  cleanups.push(ctx.on?.('suspend', event => {
    if (event?.on && event.reason === 'panic') { dispose(); return; }
    if (event?.reason !== 'focus') held = Boolean(event?.on);
    sync();
  }));
  cleanups.push(tunnel.on('dispose', dispose), tunnel.on('arrive', sync));
  doc?.addEventListener('visibilitychange', sync);
  doc?.addEventListener('pointerdown', retry, true);
  doc?.addEventListener('keydown', retry, true);
  ctx.cleanup?.(dispose);
  const ready = Promise.resolve(tunnel.ready).then(() => {
    if (disposed) return;
    started = true; startTrack(0);
  });
  ready.catch(dispose);
  return Object.freeze({
    ready, pause, dispose,
    setSpeaking(on) { speaking = Boolean(on); updateGain(); },
    debug: () => ({ started, disposed, current: current?.track.id || null, index, speaking, gain: lastGain,
      playing: Boolean(current && !current.source.el.paused), timers: Number(envelopeTimer !== null), failed: failed.size }),
  });
}
