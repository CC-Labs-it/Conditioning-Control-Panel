/** Courses shipped in this build. All shipped courses have matching online room cards. */
export const COURSES = Object.freeze([
  Object.freeze({ id: 'threshold', name: 'Threshold', online: true }),
  Object.freeze({ id: 'mainframe', name: 'Mainframe', online: true }),
  Object.freeze({ id: 'keep', name: 'The Keep', online: true }),
  Object.freeze({ id: 'boudoir', name: 'Boudoir', online: true }),
  Object.freeze({ id: 'goonworks', name: 'GOONWORKS', online: true }),
]);
export function selectedCourse(search = '') {
  const id = new URLSearchParams(search).get('track');
  return COURSES.find(c => c.id === id) || COURSES[0];
}
/** A course change leaves any room invite behind and keeps the player's local preview options. */
export function courseUrl(href, id, { race = false } = {}) {
  const course = COURSES.find(c => c.id === id);
  if (!course) return null;
  const url = new URL(href);
  url.searchParams.set('track', course.id);
  url.searchParams.delete('start');
  if (race) url.searchParams.set('start', 'race');
  for (const key of ['r', 'lobby', 'room', 'uid', 'name', 'tier']) url.searchParams.delete(key);
  return url.href;
}

/** Only an explicit chooser transition starts a solo race when its course finishes loading. */
export function courseRaceIntent(search = '') {
  const q = new URLSearchParams(search);
  if (q.get('start') !== 'race' || q.has('r')) return null;
  return COURSES.find(c => c.id === q.get('track'))?.id || null;
}
