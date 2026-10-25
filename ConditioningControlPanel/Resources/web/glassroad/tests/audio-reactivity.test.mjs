import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createMusic } from '../audio/music.js';
import { createMusicReactivity } from '../audio/reactivity.js';

function audioFixture(sampleRate = 48000) {
  const links = [], sources = [], buffers = new Set();
  let analysers = 0, reads = 0, rms = 0.2, frequency = 94;
  const node = () => ({ connect(to) { links.push([this, to]); }, disconnect() { this.disconnected = true; } });
  const ctx = {
    state: 'running', currentTime: 0, sampleRate,
    createGain() {
      const gain = { value: 1, cancelScheduledValues() {}, setValueAtTime(v) { this.value = v; }, setTargetAtTime(v) { this.value = v; } };
      return Object.assign(node(), { gain });
    },
    createAnalyser() {
      analysers++;
      return Object.assign(node(), {
        fftSize: 1024, get frequencyBinCount() { return this.fftSize / 2; },
        getFloatTimeDomainData(into) { buffers.add(into); reads++; into.fill(rms); },
        getFloatFrequencyData(into) {
          buffers.add(into); into.fill(-Infinity);
          const bin = Math.round(frequency / (sampleRate / this.fftSize));
          if (bin < into.length) into[bin] = -12;
        },
      });
    },
    createBufferSource() {
      const s = Object.assign(node(), { start() {}, stop() {} }); sources.push(s); return s;
    },
    decodeAudioData: async () => ({ duration: 30 }),
  };
  return { ctx, links, sources, buffers, get analysers() { return analysers; }, get reads() { return reads; },
    level(v) { rms = v; }, frequency(v) { frequency = v; } };
}
const file = { arrayBuffer: async () => new ArrayBuffer(4) };
test('measured melodic half-octaves select different tiles and silence clears every note', () => {
  const f = audioFixture(), reactive = createMusicReactivity(f.ctx,f.ctx.createGain());
  for(let band=0;band<8;band++) {
    reactive.clear(); f.frequency(250*2**((band+.5)/2));
    for(let frame=0;frame<120;frame++)reactive.read(1/60,{playing:true});
    for(let other=0;other<8;other++) assert.equal(reactive.frame['note'+other]>0,other===band);
  }
  f.level(0); reactive.read(1/60,{playing:true});
  for(let band=0;band<8;band++)assert.equal(reactive.frame['note'+band],0);
  reactive.dispose();
});
const fetchFn = async () => ({ ok: true, json: async () => ({ bpm: 120, sections: [] }), arrayBuffer: file.arrayBuffer });
function settle(music, opts) { for (let i = 0; i < 90; i++) music.readReactivity(1 / 60, opts); return music.reactivity; }

test('music-only analyser is made once and chart/file playback reuse frame and bounded buffers', async () => {
  const f = audioFixture(), out = f.ctx.createGain();
  const music = createMusic(f.ctx, out, null, { fetchFn });
  const frame = music.reactivity;
  assert.equal(f.analysers, 1);
  await music.playChart('/track.json');
  const chartFrame = settle(music);
  assert.equal(chartFrame, frame);
  assert.ok(frame.active && frame.bass > 0.7 && frame.mid === 0 && frame.treble === 0);
  await music.playFile(file);
  f.ctx.currentTime = 7;
  for (let i = 0; i < 600; i++) assert.equal(music.readReactivity(1 / 60), frame);
  assert.equal(frame.songS, 7, 'picked tracks carry their song clock');
  assert.equal(f.analysers, 1, 'no analyser created for subsequent tracks or frames');
  assert.equal(f.buffers.size, 2, 'one reused FFT and one waveform buffer');
  const analyserLink = f.links.find(([, n]) => typeof n.getFloatFrequencyData === 'function');
  assert.ok(analyserLink);
  assert.ok(f.links.some(([from, to]) => from === analyserLink[0] && to === out), 'tap on the same music gain that feeds output');
  assert.ok(f.sources.every((s) => f.links.some(([from, to]) => from === s && to === analyserLink[0])));
  music.dispose();
  assert.equal(frame.active, false); assert.equal(frame.energy, 0);
  assert.equal(analyserLink[1].disconnected, true);
});

