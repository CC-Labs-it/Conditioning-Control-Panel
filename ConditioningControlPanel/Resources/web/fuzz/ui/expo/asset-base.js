import {expoAllowed, expoBase} from '../edition.js';

const gameDirectory = new URL('../../', import.meta.url);

export function assetURL(path = '') {
  if (!expoAllowed()) throw new Error('The Expo transmission is unavailable.');
  const base = new URL(expoBase(), gameDirectory);
  const file = new URL(path, base);
  if (file.protocol !== base.protocol || file.host !== base.host || !file.pathname.startsWith(base.pathname)) {
    throw new TypeError('Expo asset lies outside its pack directory');
  }
  return file.href;
}
