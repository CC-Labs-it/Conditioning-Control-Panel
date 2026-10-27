/* render/musicRoad.js - drives the road, rails and helix from the song (handoff-1022/MUSIC-ROAD-BRIEF.md).
 *
 * Each frame: musicFrame (render/musicRoadMath.js) at the song position the player hears, then the
 * numbers go to the road material's music uniforms, the rail tube and the helix. The one stateful bit is
 * the flash budget: the first frame of each shatter and each drop wave asks photosafe once; a refused
 * (or Gentle) event keeps its shape and loses its flash. The event index keys that grant, not a timer, so
 * a pause or a seek cannot grant twice. No song: every uniform rests and the road plays as it did.
 */
import { musicFrame, createMusicFrame } from './musicRoadMath.js';
import { energyTexture } from './materials/road.js';
import { createHelix } from './helix.js';

export const MUSIC_FLASH = Object.freeze({ shatter: 0.55, wave: 0.3 });

/** @returns {{ helix: object|null, frame: object, setSong(song: object|null): void, update(song: object|null, tS: number, ctx: object): object, dispose(): void }} */
export function createMusicRoad({ road, rails = null, track, pal, photosafe = null, scene = null, art = null, helixQuietRange = null }) {
  const mf = createMusicFrame();
  let helix = null;
  try { helix = createHelix(track, pal, undefined, art, helixQuietRange); if (scene) scene.add(helix.object); } catch (e) { console.warn('[glassroad/music] no helix: ' + (e && e.message)); }
  let song = null, energy = null, granted = { shatter: -1, wave: -1 }, flashOk = { shatter: false, wave: false };
  const mu = road.uniforms.music;

  function setSong(s) {
    if (s === song) return;
    song = s;
    if (energy) { energy.texture.dispose(); energy = null; }
    if (s && road.uniforms.energyNode) {
      energy = energyTexture(s);
      road.uniforms.energyNode.value = energy.texture;
      mu.eW.value = energy.width; mu.eFps.value = energy.fps;
    }
    granted = { shatter: -1, wave: -1 };
  }

  /** One flash grant per event: asks photosafe on the event's first frame, remembers the answer. */
  function flash(kind, idx, gentle) {
    if (idx < 0) return false;
    if (granted[kind] !== idx) {
      granted[kind] = idx;
      flashOk[kind] = !gentle && (!photosafe || photosafe.request('music-' + kind));
    }
    return flashOk[kind];
  }

  function update(s, tS, { kartS = 0, gentle = false } = {}) {
    setSong(s);
    musicFrame(s, tS, { gentle }, mf);
    if (mu.tileBeat) {
      mu.tileBeat.value = mf.beatIndex + mf.beatPhase;
      mu.tileBar.value = mf.barIndex + mf.barPhase;
      mu.tileOn.value = mf.on ? 1 : 0;
      mu.tileGentle.value = gentle ? 1 : 0;
    }
    mu.t.value = mf.tS; mu.kartS.value = kartS;
    mu.vib.value = mf.vibM; mu.melody.value = mf.melodyM;
    mu.bassW.value = mf.on ? mf.bass * 0.7 * (1 - mf.calm * 0.5) * (gentle ? 0.5 : 1) : 0;
    mu.ripAge.value = mf.rippleAge; mu.ripAmp.value = mf.rippleAmp;
    mu.waveD.value = mf.waveD; mu.waveAmp.value = mf.waveAmp;
    mu.shatter.value = mf.shatter;
    mu.echoL.value = mf.echoL; mu.echoR.value = mf.echoR;
    mu.calm.value = mf.calm; mu.big.value = mf.big;
    const fl = (flash('shatter', mf.shatterIdx, gentle) ? mf.shatter * MUSIC_FLASH.shatter : 0)
      + (flash('wave', mf.waveIdx, gentle) ? mf.waveAmp * MUSIC_FLASH.wave : 0);
    mu.flash.value = fl;
    if (rails && rails.uniforms.pulse) {
      rails.uniforms.pulse.value = mf.on ? (mf.bass * 0.35 + mf.kick * 0.3) * (gentle ? 0.4 : 1) * (1 - mf.calm * 0.6) : 0;
      rails.uniforms.vib.value = mf.vibM; rails.uniforms.t.value = mf.tS;
    }
    if (helix) helix.update(mf, { kartS, gentle });
    return mf;
  }

  return { helix, frame: mf, setSong, update,
    dispose() { if (helix) helix.dispose(); if (energy) energy.texture.dispose(); } };
}
