// Only bundled courses may be selected. A track name never becomes an arbitrary fetch URL.
export function selectTrack(search = '') {
  return new URLSearchParams(search).get('track') === 'mainframe' ? 'mainframe' : 'threshold';
}

export function allowsOnline(trackId) { return trackId === 'threshold'; }

// Mainframe stays silent until it has its own song or the player explicitly chooses one.
export function courseSongs(trackId, search, charts) {
  const chart = new URLSearchParams(search).get('chart');
  return trackId === 'mainframe' && !chart?.trim() ? [] : charts;
}
