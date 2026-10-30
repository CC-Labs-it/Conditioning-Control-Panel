// Shared decoder limits for the provider, host and prewarmed renderer pool.
export const MAX_MEDIA_CLIPS = 6;

export function mediaClipLimit(tier) {
  return tier === 'phone' ? 3 : tier === 'mid' ? 4 : MAX_MEDIA_CLIPS;
}
