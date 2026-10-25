/* Four short takes, then the exact starting chase pose. Pure track geometry, no scene clock. */
export const COURSE_INTRO_SECONDS = 6.6;
const TAKE_SECONDS = 1.5;
const clamp = x => Math.max(0, Math.min(1, x));
const ease = x => { const u = clamp(x); return u * u * u * (u * (u * 6 - 15) + 10); };

// Authored districts keep each course's three reveals distinct. Camera rails stay inside the
// road corridor, below room ceilings, rather than orbiting through the surrounding architecture.
const DISTRICTS = {
  threshold: [['tidal-gallery', .34], ['prism-observatory', .24], ['cascade-return', .4]],
  mainframe: [['core-approach', .72], ['spiral-core', .26], ['spillway', .44]],
  keep: [['crown-ramparts', .18], ['prison-courtyard', .28], ['moat-escape', .3]],
  boudoir: [['perfume-court', .38], ['shelf-gallery', .44], ['bath-approach', .46]],
  goonworks: [['screen-grotto', .35], ['pop-quiz', .36], ['leapfrog', .38]],
};

export function courseIntroShots(track) {
  const defaults = [0, Math.floor(track.sections.length / 3), Math.floor(track.sections.length * 2 / 3)];
  const specs = DISTRICTS[track.id] || defaults.map(i => [track.sections[i].id, .4]);
  return specs.map(([id, fraction], i) => {
    const section = track.sections.find(s => s.id === id) || track.sections[defaults[i]];
    const length = section.toS > section.fromS ? section.toS - section.fromS : track.lengthM - section.fromS;
    const s = section.fromS + length * fraction;
    return Object.freeze({ section: section.id, s, side: i === 1 ? -1 : 1,
      height: i === 0 ? 7 : 5.5, behind: i === 1 ? 20 : 25, fov: i === 0 ? 52 : 48 });
  });
}

export function courseIntroShotAt(seconds) {
  return Math.min(3, Math.floor(Math.max(0, Number.isFinite(seconds) ? seconds : 0) / TAKE_SECONDS));
}

const at = (f, lateral, height) => f.p.map((p, i) => p + f.b[i] * lateral + f.n[i] * height);

/** Deterministic rail poses. Gentle uses near-still takes and a smaller final crane move. */
export function sampleCourseIntro(track, shots, seconds, gentle = false) {
  const time = Number.isFinite(seconds) ? Math.max(0, seconds) : 0;
  const shot = courseIntroShotAt(time);
  if (shot < 3) {
    const spec = shots[shot], u = ease((time - shot * TAKE_SECONDS) / TAKE_SECONDS);
    const pan = (u - .5) * (gentle ? 1.2 : 9);
    const f = track.frameAt(spec.s - spec.behind + pan);
    const lookFrame = track.frameAt(spec.s + 12 + pan * .3);
    const lateral = Math.min(3.5, f.w * .14) * spec.side;
    return { shot, section: spec.section, eye: at(f, lateral, spec.height),
      look: at(lookFrame, 0, 1.4), up: [0, 1, 0], fov: spec.fov, blend: 0 };
  }
  const spawn = track.spawnPose(0), f = track.frameAt(spawn.s);
  const move = gentle ? .35 : 1;
  // This take cuts back to the actual grid. Only this nearby crane shot travels into the POV.
  return { shot, section: track.sections[track.sectionAt(spawn.s)].id,
    eye: spawn.p.map((p, i) => p - f.t[i] * (9 + 9 * move) + f.n[i] * (3 + 7 * move) + f.b[i] * 4 * move),
    look: spawn.p.map((p, i) => p + f.n[i] * 1.1), up: [0, 1, 0], fov: 50,
    blend: ease((time - TAKE_SECONDS * 3) / (COURSE_INTRO_SECONDS - TAKE_SECONDS * 3)) };
}
