/* The camera's distance drives the mix. All playback stays inside the game's master, mixer and pause clock. */

const SCORE = 'assets/audio/breakout/music.ogg';
const WORDS = ['listen', 'drift', 'deeper', 'let-go', 'sink'];
const OPEN_CLICKS = [260, 920, 1270, 2150];
const TAU = Math.PI * 2;
const clamp = value => Math.max(0, Math.min(1, value));

function makeFoley(ac) {
  // Render a short microswitch impulse, a rattling tube and one paper footfall. Their envelopes are sample-accurate.
  const sampleRate = 24000;
  let seed = 17383;
  const noise = () => { seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0; return seed / 2147483648 - 1; };
  function mono(seconds, sample) {
    const buffer = ac.createBuffer(1, Math.ceil(seconds * sampleRate), sampleRate);
    const data = buffer.getChannelData(0);
    for (let i = 0; i < data.length; i++) data[i] = sample(i / sampleRate);
    return buffer;
  }
  function switchTap(t) {
    const body = Math.sin(TAU * (530 * t - 820 * t * t)) * Math.exp(-74 * t);
    const reed = (0.47 * Math.sin(TAU * 1710 * t) + 0.17 * Math.sin(TAU * 2915 * t)) * Math.exp(-272 * t);
    return (0.56 * body + reed + 0.48 * noise() * Math.exp(-400 * t)) * Math.min(1, t * 3400);
  }
  const click = mono(0.17, t => switchTap(t) + (t > 0.037 ? 0.25 * switchTap((t - 0.037) * 1.18) : 0));
  const panel = mono(0.73, t => {
    const flex = Math.sin(TAU * (73 * t + 6 * (1 - Math.exp(-13 * t)) / 13)) * Math.exp(-15 * t);
    const cavity = (0.28 * Math.sin(TAU * 183 * t) + 0.22 * Math.sin(TAU * 309 * t)) * Math.exp(-22 * t);
    const glass = (0.13 * Math.sin(TAU * 786 * t) + 0.055 * Math.sin(TAU * 1233 * t)) * Math.exp(-8 * t);
    return Math.min(1, t / 0.005) * (0.56 * flex + cavity + glass);
  });
  let slow = 0, fast = 0;
  const paperStep = mono(0.23, t => {
    const n = noise();
    slow += 0.06 * (n - slow); fast += 0.35 * (n - fast);
    const scuff = (fast - slow) * Math.exp(-22 * t);
    return Math.min(1, t / 0.007) * (scuff + 0.33 * Math.sin(TAU * (97 * t - 70 * t * t)) * Math.exp(-25 * t));
  });
  // Two staggered sprays of loose beads against the left and right tube walls. Leave the loop seam silent.
  const shaker = ac.createBuffer(2, sampleRate * 2, sampleRate);
  for (let channel = 0; channel < 2; channel++) {
    const data = shaker.getChannelData(channel);
    for (let onset = 0.015 + channel * 0.03; onset < 1.93; onset += 0.032 + (noise() + 1) * 0.044) {
      const at = Math.floor(onset * sampleRate), pitch = 910 + (noise() + 1) * 640;
      const accent = 0.20 + 0.32 * (1 + Math.cos(TAU * onset * 2));
      for (let i = 0; i < 1000; i++) {
        const t = i / sampleRate;
        data[at + i] += accent * Math.min(1, t * 1700) * Math.exp(-130 * t)
          * (0.48 * noise() + 0.23 * Math.sin(TAU * pitch * t));
      }
    }
  }
  return { click, panel, paperStep, shaker };
}

