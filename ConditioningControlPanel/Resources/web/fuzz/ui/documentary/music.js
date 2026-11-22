import { createMuseumMusic } from '../../audio/museum-music.js';
export { MUSEUM_TRACKS } from '../../audio/museum-music.js';

export function createDocumentaryMusic(ctx, tunnel, options = {}) {
  return createMuseumMusic(ctx, {
    source: 'documentary-music', label: 'Museum music', level: .44, speechLevel: .14,
    document: tunnel.captions?.ownerDocument, ready: tunnel.ready, retryMissing: true,
    playWhenHidden: () => tunnel.state.docked,
    bindVisit: (dispose, sync) => [tunnel.on('dispose', dispose), tunnel.on('arrive', sync)],
    ...options,
  });
}
