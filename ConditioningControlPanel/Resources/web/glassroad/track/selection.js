import { selectedCourse } from './catalog.js';

// Only bundled courses may be selected. A track name never becomes an arbitrary fetch URL.
export function selectTrack(search = '') {
  return selectedCourse(search).id;
}

export function allowsOnline(trackId) { return trackId === 'threshold'; }

// Loopback previews have private course recordings. Other hosts need their own chart.
export function courseSongs(trackId, search, charts) {
  const chart = new URLSearchParams(search).get('chart');
  if (trackId === 'mainframe' && !chart?.trim() && charts.includes('/private-audio/pink-road/chart.json')) {
    return ['/private-audio/tracks-2026-10-24/mainframe/out/v3/race-chart.json'];
  }
  if (trackId === 'keep' && !chart?.trim() && charts.includes('/private-audio/pink-road/chart.json')) return ['/private-audio/locked-cock/out/v3/race-chart.json'];
  if (trackId === 'goonworks' && !chart?.trim() && charts.includes('/private-audio/pink-road/chart.json')) return ['/private-audio/goonworks/race-chart.json'];
  return ['mainframe', 'keep', 'goonworks'].includes(trackId) && !chart?.trim() ? [] : charts;
}
