/** Match the shipped course and tuning before admitting a kart to an online room. */
export async function courseHash(trackJson, feelJson) {
  const bytes = new TextEncoder().encode(JSON.stringify([trackJson, feelJson]));
  const digest = await globalThis.crypto.subtle.digest('SHA-256', bytes);
  return Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, '0')).join('');
}