test('bands follow Hz boundaries across sample rates; no song clock or SFX creates a beat', async () => {
  for (const sampleRate of [32000, 44100, 48000]) {
    const f = audioFixture(sampleRate), music = createMusic(f.ctx, f.ctx.createGain(), null);
    await music.playFile(file);
    for (const [hz, band] of [[94, 'bass'], [1000, 'mid'], [6000, 'treble']]) {
      music.stop(); await music.playFile(file); f.frequency(hz);
      const v = settle(music);
      assert.ok(v[band] > 0.7, `${sampleRate}Hz context / ${hz}Hz ${band}`);
      for (const other of ['bass', 'mid', 'treble']) if (other !== band) assert.equal(v[other], 0);
      assert.ok(v.energy > 0.5 && v.energy <= 1);
    }
    f.level(0); f.ctx.currentTime = 9;
    const silent = music.readReactivity(1 / 60);
    assert.equal(silent.active, false);
    for (const key of ['bass', 'mid', 'treble', 'energy']) assert.equal(silent[key], 0, 'silent waveform overrides FFT residue');
    music.stop(); f.level(1); f.ctx.currentTime = 12;
    assert.equal(music.readReactivity(1 / 60).energy, 0, 'no playing source, even if other sound exists');
    music.dispose();
  }
});

test('pause and suspension hold the envelope and clock; mute and stop clear even when suspended', async () => {
  const f = audioFixture(), music = createMusic(f.ctx, f.ctx.createGain(), null);
  await music.playFile(file); f.ctx.currentTime = 2;
  settle(music);
  const held = { ...music.reactivity }, reads = f.reads;
  f.level(0); f.ctx.currentTime = 6;
  assert.deepEqual(music.readReactivity(1, { paused: true }), { ...held, frozen: true });
  f.ctx.state = 'suspended';
  assert.deepEqual(music.readReactivity(1), { ...held, frozen: true });
  assert.equal(f.reads, reads, 'no analyser work while frozen');
  assert.equal(music.readReactivity(1, { muted: true }).energy, 0);
  f.ctx.state = 'running'; f.level(0.2); settle(music);
  assert.ok(music.reactivity.energy > 0);
  music.setVolume(0);
  assert.equal(music.reactivity.energy, 0, 'voice mute clears immediately');
  assert.equal(music.readReactivity(1).active, false);
  music.setVolume(1); settle(music);
  f.ctx.state = 'suspended'; music.stop();
  assert.equal(music.reactivity.active, false); assert.equal(music.reactivity.songS, 0);
  music.dispose();
});

test('Gentle has a >=300ms attack and reduced envelope; normal also rises smoothly', () => {
  const f = audioFixture(), reactive = createMusicReactivity(f.ctx, f.ctx.createGain());
  const opts = { playing: true, gentle: false, songS: 3 };
  f.level(1);
  const normal = reactive.read(1 / 60, opts).energy;
  assert.ok(normal > 0 && normal < 0.15);
  reactive.clear(); opts.gentle = true;
  const gentle = reactive.read(1 / 60, opts).energy;
  assert.ok(gentle <= 0.55 * (1 - Math.exp(-(1 / 60) / 0.3)), 'attack no faster than 300 ms');
  assert.ok(gentle < normal / 2);
  for (let i = 0; i < 300; i++) reactive.read(1 / 60, opts);
  assert.ok(reactive.frame.energy <= 0.55);
  reactive.clear(); opts.gentle = false;
  assert.ok(reactive.read(10, opts).energy < 0.6, 'a long frame cannot jump to peak');
  reactive.dispose();
});

test('unavailable analyser degrades to a silent stable frame; ended sources disconnect', async () => {
  const f = audioFixture(); f.ctx.createAnalyser = undefined;
  const music = createMusic(f.ctx, f.ctx.createGain(), null);
  await music.playFile(file);
  const frame = music.readReactivity(1 / 60);
  assert.equal(frame.active, false); assert.equal(frame.energy, 0);
  const source = f.sources[0]; source.onended();
  assert.equal(source.disconnected, true); assert.equal(music.playing, false);
  music.dispose();
});

test('boot sends sampled real music frame and picked-file song time to every world update', () => {
  const boot = readFileSync(new URL('../boot.js', import.meta.url), 'utf8');
  assert.match(boot, /songS = inRace && music\?\.playing \? music\.songS : NaN/);
  assert.match(boot, /opts\.volMaster === 0 \|\| opts\.volMusic === 0/);
  assert.equal([...boot.matchAll(/music\.readReactivity\(/g)].length, 1);
  assert.match(boot, /paused, song, songS, audioReactive, words:/);
});
