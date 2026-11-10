import { NARRATION_CLIPS, NARRATION_BANKS, RIDE_RECORDINGS, STAGE_SCHEDULE, VERIFIED_RELEASE_SECONDS } from './narration-data.js';
import { subtitleAt, subtitleBoundaries, SUBTITLE_TIMING } from './subtitles.js';
import { projectionTimeline } from './narration-slides.js';

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
  const fadeInSeconds = 0.12;
  const speechGapMs = Math.max(0, Number(options.speechGapMs ?? 1500));
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
  let wakeTimer = null, loadTimer = null, blockedTimer = null, finishTimer = null, envelopeTimer = null;
  let stageIndex = 0, pendingReaction = null, lastReaction = -Infinity, lastStatus = '';
  let awayAt = null, awayCount = 0, backCount = 0, emptyClicks = 0, capCount = 0;
  let hoverPlayed = 0, lastHoverPlayed = -Infinity, speaking = false, subtitles = options.subtitles !== false;
  let captionText = '';
  let speechReadyAt = 0;
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
  function setSpeaking(value) {
    const next = Boolean(value);
    if (speaking === next) return;
    speaking = next;
    try { options.onSpeaking?.(next); } catch { /* Music ducking cannot block narration cleanup. */ }
  }
  function caption(text = '') {
    if (text === captionText && tunnel.captions?.textContent === text) return;
    captionText = text;
    if (!tunnel.captions) return;
    tunnel.captions.textContent = text;
    tunnel.captions.hidden = !text;
    if (tunnel.captions.dataset) tunnel.captions.dataset.timing = SUBTITLE_TIMING;
  }
  function duration(job = active) {
    const actual = Number(job?.source?.el?.duration);
    return Number.isFinite(actual) && actual > 0 ? actual : clips[job?.id]?.duration || 0;
  }
  function paintSubtitle() {
    if (!subtitles || !active || disposed || doc?.hidden || isPaused() || active.buffering ||
        (!active.missing && (!active.played || active.source?.el.paused))) { caption(); return; }
    caption(subtitleAt(active.id, position(), duration(), options.subtitleData));
  }
  function paintProjection(job = active, complete = false) {
    if (!job?.ride || (!complete && (!job.played && !job.missing))) return;
    const progress = complete ? Infinity : position() / duration(job);
    const reached = job.projections.filter(cue => cue.at <= progress);
    for (const cue of reached) {
      if (cue.beat && !job.projectionBeats.has(cue.key)) {
        job.projectionBeats.add(cue.key);
        runBeat({ id: cue.beat });
      }
    }
    const slide = reached.filter(cue => cue.slot).at(-1);
    if (complete || !slide || job.lastProjection === slide.key) return;
    if (job.lastProjector && job.lastProjector !== slide.projector) projector(job.lastProjector, false);
    job.lastProjector = slide.projector; job.lastProjection = slide.key;
    tunnel.beat('projector.show', { id: slide.projector, slot: slide.slot, title: slide.title, line: '', timing: SUBTITLE_TIMING });
  }
  function setSubtitles(value) {
    subtitles = Boolean(value);
    paintSubtitle();
    if (active?.missing) schedule();
  }
  function updateEnvelope() {
    cancelTimer('envelope');
    if (!active || active.missing || disposed || isPaused() || active.buffering || active.source?.el.paused) return;
    const job = active, at = position(), length = duration(job);
    const fadeOutSeconds = Math.max(0, Number((options.releaseSeconds || VERIFIED_RELEASE_SECONDS)[job.id]) || 0);
    const attack = Math.max(0, Math.min(1, (at - job.fadeStart) / fadeInSeconds));
    const release = fadeOutSeconds ? Math.max(0, Math.min(1, (length - at) / fadeOutSeconds)) : 1;
    const gain = Math.min(attack, release);
    if (!Number.isFinite(job.gain) || Math.abs(job.gain - gain) > 0.001) {
      job.gain = gain; job.source?.setGain?.(gain);
    }
    if (!(length > 0) || at >= length) return;
    // Only the short attacks and releases tick rapidly. The body uses one future deadline.
    const nearEdge = attack < 1 || release < 1;
    if (!nearEdge && !fadeOutSeconds) return;
    const playbackRate = Number(job.source?.el?.playbackRate) || 1;
    const delay = nearEdge ? 20 : Math.max(20, (length - fadeOutSeconds - at) / playbackRate * 1000);
    envelopeTimer = setTimer(() => { envelopeTimer = null; if (active === job) updateEnvelope(); }, delay);
  }
  function cancelTimer(name) {
    if (name === 'wake') { clearTimer(wakeTimer); wakeTimer = null; }
    if (name === 'load') { clearTimer(loadTimer); loadTimer = null; }
    if (name === 'blocked') { clearTimer(blockedTimer); blockedTimer = null; }
    if (name === 'finish') { clearTimer(finishTimer); finishTimer = null; }
    if (name === 'envelope') { clearTimer(envelopeTimer); envelopeTimer = null; }
  }
  function projector(id, show) {
    tunnel.beat(show ? 'projector.show' : 'projector.hide', { id, ...PROJECTORS[id] });
  }
  function sourceFor(id) {
    const url = new URL(`${id}.mp3`, options.voiceRoot || VOICE_ROOT).href;
    const settings = { bus: 'voice', source: 'documentary', sourceLabel: 'Museum narrator', gain: 0, autoplay: false, preload: 'auto' };
    if (options.createStream) return options.createStream(url, settings);
    if (ctx.audio?.stream) return ctx.audio.stream(url, settings);
    // The standalone preview can omit the game mixer. The real game always uses its scoped stream API.
    if (typeof Audio === 'undefined') return null;
    const el = new Audio(url); el.preload = 'auto'; el.volume = 0;
    return { el, play: () => el.play(), pause: () => el.pause(), setGain(value) { el.volume = Math.max(0, Math.min(1, value)); }, stop() { el.pause(); el.removeAttribute('src'); el.load(); } };
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
    cancelTimer('load'); cancelTimer('blocked'); cancelTimer('envelope'); setSpeaking(false);
  }
  function runBeat(cue) {
    tunnel.beat(cue.id, cue.data);
  }
  function stopActive(complete = false) {
    if (!active) return;
    const job = active; active = null;
    clearSource(job); cancelTimer('wake'); caption();
    if (complete) {
      paintProjection(job, true);
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
    speechReadyAt = now() + speechGapMs;
    if (!disposed) { status('finished'); dispatch(); }
  }
  function missing(job, reason) {
    if (disposed || active !== job || job.missing) return;
    const at = position(); clearSource(job);
    job.missing = true; job.elapsed = at; job.since = isPaused() ? null : now();
    status('missing', reason || 'Recording unavailable; visual timing continues.');
    if (job.final) { ended(job); return; }
    advance();
  }
  function playActive() {
    if (!active || disposed || isPaused()) return;
    const job = active;
    if (job.missing) { if (job.since === null) job.since = now(); paintSubtitle(); schedule(); return; }
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
    const job = active, at = position();
    while (job.beatIndex < (job.ride?.beats.length || 0) && job.ride.beats[job.beatIndex].at <= at) runBeat(job.ride.beats[job.beatIndex++]);
    if (job.missing && at >= clips[job.id].duration) { ended(job); return; }
    paintProjection(); paintSubtitle(); updateEnvelope();
    schedule();
  }
  function startJob(job) {
    if (disposed) { job.release?.(); return; }
    active = { ...job, beatIndex: 0, missing: false, elapsed: 0, since: null, listeners: [], fadeStart: 0, gain: 0,
      projections: projectionTimeline(job.id, options.subtitleData), projectionBeats: new Set(), lastProjector: job.ride?.projector || null, lastProjection: null };
    const current = active;
    caption();
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
      if (isPaused()) { current.source.pause(); return; }
      cancelTimer('load'); cancelTimer('blocked');
      if (!current.played) current.onStart?.();
      current.played = true; current.buffering = false; current.fadeStart = position(); current.gain = 0; current.source?.setGain?.(0);
      setSpeaking(true); status('playing'); paintProjection(); paintSubtitle(); updateEnvelope();
      if (current.final && !current.finishStarted) {
        current.finishStarted = true;
        finishRemaining = Math.max(0, duration(current) - position()) * 1000 + 2000;
        armFinishTimer();
      }
    });
    const quiet = event => {
      if (active !== current) return;
      current.buffering = event?.type === 'waiting' || event?.type === 'stalled';
      cancelTimer('envelope'); setSpeaking(false); caption();
    };
    listen('pause', quiet); listen('waiting', quiet); listen('stalled', quiet);
    listen('loadedmetadata', () => { if (active === current) { paintProjection(); paintSubtitle(); updateEnvelope(); } });
    listen('durationchange', () => { if (active === current) { paintProjection(); paintSubtitle(); updateEnvelope(); } });
    listen('canplay', () => {
      if (active !== current || disposed) return;
      cancelTimer('load'); cancelTimer('blocked');
      blockedTimer = setTimer(() => {
        blockedTimer = null;
        if (active === current && !isPaused() && current.source?.el.paused) status('blocked', 'A gesture is needed to start the narrator.');
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
      if (!active.missing) return;
      const at = position(), times = [clips[active.id].duration, ...(active.ride?.beats || []).map(cue => cue.at)];
      times.push(...active.projections.map(cue => cue.at * duration()));
      if (subtitles) times.push(...subtitleBoundaries(active.id, duration(), options.subtitleData));
      const next = Math.min(clips[active.id].duration, ...times.filter(time => time > at + 0.001));
      wakeTimer = setTimer(() => { wakeTimer = null; advance(); }, Math.max(1, (next - at) * 1000));
    } else if (stageStarted !== null && !finishing && stageIndex < stageSchedule.length) {
      wakeTimer = setTimer(() => { wakeTimer = null; dispatch(); }, Math.max(1, (stageSchedule[stageIndex].at - elapsed()) * 1000));
    }
  }
  function dispatch() {
    if (disposed || active || isPaused()) return;
    if (now() < speechReadyAt) {
      cancelTimer('wake');
      wakeTimer = setTimer(() => { wakeTimer = null; dispatch(); }, speechReadyAt - now());
      return;
    }
    if (queue.length) { startJob(queue.shift()); return; }
    if (finishing) return;
    if (pendingReaction) { const job = pendingReaction; pendingReaction = null; startJob(job); return; }
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
  function reaction(id, hooks = {}) {
    if (!clips[id] || disposed || finishing || isPaused() || now() - lastReaction < 12000) return;
    lastReaction = now();
    const job = { id, ...hooks };
    pendingReaction = job;
    dispatch();
  }
  function retry() {
    if (disposed || isPaused()) return;
    if (active) playActive(); else dispatch();
  }
  function armFinishTimer() {
    cancelTimer('finish');
    if (!finishing || !finishResolve || isPaused() || !active?.final || !active.played) return;
    finishDue = now() + finishRemaining;
    finishTimer = setTimer(() => {
      finishTimer = null;
      const job = active, el = job?.source?.el;
      if (!job?.final) return;
      if (el?.ended) ended(job);
      else if (el?.error) missing(job, 'The closing recording failed.');
      // A duration estimate is never permission to truncate a healthy or paused recording.
    }, finishRemaining);
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
      freezeFallback(); active?.source?.pause(); cancelTimer('wake'); cancelTimer('envelope'); setSpeaking(false); caption();
      if (finishTimer !== null) { finishRemaining = Math.max(1, finishDue - now()); cancelTimer('finish'); }
      status('paused');
    } else { playActive(); dispatch(); armFinishTimer(); }
  }
  function visibility() {
    if (disposed) return;
    if (!stage) {
      if (doc?.hidden) { freezeFallback(); active?.source?.pause(); cancelTimer('wake'); cancelTimer('envelope'); setSpeaking(false); caption(); }
      else retry();
      return;
    }
    if (doc?.hidden) {
      caption();
      awayAt = now();
      if (stageStarted !== null) reaction((banks.away || [])[Math.min(awayCount++, 4)]);
    } else {
      const phonePaused = active?.played && active.source?.el.paused && !isPaused();
      retry();
      paintSubtitle();
      if (awayAt !== null && stageStarted !== null) reaction(phonePaused ? 'ph1' : now() - awayAt > 900000 ? 'bk4' : ['bk1', 'bk2', 'bk3'][backCount++ % 3]);
      awayAt = null;
    }
  }
  function start() {
    if (started || disposed) return;
    started = true;
    if (tunnel.state.docked || tunnel.state.z >= 580) { beginStage(); return; }
    const z = tunnel.state.z;
    const initial = z < 12 ? rides.find(row => row.id === 'r1') : rides.find(row =>
      (row.id === 'r2' && z >= row.at && z < 130) ||
      (row.id === 'r3' && z >= row.at && z < 340) ||
      (row.id === 'r4' && z >= row.at && z < 520));
    if (initial) queueRide(initial);
  }
  const cues = rides.filter(row => row.at > 0).map(row => ({ id: `narration.${row.id}`, at: row.at }));
  tunnel.addCues(cues);
  cleanups.push(tunnel.on('cue', cue => {
    if (!started || disposed) return;
    const ride = rides.find(row => cue.id === `narration.${row.id}`);
    if (ride) queueRide(ride);
  }));
  cleanups.push(tunnel.on('arrive', beginStage));
  cleanups.push(tunnel.on('band', event => {
    if (!stage && !active && !queue.length && !tunnel.state.locked && event.band >= 3 && event.heldS >= 2.4) reaction('r2b');
  }));
  cleanups.push(tunnel.on('stage-action', event => {
    if (!stage || stageStarted === null) return;
    if (event.id === 'ad') reaction(event.opened ? `ad${phase()}` : ['adc1', 'adc2'][Math.min(capCount++, 1)]);
    if (event.id === 'hover-new-run' && hoverPlayed < 2 && now() - lastHoverPlayed >= 300000 && pendingReaction?.kind !== 'hover' && active?.kind !== 'hover') {
      reaction(['hv1', 'hv2'][hoverPlayed], { kind: 'hover', onStart() { hoverPlayed++; lastHoverPlayed = now(); } });
    }
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
    finishing = true; pendingReaction = null; queue.splice(0).forEach(job => job.release?.());
    const id = `nr${phase()}`;
    if (!clips[id]) return Promise.resolve();
    finishPromise = new Promise(resolve => { finishResolve = resolve; });
    queue.push({ id, final: true, onEnd() { cancelTimer('finish'); finishResolve?.(); finishResolve = null; } });
    dispatch();
    return finishPromise;
  }
  function dispose() {
    if (disposed) return;
    disposed = true;
    stopActive(false); queue.splice(0).forEach(job => job.release?.()); pendingReaction = null;
    for (const name of ['wake', 'load', 'blocked', 'finish', 'envelope']) cancelTimer(name);
    cleanups.splice(0).forEach(off => off?.());
    doc?.removeEventListener('visibilitychange', visibility);
    doc?.removeEventListener('pointerdown', retry, true);
    doc?.removeEventListener('keydown', retry, true);
    finishResolve?.(); finishResolve = null;
    caption(); setSpeaking(false);
    status('disposed');
  }
  return Object.freeze({
    ready, pause, dispose, retry, unlock: retry, finishNewRun, setSubtitles,
    debug: () => ({ started, disposed, stage, current: active?.id || null, queued: queue.map(job => job.id),
      gated: false, missing: Boolean(active?.missing), elapsed: elapsed(),
      acceptedTerms: acceptedTerms.length, status: lastStatus.split(':')[0], subtitles, subtitleTiming: SUBTITLE_TIMING, speaking, hoverPlayed,
      timers: [wakeTimer, loadTimer, blockedTimer, finishTimer, envelopeTimer].filter(timer => timer !== null).length }),
  });
}