function createGraph(ctx, effects) {
  const ac = effects.ac;
  const music = ctx.audio.input({ bus: 'ambient', source: 'breakout_music', sourceLabel: 'Breakout music' });
  const nodes = [], sources = new Set();
  let alive = true, song = null, wanted = false;
  const store = node => { nodes.push(node); return node; };
  function gain(value, destination) {
    const n = store(ac.createGain()); n.gain.value = value; n.connect(destination); return n;
  }
  function filter(hz, resonance, destination) {
    const n = store(ac.createBiquadFilter()); n.type = 'lowpass'; n.frequency.value = hz; n.Q.value = resonance;
    n.connect(destination); return n;
  }
  function pan(value, destination) {
    if (!ac.createStereoPanner) return destination;
    const n = store(ac.createStereoPanner()); n.pan.value = value; n.connect(destination); return n;
  }
  function start(src, at = ac.currentTime, onEnd) {
    sources.add(src);
    src.onended = () => { sources.delete(src); src.disconnect(); onEnd?.(); };
    src.start(at);
    return src;
  }
  const balance = ctx.reduced || ctx.gentle ? 0.7 : 1;
  const bus = gain(balance, effects.node);
  const foley = makeFoley(ac);
  const stereo = Array.from({ length: 7 }, (_, i) => pan((i - 3) / 4, bus));
  function hit(buffer, volume, rate, position, delay = 0) {
    if (!alive) return;
    const out = stereo[Math.round(clamp((position + 1) / 2) * 6)];
    const env = ac.createGain(); env.gain.value = volume; env.connect(out);
    const src = ac.createBufferSource(); src.buffer = buffer; src.playbackRate.value = rate; src.connect(env);
    start(src, ac.currentTime + delay, () => env.disconnect());
  }
  const shakerGain = gain(0, bus), shakerFilter = filter(530, 0.45, shakerGain);
  const shaker = ac.createBufferSource(); shaker.buffer = foley.shaker; shaker.loop = true;
  shaker.connect(shakerFilter); start(shaker);

  const shellGain = gain(0, bus), shell = ac.createOscillator();
  shell.type = 'sine'; shell.frequency.value = 49;
  shell.connect(shellGain); start(shell);
  const overtoneGain = gain(0, bus), overtone = ac.createOscillator();
  overtone.type = 'triangle'; overtone.frequency.value = 98;
  overtone.connect(overtoneGain); start(overtone);

  // The original ten-second Breakout loop, through a soft clipping stage, tape wow and one moving reflection.
  let songGain, songFilter, echoGain, echoPan, beginSong;
  const target = (param, value, tau = 0.14) => param.setTargetAtTime(value, ac.currentTime, tau);
  if (music) {
    songGain = gain(0, music.node);
    songFilter = filter(800, 0.64, songGain);
    const delay = store(ac.createDelay(0.5)); delay.delayTime.value = 0.226;
    echoPan = pan(0.36, songGain);
    echoGain = gain(0.16, echoPan);
    delay.connect(filter(1450, 0.45, echoGain));
    songFilter.connect(delay);
    const shaper = store(ac.createWaveShaper());
    shaper.curve = Float32Array.from({ length: 513 }, (_, i) => Math.tanh((i / 256 - 1) * 1.65) / Math.tanh(1.65));
    shaper.connect(songFilter);
    let decoded = null;
    beginSong = () => {
      if (!alive || !wanted || !decoded || song) return;
      song = ac.createBufferSource(); song.buffer = decoded; song.loop = true;
      song.playbackRate.value = 0.96; song.connect(shaper); start(song);
    };
    Promise.resolve(ctx.audio.preload(SCORE)).then(buf => { decoded = buf; beginSong(); }).catch(() => {});
  }

  return {
    opening(i) {
      const position = [-0.72, 0.66, 0.18, -0.26, 0.12][i % 5];
      hit(foley.click, 0.46 + i * 0.045, 0.85 + i * 0.033, position);
      hit(foley.click, 0.14, 0.74, -position, 0.115);
    },
    step(i, depth) {
      hit(foley.paperStep, 0.26 - 0.11 * depth, i % 2 ? 0.83 : 0.95, i % 2 ? -0.28 : 0.28);
    },
    press({ index = 0, pan: position = 0, strength = 1 } = {}) {
      const power = 0.60 + 0.40 * clamp(strength), pitch = [1.0, 0.95, 1.07, 0.98][index % 4];
      hit(foley.click, 0.57 * power, 0.80 * pitch, position);
      hit(foley.panel, 0.66 * power, pitch, position * 0.3, 0.009);
      hit(foley.click, 0.11 * power, 0.72 * pitch, -position * 0.6, 0.105);
    },
    update(phase, seconds, depth) {
      const tunnel = phase === 'tunnel', capture = phase === 'capture';
      const inTube = tunnel || capture, tension = inTube ? depth ** 1.6 : 0;
      const pull = capture ? Math.min(1, seconds / 3.8) : 0;
      const exit = 1 - pull;
      if (songGain) {
        wanted = inTube;
        if (wanted) beginSong();
        const swell = capture ? 1 : Math.min(1, seconds / 2.2);
        target(songGain.gain, inTube ? (0.53 + 0.17 * depth) * swell * exit : 0, 0.16);
        target(songFilter.frequency, inTube ? (1050 + 2350 * Math.sin(Math.PI * depth) ** 2) * (1 - pull * 0.64) : 650);
        target(echoGain.gain, 0.13 + 0.16 * tension);
        if (echoPan.pan) target(echoPan.pan, Math.sin(seconds * 0.47) * (0.24 + 0.44 * tension));
        if (song) {
          const wow = 0.0035 * Math.sin(seconds * 2.2) + 0.0018 * Math.sin(seconds * 9.1);
          target(song.playbackRate, 0.964 + wow * (1 + 2.6 * tension) - 0.11 * tension - 0.34 * pull, 0.08);
        }
      }
      target(shakerGain.gain, inTube ? (0.025 + 0.54 * tension) * exit : 0, 0.11);
      target(shakerFilter.frequency, 560 + 3900 * tension);
      target(shaker.playbackRate, 0.56 + 1.88 * tension);
      const room = phase === 'finale' ? 0.010 : 0;
      target(shellGain.gain, inTube ? (0.020 + 0.12 * tension) * exit : room, 0.16);
      target(overtoneGain.gain, inTube ? (0.010 + 0.034 * tension) * exit : room * 0.5);
      target(shell.frequency, 48.4 + 15.8 * tension);
      target(overtone.frequency, 97.2 + 31.6 * tension);
    },
    stop() {
      if (!alive) return;
      alive = false;
      for (const src of [...sources]) { try { src.stop(); } catch { /* The last transient ended between frames. */ } }
      for (const node of nodes) node.disconnect();
      effects.stop(); music?.stop();
    },
  };
}

