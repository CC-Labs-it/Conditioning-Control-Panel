/* Keep residency is a replacement plan: one quality tier, one sky, no Threshold props. */
export const KEEP_REPLACES = Object.freeze({
  'road-bands-mask': 'keep-road-seams-mask',
  'road-glass-roughness': 'keep-road-roughness',
  'rail-glow-mask': 'keep-rail-mask',
  'sky-deep-galaxy': 'keep-sky-galaxy',
});
export const KEEP_COLOURS = Object.freeze(['#521744', '#ed3e9d', '#100e1c']);

export function keepArtPlan(want, tier) {
  const phone = tier === 'phone', suffix = phone ? '-phone' : '', lod = phone ? 1 : 0;
  const aliases = Object.fromEntries(Object.entries(KEEP_REPLACES).map(([slot, id]) => [slot, id + suffix]));
  for (const id of ['dawn', 'deep', 'afterglow']) {
    aliases[`sky-${id}-galaxy`] = 'keep-sky-galaxy' + suffix;
    aliases[`sky-${id}-stars`] = 'sky-deep-stars';
  }
  aliases['hazard-keep-footprint'] = 'hazard-keep-footprint' + suffix;
  const ids = want.filter((id) => !/^(chess-|slot-cabinet|threshold-slot|stone-threshold|card-(drop|lens|orbit|petal)$|pocket-watch$|spiral-disc$)/.test(id))
    .map((id) => aliases[id] || id);
  ids.push(...Object.values(aliases), 'stone-keep', 'glide-keep-beanie', 'hazard-keep-portcullis',
    ...['keep-colossus-padlock', 'keep-colossus-chainpile', 'keep-portcullis-arch'].map((id) => `${id}-lod${lod}`));
  return { ids: [...new Set(ids)], aliases };
}
