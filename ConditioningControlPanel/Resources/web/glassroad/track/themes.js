// Course identity stays visible even when the player chooses different pictures or music.
export const COURSE_THEMES = Object.freeze({
  threshold: Object.freeze({ label: 'Bambi', lyrics: 'Bimbo hypnosis' }),
  mainframe: Object.freeze({ label: 'Drone', lyrics: 'Drone programming' }),
  keep: Object.freeze({ label: 'Locked / Circe', lyrics: 'Chastity and denial. Mentions cock.' }),
  boudoir: Object.freeze({ label: 'Sissy', lyrics: 'Feminisation' }),
  goonworks: Object.freeze({ label: 'Goon', lyrics: 'Gooning and relapse' }),
});

export const courseTheme = id => COURSE_THEMES[id] || null;
