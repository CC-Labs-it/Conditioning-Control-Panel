import { selectedCourse } from './catalog.js';

// Only bundled courses may be selected. A track name never becomes an arbitrary fetch URL.
export function selectTrack(search = '') {
  return selectedCourse(search).id;
}

export function allowsOnline(trackId) { return trackId === 'threshold'; }

// Mainframe stays silent until it has its own song or the player explicitly chooses one.
export function courseSongs(trackId, search, charts) {
  const chart = new URLSearchParams(search).get('chart');
  return ['mainframe', 'goonworks'].includes(trackId) && !chart?.trim() ? [] : charts;
}
