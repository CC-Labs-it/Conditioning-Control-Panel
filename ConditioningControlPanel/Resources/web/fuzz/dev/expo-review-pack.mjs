// Read the same edition-selected directory that the visit is about to use.
export async function reviewPack(page) {
  const game=page.frames().find(frame=>new URL(frame.url()).pathname.endsWith('/index.html'));
  if (!game) throw new Error('Expo review requires the game frame');
  return game.evaluate(async()=>{
    const {assetURL}=await import('./ui/expo/asset-base.js');
    window.expoReviewAssetURL=assetURL;
    return assetURL();
  });
}

export function packFile(url, root, directory='') {
  const prefix=root && root+directory;
  if (!prefix || !url.startsWith(prefix)) return '';
  return url.slice(prefix.length).split(/[?#]/,1)[0];
}
