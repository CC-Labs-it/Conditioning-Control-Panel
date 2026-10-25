import test from 'node:test';
import assert from 'node:assert/strict';
import { createBus } from '../core/events.js';
import { createFx } from '../fx/reactions.js';
import { createGentle } from '../fx/gentle.js';
import { createPhotosafe } from '../fx/photosafe.js';
import { createCamRig } from '../render/cameraMath.js';
import { createMusicEdgeState } from '../render/musicEdges.js';
import { normalizeChart } from '../audio/chart.js';
import { setLiveMusic } from '../audio/songShape.js';
import { makeFeel, fakeTrack, place, DT } from './sim-fake.mjs';

test('a loud song, rhythmic phrases and drops animate the border without changing the camera pose or FOV', () => {
  const track = fakeTrack(), feel = makeFeel(), bus = createBus();
  const fx = createFx(bus, feel, { gentle: createGentle(feel, { search: '' }), photosafe: createPhotosafe(feel) });
  const musicRig = createCamRig(feel), quietRig = createCamRig(feel), edge = createMusicEdgeState();
  musicRig.useVel = quietRig.useVel = false;
  const song = normalizeChart({ bpm: 120, durationS: 64,
    beats: Array.from({ length: 128 }, (_, i) => i * .5),
    downbeats: Array.from({ length: 32 }, (_, i) => i * 2),
    sections: [{ startS: 0, mood: 'drop', energy: 1 }] });
  const voice = { song, songS: 0, playing: true, analysis: null };
  let borderMoved = false;
  setLiveMusic(voice);
  try {
    for (let frame = 0; frame < 600; frame++) {
      const time = frame * DT, kart = place(track, { s: 40 + time * 30, speed: 30 });
      voice.songS = time;
      if (frame % 30 === 0) {
        bus.emit('phrase-gate', { id: 'phrase', s: time * 30 });
        bus.emit('word-pop', { text: 'TEST', p: [0, 0, 0] });
        bus.emit('press-thud', { s: time * 30, strength: 1 });
      }
      fx.update(DT, kart);
      const signal = .5 + .5 * Math.cos(time * Math.PI * 4);
      const border = edge.update(DT, { enabled: true, beatSong: song, songS: time,
        audioReactive: { active: true, energy: signal, bass: signal, mid: signal, treble: signal } });
      borderMoved ||= Math.abs(border.shakePx) > .01;
      if (!frame) { musicRig.snap(kart, 16 / 9, fx.state, track); quietRig.snap(kart, 16 / 9, null, track); }
      else { musicRig.update(kart, fx.state, DT, 16 / 9, track); quietRig.update(kart, null, DT, 16 / 9, track); }
      assert.deepEqual(musicRig.eye, quietRig.eye, 'music cannot move the eye');
      assert.deepEqual(musicRig.look, quietRig.look, 'music cannot tilt the aim');
      assert.deepEqual(musicRig.up, quietRig.up, 'music cannot roll the horizon');
      assert.equal(musicRig.vFov, quietRig.vFov, 'music cannot zoom the scene');
    }
    assert.ok(borderMoved, 'the approved music border still responds');
  } finally { setLiveMusic(null); fx.dispose(); }
});
