// Only bundled courses may be selected. A track name never becomes an arbitrary fetch URL.
export function selectTrack(search = '') {
  return new URLSearchParams(search).get('track') === 'mainframe' ? 'mainframe' : 'threshold';
}

export function allowsOnline(trackId) { return trackId === 'threshold'; }
