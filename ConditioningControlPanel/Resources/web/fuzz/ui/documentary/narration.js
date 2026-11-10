import { NARRATION_CLIPS, NARRATION_BANKS, RIDE_RECORDINGS, STAGE_SCHEDULE } from './narration-data.js';

const VOICE_ROOT = new URL('../../assets/documentary/voice/', import.meta.url);
const PROJECTORS = {
  booth: { title: 'The Subject', line: 'A documentary about your evening.' },
  lever: { title: 'Pumping research', line: 'A small input. A very committed machine.' },
  reflex: { title: 'The click reflex', line: 'Notice. Click. Repeat.' },
  replay: { title: 'Again, in slow motion', line: 'An extraordinary specimen.' },
  wakeup: { title: 'The outside world', line: 'Specimen: unpressed.' },
};

/** Final delivery audio only. No speech synthesis and no decoded hour-long buffers. */
export function createDocumentaryNarration(ctx, tunnel, options = {}) {
  const doc = options.document || tunnel.captions?.ownerDocument || globalThis.document;
  const now = options.now || (() => performance.now());
  const setTimer = options.setTimer || ((fn, ms) => setTimeout(fn, ms));
  const clearTimer = options.clearTimer || (id => clearTimeout(id));
  const clips = options.clips || NARRATION_CLIPS;
  const banks = options.banks || NARRATION_BANKS;
  const rides = options.rides || RIDE_RECORDINGS;
  const advancedMs = Math.max(0, Number(options.hours) || 0) * 3600000;
  const cleanups = [], queue = [], seen = new Set();
  const accepted = new Set(options.acceptedClauseIds || ctx.state?.acceptedClauseIds || []);
  const acceptedTerms = Object.keys(clips).filter(id => id.startsWith('term-') && accepted.has(id));
  const stageSchedule = (options.schedule || STAGE_SCHEDULE).filter(row => {
    if (row.id.startsWith('term-')) return accepted.has(row.id);
    // Current game saves only a Terms count, which cannot establish which clauses were accepted.
    if (['c6', 'c7'].includes(row.id)) return acceptedTerms.some(id => clips[id].block === 1);
    if (['c11', 'c12'].includes(row.id)) return acceptedTerms.some(id => clips[id].block === 2);
    return Boolean(clips[row.id]);
  });
  let active = null, started = false, disposed = false, stage = false, stageStarted = null;
  let explicitPause = Boolean(ctx.paused), pausedAt = explicitPause ? now() : null, stagePausedMs = 0;
  let wakeTimer = null, loadTimer = null, blockedTimer = null, finishTimer = null;
  let stageIndex = 0, pendingReaction = null, lastReaction = -Infinity, lastStatus = '';
  let awayAt = null, awayCount = 0, backCount = 0, hoverCount = 0, emptyClicks = 0, capCount = 0;
  let finishing = false, finishPromise = null, finishResolve = null, finishRemaining = 0, finishDue = 0;

  const subscriber = ['basic', 'prime'].includes(String(ctx.caps?.plan || '').toLowerCase());
  const isPaused = () => explicitPause || Boolean(ctx.paused) || (!stage && Boolean(doc?.hidden));
  const elapsed = () => stageStarted === null ? 0 : Math.max(0, ((pausedAt ?? now()) - stageStarted - stagePausedMs + advancedMs) / 1000);
  const phase = () => elapsed() < 30 ? 0 : elapsed() < 300 ? 1 : elapsed() < 1200 ? 2 : elapsed() < 3600 ? 3 : elapsed() < 7200 ? 4 : elapsed() < 10800 ? 5 : 6;
  function status(value, reason) {
    const key = `${value}:${active?.id || ''}`;
    if (key === lastStatus) return;
    lastStatus = key;
    try { options.onStatus?.({ status: value, id: active?.id || null, reason }); } catch { /* Reporting cannot stop playback cleanup. */ }
  }
  function cancelTimer(name) {
    if (name === 'wake') { clearTimer(wakeTimer); wakeTimer = null; }
    if (name === 'load') { clearTimer(loadTimer); loadTimer = null; }
    if (name === 'blocked') { clearTimer(blockedTimer); blockedTimer = null; }
    if (name === 'finish') { clearTimer(finishTimer); finishTimer = null; }
  }
  function projector(id, show) {
    tunnel.beat(show ? 'projector.show' : 'projector.hide', { id, ...PROJECTORS[id] });
  }
  function sourceFor(id) {
    const url = new URL(`${id}.mp3`, options.voiceRoot || VOICE_ROOT).href;
    const settings = { bus: 'voice', source: 'documentary', sourceLabel: 'Museum narrator', gain: 1, autoplay: false, preload: 'auto' };
    if (options.createStream) return options.createStream(url, settings);
    if (ctx.audio?.stream) return ctx.audio.stream(url, settings);
    // The standalone preview can omit the game mixer. The real game always uses its scoped stream API.
    if (typeof Audio === 'undefined') return null;
    const el = new Audio(url); el.preload = 'auto';
    return { el, play: () => el.play(), pause: () => el.pause(), stop() { el.pause(); el.removeAttribute('src'); el.load(); } };
  }
  function position() {
    if (!active) return 0;
    if (!active.missing) return Math.max(0, Number(active.source?.el?.currentTime) || 0);
    return active.elapsed + (active.since === null ? 0 : Math.max(0, now() - active.since) / 1000);
  }
  function freezeFallback() {
    if (!active?.missing) return;
    active.elapsed = position(); active.since = null;
  }
  function clearSource(job) {
    job?.listeners?.forEach(([event, fn]) => job.source?.el?.removeEventListener(event, fn));
    if (job) job.listeners = [];
    try { job?.source?.stop(); } catch { /* A detached media element may already be stopped. */ }
    if (job) job.source = null;
    cancelTimer('load'); cancelTimer('blocked');
  }
  function runBeat(cue) {
    if (cue.id === 'floor.replay') { projector('reflex', false); projector('replay', true); }
    tunnel.beat(cue.id, cue.data);
  }
  function stopActive(complete = false) {
    if (!active) return;
    const job = active; active = null;
    clearSource(job); cancelTimer('wake');
    if (complete) {
      for (const cue of (job.ride?.beats || []).slice(job.beatIndex)) runBeat(cue);
      if (job.id === 'r1') tunnel.beat('booth.unlock');
    }
    if (job.id === 'r3') tunnel.beat('floor.release');
    job.release?.();
    if (job.ride) { projector(job.ride.projector, false); if (job.id === 'r3') projector('replay', false); }
    if (complete) job.onEnd?.();
  }
  function ended(job) {
    if (disposed || active !== job) return;
    stopActive(true);
    if (!disposed) { status('finished'); dispatch(); }
  }
  function missing(job, reason) {
    if (disposed || active !== job || job.missing) return;
    const at = position(); clearSource(job);
    job.missing = true; job.elapsed = at; job.since = isPaused() || job.gated ? null : now();
    status('missing', reason || 'Recording unavailable; visual timing continues.');
    if (job.final) { ended(job); return; }
    advance();
  }
  function playActive() {
    if (!active || disposed || isPaused() || active.gated) return;
    const job = active;
    if (job.missing) { if (job.since === null) job.since = now(); schedule(); return; }
    try {
      const result = job.source?.play();
      result?.catch?.(error => {
        if (active !== job || disposed || isPaused()) return;
        if (error?.name === 'NotAllowedError') status('blocked', 'A gesture is needed to start the narrator.');
        else if (error?.name !== 'AbortError') missing(job, String(error?.message || error));
      });
    } catch (error) {
      if (error?.name === 'NotAllowedError') status('blocked'); else missing(job, String(error?.message || error));
    }
  }
  function advance() {
    if (!active || disposed || isPaused()) return;
    const job = active, at = position(), gate = job.ride?.gate;
    if (gate && !job.gatePassed && at >= gate.at && tunnel.state.z < gate.distance) {
      job.gated = true;
      if (job.missing) { job.elapsed = gate.at; job.since = null; }
      else { job.source?.pause(); try { job.source.el.currentTime = gate.at; } catch { /* Playback remains paused until the distance cue. */ } }
      cancelTimer('wake'); return;
    }
    if (gate && tunnel.state.z >= gate.distance) job.gatePassed = true;
    while (job.beatIndex < (job.ride?.beats.length || 0) && job.ride.beats[job.beatIndex].at <= at) runBeat(job.ride.beats[job.beatIndex++]);
    if (job.missing && at >= clips[job.id].duration) { ended(job); return; }
    schedule();
  }
  function startJob(job) {
    if (disposed) { job.release?.(); return; }
    active = { ...job, beatIndex: 0, gatePassed: false, gated: false, missing: false, elapsed: 0, since: null, listeners: [] };
    const current = active;
    if (job.ride) projector(job.ride.projector, true);
    status('loading');
    try { current.source = sourceFor(job.id); } catch (error) { missing(current, String(error?.message || error)); return; }
    if (!current.source?.el) { missing(current, 'Audio stream unavailable.'); return; }
    const listen = (event, fn) => { current.source.el.addEventListener(event, fn); current.listeners.push([event, fn]); };
    listen('timeupdate', advance);
    listen('ended', () => ended(current));
    listen('error', () => missing(current, 'The recording could not be loaded.'));
    listen('playing', () => {
      if (active !== current || disposed) return;
      if (isPaused() || current.gated) { current.source.pause(); return; }
      cancelTimer('load'); cancelTimer('blocked'); current.played = true; status('playing');
    });
    listen('canplay', () => {
      if (active !== current || disposed) return;
      cancelTimer('load'); cancelTimer('blocked');
      blockedTimer = setTimer(() => {
        blockedTimer = null;
        if (active === current && !isPaused() && !current.gated && current.source?.el.paused) status('blocked', 'A gesture is needed to start the narrator.');
      }, 350);
    });
    loadTimer = setTimer(() => { loadTimer = null; if (active === current && !current.played && current.source?.el.readyState === 0) missing(current, 'Recording load timed out.'); }, 15000);
    if (isPaused()) status('paused'); else playActive();
  }
  function queueRide(ride) {
    if (seen.has(ride.id) || disposed || finishing) return;
    seen.add(ride.id);
    const release = ride.hold == null ? null : tunnel.hold(`narration.${ride.id}`, ride.hold);
    queue.push({ id: ride.id, ride, release }); dispatch();
  }
  function schedule() {
    cancelTimer('wake');
    if (disposed || isPaused()) return;
    if (active) {
      if (!active.missing || active.gated) return;
      const at = position(), times = [clips[active.id].duration, ...(active.ride?.beats || []).map(cue => cue.at)];
      if (active.ride?.gate && !active.gatePassed) times.push(active.ride.gate.at);
      const next = Math.min(clips[active.id].duration, ...times.filter(time => time > at + 0.001));
      wakeTimer = setTimer(() => { wakeTimer = null; advance(); }, Math.max(1, (next - at) * 1000));
    } else if (stageStarted !== null && !finishing && stageIndex < stageSchedule.length) {
      wakeTimer = setTimer(() => { wakeTimer = null; dispatch(); }, Math.max(1, (stageSchedule[stageIndex].at - elapsed()) * 1000));
    }
  }
  function dispatch() {
    if (disposed || active || isPaused()) return;
    if (queue.length) { startJob(queue.shift()); return; }
    if (finishing) return;
    if (pendingReaction) { const id = pendingReaction; pendingReaction = null; startJob({ id }); return; }
    if (stageStarted !== null && stageIndex < stageSchedule.length && stageSchedule[stageIndex].at <= elapsed()) {
      const next = stageSchedule[stageIndex++];
      if (next.at >= 7200) tunnel.beat('stage.booth', { state: 'c' });
      else if (next.at >= 3600) tunnel.beat('stage.booth', { state: 'b' });
      startJob({ id: next.id }); return;
    }
    schedule();
  }
  function beginStage() {
    if (stage || disposed || finishing) return;
    stage = true;
    tunnel.beat('stage.pitch');
    const pitch = subscriber ? ['sub1', 'sub2', ...(ctx.caps?.plan === 'prime' ? ['sub3'] : []), 'p2'] : ['p1', 'p2'];
    pitch.filter(id => clips[id]).forEach((id, index, list) => queue.push({ id, onEnd: index === list.length - 1 ? () => {
      stageStarted = now(); stagePausedMs = 0;
      // Preview time jumps should not queue hours of stale commentary.
      stageIndex = stageSchedule.findIndex(row => row.at >= advancedMs / 1000);
      if (stageIndex < 0) stageIndex = stageSchedule.length;
      tunnel.beat('stage.buttons');
    } : null }));
    dispatch();
  }
  function reaction(id) {
    if (!clips[id] || disposed || finishing || isPaused() || now() - lastReaction < 12000) return;
    lastReaction = now();
    if (active || queue.length) pendingReaction = id;
    else startJob({ id });
  }
  function retry() {
    if (disposed || isPaused()) return;
    if (active) playActive(); else dispatch();
  }
  function armFinishTimer() {
    cancelTimer('finish');
    if (!finishing || !finishResolve || isPaused()) return;
    finishDue = now() + finishRemaining;
    finishTimer = setTimer(() => { finishTimer = null; stopActive(false); finishResolve?.(); finishResolve = null; }, finishRemaining);
  }
  function pause(on) {
    if (disposed) return;
    explicitPause = Boolean(on);
    const held = isPaused();
    if (held && pausedAt === null) pausedAt = now();
    if (!held && pausedAt !== null) {
      if (stageStarted !== null) stagePausedMs += now() - Math.max(stageStarted, pausedAt);
      pausedAt = null;
    }
    if (isPaused()) {
      freezeFallback(); active?.source?.pause(); cancelTimer('wake');
      if (finishTimer !== null) { finishRemaining = Math.max(1, finishDue - now()); cancelTimer('finish'); }
      status('paused');
    } else { playActive(); dispatch(); armFinishTimer(); }
  }
  function visibility() {
    if (disposed) return;
    if (!stage) {
      if (doc?.hidden) { freezeFallback(); active?.source?.pause(); cancelTimer('wake'); }
      else retry();
      return;
    }
    if (doc?.hidden) {
      awayAt = now();
      if (stageStarted !== null) reaction((banks.away || [])[Math.min(awayCount++, 4)]);
    } else {
      const phonePaused = active?.played && active.source?.el.paused && !active.gated && !isPaused();
      retry();
      if (awayAt !== null && stageStarted !== null) reaction(phonePaused ? 'ph1' : now() - awayAt > 900000 ? 'bk4' : ['bk1', 'bk2', 'bk3'][backCount++ % 3]);
      awayAt = null;
    }
  }
  function start() {
    if (started || disposed) return;
    started = true;
    if (tunnel.state.docked || tunnel.state.z >= 580) { beginStage(); return; }
    const z = tunnel.state.z;
    const initial = z < 12 ? rides.find(row => row.id === 'r1') : z < 130 ? rides.find(row => row.id === 'r2') : z >= 180 && z < 340 ? rides.find(row => row.id === 'r3') : z >= 400 && z < 520 ? rides.find(row => row.id === 'r4') : null;
    if (initial) queueRide(initial);
  }
  const cues = rides.filter(row => row.at > 0).map(row => ({ id: `narration.${row.id}`, at: row.at }));
  rides.filter(row => row.gate).forEach(row => cues.push({ id: `narration.gate.${row.id}`, at: row.gate.distance }));
  tunnel.addCues(cues);
  cleanups.push(tunnel.on('cue', cue => {
    if (!started || disposed) return;
    const ride = rides.find(row => cue.id === `narration.${row.id}`);
    if (ride) queueRide(ride);
    if (active?.gated && tunnel.state.z >= active.ride.gate.distance) {
      active.gated = false; active.gatePassed = true; playActive(); advance();
    }
  }));
  cleanups.push(tunnel.on('arrive', beginStage));
  cleanups.push(tunnel.on('band', event => {
    if (!stage && !active && !queue.length && !tunnel.state.locked && event.band >= 3 && event.heldS >= 2.4) reaction('r2b');
  }));
  cleanups.push(tunnel.on('stage-action', event => {
    if (!stage || stageStarted === null) return;
    if (event.id === 'ad') reaction(event.opened ? `ad${phase()}` : ['adc1', 'adc2'][Math.min(capCount++, 1)]);
    if (event.id === 'hover-new-run') reaction(['hv1', 'hv2'][Math.min(hoverCount++, 1)]);
    if (event.id === 'empty-click') {
      emptyClicks++;
      const index = [1, 10, 25, 50, 200].indexOf(emptyClicks);
      if (index >= 0) reaction(`ck${index + 1}`);
    }
  }));
  cleanups.push(ctx.on?.('pause', () => pause(true)), ctx.on?.('resume', () => pause(false)));
  cleanups.push(ctx.on?.('suspend', event => {
    if (event?.on && event.reason === 'panic') { dispose(); return; }
    if (stage && event?.reason === 'focus') {
      // Host focus can hold the global mixer, but the docked wall clock keeps counting.
      // Browser visibility alone does not hold the mixer, so ordinary background narration continues.
      if (!event.on) { playActive(); dispatch(); }
      return;
    }
    pause(Boolean(event?.on));
  }));
  cleanups.push(tunnel.on('dispose', dispose));
  doc?.addEventListener('visibilitychange', visibility);
  doc?.addEventListener('pointerdown', retry, true);
  doc?.addEventListener('keydown', retry, true);
  const ready = Promise.resolve(tunnel.ready).then(start);
  ready.catch(() => dispose());
  ctx.cleanup?.(dispose);

  function finishNewRun() {
    if (disposed) return Promise.resolve();
    if (finishPromise) return finishPromise;
    finishing = true; pendingReaction = null; queue.splice(0).forEach(job => job.release?.()); stopActive(false);
    const id = `nr${phase()}`;
    if (!clips[id]) return Promise.resolve();
    finishPromise = new Promise(resolve => { finishResolve = resolve; });
    finishRemaining = clips[id].duration * 1000 + 2000;
    startJob({ id, final: true, onEnd() { cancelTimer('finish'); finishResolve?.(); finishResolve = null; } });
    armFinishTimer();
    return finishPromise;
  }
  function dispose() {
    if (disposed) return;
    disposed = true;
    stopActive(false); queue.splice(0).forEach(job => job.release?.()); pendingReaction = null;
    for (const name of ['wake', 'load', 'blocked', 'finish']) cancelTimer(name);
    cleanups.splice(0).forEach(off => off?.());
    doc?.removeEventListener('visibilitychange', visibility);
    doc?.removeEventListener('pointerdown', retry, true);
    doc?.removeEventListener('keydown', retry, true);
    finishResolve?.(); finishResolve = null;
    status('disposed');
  }
  return Object.freeze({
    ready, pause, dispose, retry, unlock: retry, finishNewRun,
    debug: () => ({ started, disposed, stage, current: active?.id || null, queued: queue.map(job => job.id),
      gated: Boolean(active?.gated), missing: Boolean(active?.missing), elapsed: elapsed(),
      acceptedTerms: acceptedTerms.length, status: lastStatus.split(':')[0],
      timers: [wakeTimer, loadTimer, blockedTimer, finishTimer].filter(timer => timer !== null).length }),
  });
}
