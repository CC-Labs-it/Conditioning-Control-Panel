/* The Bimbodoll ending's recorded cues. All playback goes through existing voice and moan controls. */
const LINES = [
  ['sink', 2.58], ['dropping', 4.60], ['feel-good', 3.20], ['bimbo-doll', 4.10], ['good-bimbo', 4.83],
];
const MOANS = [['soft', 1.69], ['breathy', 2.97], ['rising', 3.69]];
const INTERJECTIONS = [
  ['um', .66], ['so-like', 2.69], ['umm-thinking', 3.48], ['giggle-bimbo', 3.00],
  ['giggle2', 1.18, 'assets/audio/themes/bimbo/giggle2.mp3'],
];
const CLICKS = ['interjection', 'interjection', 'moan', 'interjection', 'line', 'interjection', 'line'];
const INSIDE = [
  [2200, 'interjection'], [4150, 'moan'], [4300, 'line'],
  [5600, 'interjection'], [4050, 'moan'], [4420, 'line'],
];
const linePath = name => 'assets/audio/themes/bimbo/ending/' + name + '.mp3';
const moanPath = name => 'assets/audio/moans/' + name + '.mp3';
const clipPath = (clip, kind) => clip[2] || (kind === 'moan' ? moanPath(clip[0]) : linePath(clip[0]));

function shuffledDraw(clips, opening = []) {
  let bag = [], previous = null, first = true;
  return () => {
    if (!bag.length) {
      bag = first ? clips.filter(clip => !opening.includes(clip)) : clips.slice();
      for (let i = bag.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [bag[i], bag[j]] = [bag[j], bag[i]];
      }
      const last = bag.length - 1;
      if (bag[last] === previous) [bag[0], bag[last]] = [bag[last], bag[0]];
      if (first) { bag.push(...opening.slice().reverse()); first = false; }
    }
    previous = bag.pop();
    return previous;
  };
}

export function createBambiVoices(ctx) {
  const doc = ctx.world?.ownerDocument || globalThis.document;
  const win = doc?.defaultView;
  const audio = ctx.audio;
  let phase = 'absorb', entered = false, blurred = false, clicks = 0, busyUntil = -Infinity, current = null, timer = null;
  const draw = {
    interjection: shuffledDraw(INTERJECTIONS, [INTERJECTIONS[0], INTERJECTIONS[1], INTERJECTIONS[3]]),
    line: shuffledDraw(LINES), moan: shuffledDraw(MOANS),
  };
  let nextClick = draw.interjection();
  audio.preload?.(clipPath(nextClick, 'interjection'));

  function silence() {
    current?.abort();
    current = null;
  }
  function play(path, options) {
    silence();  // abort also invalidates a first decode still in flight
    current = new AbortController();
    audio.play(path, { ...options, signal: current.signal });
  }
  function recorded(clip, kind, gain) {
    const moan = kind === 'moan';
    const source = moan ? 'moans' : 'voices';
    // A player can first enable audio after entry. The mixer installs the filter once it is armed.
    if (phase === 'inside') audio.muffle(source, true, { hz: 1250, cut: .85 });
    play(clipPath(clip, kind), {
      bus: 'voice', source, sourceLabel: moan ? 'Lucky moans' : 'Voices',
      gain: gain * (ctx.gentle ? .76 : 1), fadeInMs: moan ? 95 : 22, fadeOutMs: moan ? 195 : 90,
    });
  }
  const held = () => ctx.paused || blurred || doc?.hidden;
  function click(event, seconds) {
    if (phase !== 'absorb' || !event?.isTrusted || held() || (clicks && seconds < busyUntil)) return false;
    const kind = CLICKS[clicks % CLICKS.length];
    const clip = nextClick;
    recorded(clip, kind, kind === 'moan' ? .135 : clicks ? .24 : .22);
    busyUntil = seconds + Math.max(3.6, clip[1] + 1.10);
    clicks++;
    const nextKind = CLICKS[clicks % CLICKS.length];
    nextClick = draw[nextKind]();
    audio.preload?.(clipPath(nextClick, nextKind));
    return true;
  }
  function giggle(seconds, path, gain) {
    if (phase !== 'absorb' || held() || seconds < busyUntil) return false;
    play(path, { bus: 'voice', gain });
    busyUntil = seconds + 1.85;
    return true;
  }
  function hush() {
    if (phase === 'done') return;
    timer?.();
    timer = null;
    silence();
    if (phase === 'inside') {
      audio.muffle('voices', false);
      audio.muffle('moans', false);
    }
    phase = 'quiet';
  }
  function enter() {
    if (phase === 'done' || entered) return;
    entered = true;
    silence();
    phase = 'inside';
    function schedule(i) {
      if (i >= INSIDE.length || phase !== 'inside') return;
      const [delay,kind] = INSIDE[i];
      const clip = draw[kind]();
      audio.preload?.(clipPath(clip, kind));
      timer = ctx.after(delay, () => {
        if (phase !== 'inside') return;
        if (!held()) {
          recorded(clip, kind, kind === 'moan' ? .12 : .23);
        }
        schedule(i + 1);
      });
    }
    schedule(0);
  }
  function onVisibility() { if (doc?.hidden) silence(); }
  function blur() { blurred = true; silence(); }
  function focus() { blurred = false; }
  function dispose() {
    if (phase === 'done') return;
    hush();
    phase = 'done';
    doc?.removeEventListener('visibilitychange', onVisibility);
    win?.removeEventListener('blur', blur);
    win?.removeEventListener('focus', focus);
  }
  doc?.addEventListener('visibilitychange', onVisibility);
  win?.addEventListener('blur', blur);
  win?.addEventListener('focus', focus);
  ctx.on('pause', silence);
  ctx.on('suspend', ({ on, reason }) => { if (on) { if (reason === 'panic') dispose(); else silence(); } });
  ctx.cleanup(dispose);
  return { click, giggle, hush, enter, dispose };
}