export function createButtonSound(ctx) {
  let graph = null, stopped = false, phase = '', lastMix = -Infinity, lastArm = -Infinity;
  let clickIndex = 0, stepIndex = 0, lastStep = -1, nextWordMs = 2000, wordIndex = 0, voice = null;
  ctx.audio.solo(true);

  function hush() {
    voice?.abort(); voice = null;
    if (wordIndex) ctx.audio.muffle('whispers', false);
  }
  function stop() {
    if (stopped) return;
    stopped = true;
    hush(); graph?.stop(); ctx.audio.solo(false);
  }
  ctx.cleanup(stop);
  return {
    update(nextPhase, elapsedMs, depth = 0) {
      if (stopped || ctx.paused) return;
      if (nextPhase !== phase) {
        if (phase === 'tunnel') hush();
        phase = nextPhase; lastMix = -Infinity; lastArm = -Infinity;
      }
      if (!graph && elapsedMs - lastArm >= 250) {
        lastArm = elapsedMs;
        const input = ctx.audio.input({ bus: 'sfx', source: 'button_foley', sourceLabel: 'Button ending' });
        if (input) graph = createGraph(ctx, input);
      }
      if (!graph) return;
      const d = clamp(depth);
      if (phase === 'freeze' && clickIndex < OPEN_CLICKS.length && elapsedMs >= OPEN_CLICKS[clickIndex]) {
        graph.opening(clickIndex++);
        while (clickIndex < OPEN_CLICKS.length && OPEN_CLICKS[clickIndex] <= elapsedMs) clickIndex++;
      }
      if (phase === 'tunnel') {
        const step = Math.floor(d * 26);
        if (step !== lastStep) {
          if (lastStep >= 0 && step > lastStep) graph.step(stepIndex++, d);
          lastStep = step;
        }
        if (elapsedMs >= nextWordMs && d < 0.98) {
          voice?.abort(); voice = new AbortController();
          ctx.audio.muffle('whispers', true, { hz: 1280, cut: 0.88 });
          ctx.voice(WORDS[wordIndex % WORDS.length], {
            source: 'whispers', sourceLabel: 'Whispers', gain: ctx.gentle ? 0.35 : 0.48,
            rate: 0.95 - d * 0.07, pan: wordIndex % 2 ? 0.53 : -0.56,
            fadeInMs: 72, fadeOutMs: 170, signal: voice.signal,
          });
          nextWordMs = elapsedMs + [5750, 4800, 5650, 6200][wordIndex % 4]; wordIndex++;
        }
      }
      // Smooth audio-rate interpolation between camera samples, without enqueuing parameters every render frame.
      if (elapsedMs - lastMix >= 65) {
        graph.update(phase, elapsedMs / 1000, d); lastMix = elapsedMs;
      }
    },
    press(contact) { if (!stopped && !ctx.paused) graph?.press(contact); },
    stop,
  };
}
