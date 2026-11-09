/* Unit Complete uses one mixer input. The scanner releases between targets and the rain cuts out with the CRT. */
export function unitAudio(ctx) {
  const io = ctx.audio.input({ bus: 'sfx', gain: ctx.gentle || ctx.reduced ? 0.44 : 0.78 });
  if (!io) {
    const silent = () => {};
    return { scan: silent, release: silent, dissolve: silent, off: silent, key: silent, boot: silent, stop: silent };
  }
  const ac = io.ac;
  const sources = new Set();
  let stopped = false;
  function start(node, at = ac.currentTime, end, release) {
    sources.add(node);
    node.onended = () => { sources.delete(node); node.disconnect(); release?.(); };
    node.start(at);
    if (end !== undefined) node.stop(end);
  }
  function gain(value, destination = io.node) {
    const node = ac.createGain();
    node.gain.value = value;
    node.connect(destination);
    return node;
  }
  const buffer = ac.createBuffer(1, ac.sampleRate, ac.sampleRate);
  const samples = buffer.getChannelData(0);
  for (let i = 0; i < samples.length; i++) samples[i] = Math.random() * 2 - 1;

  const rain = ac.createBufferSource();
  rain.buffer = buffer;
  rain.loop = true;
  const band = ac.createBiquadFilter();
  band.type = 'bandpass';
  band.frequency.value = 1450;
  band.Q.value = 0.55;
  const bed = gain(0);
  bed.gain.setTargetAtTime(0.028, ac.currentTime, 0.6);
  rain.connect(band).connect(bed);
  start(rain);

  const hum = ac.createOscillator();
  const mod = ac.createOscillator();
  const gate = gain(0);
  hum.frequency.value = 82;
  mod.frequency.value = 24;
  mod.connect(gain(4, hum.frequency));
  hum.connect(gate);
  start(hum);
  start(mod);

  function chirp(from, to, seconds, level, wave = 'sine', delay = 0) {
    if (stopped) return;
    const t = ac.currentTime + delay;
    const osc = ac.createOscillator();
    const env = gain(0);
    osc.type = wave;
    osc.frequency.setValueAtTime(from, t);
    osc.frequency.exponentialRampToValueAtTime(to, t + seconds);
    env.gain.setValueAtTime(0, t);
    env.gain.linearRampToValueAtTime(level, t + 0.005);
    env.gain.exponentialRampToValueAtTime(0.0001, t + seconds);
    osc.connect(env);
    start(osc, t, t + seconds + 0.01);
  }
  // A brushed, falling air band follows the cut. Two short, increasingly high glass partials give it a tail.
  // The buffer is shared by all the objects in this take; each object only starts one pitched source.
  const vapor = ac.createBuffer(1, Math.ceil(0.26 * 24000), 24000);
  const vaporSamples = vapor.getChannelData(0);
  let high = 0, low = 0;
  for (let i = 0; i < vaporSamples.length; i++) {
    const t = i / vapor.sampleRate, u = i / vaporSamples.length;
    const noise = Math.random() * 2 - 1;
    high += (0.49 - 0.27 * u) * (noise - high);
    low += 0.095 * (high - low);
    const attack = Math.min(1, t / 0.014), tail = (1 - u) ** 2;
    const air = (high - low) * 0.73 * Math.exp(-6 * t);
    const ring = Math.sin(2 * Math.PI * (660 * t + 290 * t * t)) * 0.12 * Math.exp(-17 * t);
    const glint = Math.sin(2 * Math.PI * 1318.5 * t) * 0.045 * Math.sin(Math.PI * u) * Math.exp(-7 * t);
    vaporSamples[i] = (air + ring + glint) * attack * tail;
  }
  const panBuses = Array.from({length: 7}, (_, i) => {
    if (!ac.createStereoPanner) return io.node;
    const panner = ac.createStereoPanner();
    panner.pan.value = (i - 3) / 4;
    panner.connect(io.node);
    return panner;
  });
  const pitches = [1, 1.122, 0.944, 1.189, 1.059];
  let note = 0;
  return {
    scan(pace) {
      hum.frequency.setTargetAtTime(82 + 96 * pace, ac.currentTime, 0.09);
      gate.gain.setTargetAtTime(0.12, ac.currentTime, 0.035);
    },
    release() { gate.gain.setTargetAtTime(0, ac.currentTime, 0.045); },
    dissolve(pan = 0, batchSize = 1, index = 0) {
      if (stopped) return;
      const panIndex = Math.round((Math.max(-1, Math.min(1, pan)) + 1) * 3);
      const level = gain(0.76 / Math.sqrt(batchSize), panBuses[panIndex]);
      const source = ac.createBufferSource();
      source.buffer = vapor;
      source.playbackRate.value = pitches[note++ % pitches.length];
      source.connect(level);
      const delay = batchSize > 1 ? 0.042 * index / (batchSize - 1) : 0;
      start(source, ac.currentTime + delay, undefined, () => level.disconnect());
    },
    off() {
      gate.gain.setTargetAtTime(0, ac.currentTime, 0.012);
      bed.gain.setTargetAtTime(0, ac.currentTime, 0.06);
      chirp(110, 37, 0.18, 0.3);
      chirp(1660, 170, 0.47, 0.09, 'triangle', 0.015);
    },
    key() { chirp(720, 430, 0.027, 0.07, 'triangle'); },
    boot() { chirp(540, 810, 0.14, 0.13); },
    stop() {
      if (stopped) return;
      stopped = true;
      for (const node of sources) node.stop();
      for (const bus of panBuses) if (bus !== io.node) bus.disconnect();
      io.stop();
    },
  };
}
