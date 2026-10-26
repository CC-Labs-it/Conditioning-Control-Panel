import { selectedCourse } from './catalog.js';

// Only bundled courses may be selected. A track name never becomes an arbitrary fetch URL.
export function selectTrack(search = '') {
  return selectedCourse(search).id;
}

export function allowsOnline(trackId) { return trackId === 'threshold'; }

// The signed catalog is shared by all courses. A course must never borrow another recording.
export const COURSE_SONG_IDS = Object.freeze({ threshold: 'pink-road', mainframe: 'mainframe', keep: 'keep', goonworks: 'goonworks', boudoir: 'boudoir' });
export function courseSongRows(trackId, songs) {
  const id = COURSE_SONG_IDS[trackId];
  return id && Array.isArray(songs) ? songs.filter(song => song?.id === id) : [];
}

// Loopback previews have private course recordings. Other hosts need their own chart.
export function courseSongs(trackId, search, charts) {
  const chart = new URLSearchParams(search).get('chart');
  if (trackId === 'mainframe' && !chart?.trim() && charts.includes('/private-audio/pink-road/chart.json')) {
    return ['/private-audio/tracks-2026-10-24/mainframe/out/v3/race-chart.json'];
  }
  if (trackId === 'keep' && !chart?.trim() && charts.includes('/private-audio/pink-road/chart.json')) return ['/private-audio/keep/race-chart.json'];
  if (trackId === 'goonworks' && !chart?.trim() && charts.includes('/private-audio/pink-road/chart.json')) return ['/private-audio/goonworks/race-chart.json'];
  if (trackId === 'boudoir' && !chart?.trim() && charts.includes('/private-audio/pink-road/chart.json')) return ['/private-audio/boudoir/race-chart.json'];
  if (['mainframe', 'keep', 'goonworks', 'boudoir'].includes(trackId) && !chart?.trim()) {
    const own = '/glassroad-audio/' + COURSE_SONG_IDS[trackId] + '/chart.json';
    return charts.filter(url => url === own);
  }
  return charts;
}
