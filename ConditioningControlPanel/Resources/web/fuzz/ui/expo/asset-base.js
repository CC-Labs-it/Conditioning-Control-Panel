const defaultBase = new URL('../../assets/expo/', import.meta.url);
let base = defaultBase;

// Configure the directory before accepting a transmission. A null base selects the bundled pack.
export function setExpoAssetBase(directory) {
  if (directory == null) { base = defaultBase; return; }
  const next = new URL(directory, globalThis.document?.baseURI || new URL('../../', import.meta.url));
  if (!next.pathname.endsWith('/')) next.pathname += '/';
  base = next;
}

export function assetURL(path = '') {
  const file = new URL(path, base);
  if (file.protocol !== base.protocol || file.host !== base.host || !file.pathname.startsWith(base.pathname)) {
    throw new TypeError('Expo asset lies outside its pack directory');
  }
  return file.href;
}
