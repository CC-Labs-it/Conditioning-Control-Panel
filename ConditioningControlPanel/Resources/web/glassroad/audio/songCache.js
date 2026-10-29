// Fetch the selected recording while graphics load. No context, playback or disk cache.
import { chartAudioUrl, validChart } from './music.js';

export function createSongCache({ fetchFn = globalThis.fetch } = {}) {
  const requests = new Map();
  async function fetchSong(url) {
    const key = String(url);
    if (!requests.has(key)) {
      const pending = Promise.resolve().then(() => fetchFn(url)).then(async response => {
        if (!response.ok) { requests.delete(key); return response; }
        // Consume the network body now; later playback gets a fresh in-memory response.
        const bytes = await response.arrayBuffer();
        return new Response(bytes, { status: response.status, headers: response.headers });
      }).catch(error => { requests.delete(key); throw error; });
      requests.set(key, pending);
    }
    return (await requests.get(key)).clone();
  }
  async function preload(url) {
    try {
      const response = await fetchSong(url);
      if (!response.ok) return false;
      const chart = await response.json();
      return validChart(chart) && (await fetchSong(chartAudioUrl(url, chart))).ok;
    } catch { return false; }
  }
  return { fetch: fetchSong, preload, clear: () => requests.clear() };
}
