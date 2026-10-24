/** Courses shipped in this build. Online admission remains on the server's Threshold card. */
export const COURSES = Object.freeze([
  Object.freeze({ id: 'threshold', name: 'Threshold', online: true }),
  Object.freeze({ id: 'mainframe', name: 'Mainframe', online: false }),
  Object.freeze({ id: 'keep', name: 'The Keep', online: false }),
]);
export function selectedCourse(search = '') {
  const id = new URLSearchParams(search).get('track');
  return COURSES.find(c => c.id === id) || COURSES[0];
}
/** A course change leaves any room invite behind and keeps the player's local preview options. */
export function courseUrl(href, id) {
  const course = COURSES.find(c => c.id === id);
  if (!course) return null;
  const url = new URL(href);
  url.searchParams.set('track', course.id);
  for (const key of ['r', 'lobby', 'room', 'uid', 'name', 'tier']) url.searchParams.delete(key);
  return url.href;
}
