export function artBase(host = globalThis.location?.host) {
  return host === 'ccp.game' ? 'https://ccp.art/' : '/__art__/';
}
