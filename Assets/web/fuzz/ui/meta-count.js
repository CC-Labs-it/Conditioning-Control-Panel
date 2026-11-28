/* Public, anonymous game counters. Keep the last confirmed total for offline endings. */
const API = 'https://codebambi-proxy.vercel.app/v2/tc';
const STORAGE = 'tc.meta-count.v1.';
const DEV_PARAMS = ['preview', 'fresh', 'buy', 'pace', 'mood', 'theme', 'focus', 'waveStudy', 'shop', 'wallet', 'equip', 'style', 'icons', 'sky'];

function liveGame() {
  const page = globalThis.location;
  if (page?.origin !== 'https://tc.cclabs.app') return false;
  const params = new URLSearchParams(page.search);
  return !page.pathname.includes('/dev/') && !DEV_PARAMS.some(key => params.has(key));
}

const valid = n => Number.isSafeInteger(n) && n >= 0;

function cached(key) {
  try {
    const value = JSON.parse(localStorage.getItem(STORAGE + key));
    return valid(value) ? value : null;
  } catch { return null; }
}

function remember(key, n) {
  try { localStorage.setItem(STORAGE + key, String(Math.max(cached(key) ?? 0, n))); } catch { /* storage may be disabled */ }
}

async function request(path, options, signal) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), 2200);
  try {
    const response = await fetch(API + path, {
      cache: 'no-store', ...options, credentials: 'omit', referrerPolicy: 'no-referrer',
      signal: signal ? AbortSignal.any([signal, controller.signal]) : controller.signal,
    });
    return response.ok ? await response.json() : null;
  } catch { return null; }
  finally { clearTimeout(timer); }
}

export async function bump(key) {
  const response = liveGame() && await request('/count', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ key }),
  });
  if (response?.key === key && valid(response.n)) {
    remember(key, response.n);
    return response.n;
  }
  const last = cached(key);
  return last === null ? null : Math.min(Number.MAX_SAFE_INTEGER, last + 1);
}

export async function read(keys) {
  const unique = [...new Set(keys)];
  if (!unique.length) return {};
  const response = liveGame() && await request('/counts?' + new URLSearchParams({ keys: unique.join(',') }));
  return Object.fromEntries(unique.map(key => {
    const n = response?.[key];
    if (valid(n)) remember(key, n);
    return [key, valid(n) ? n : cached(key)];
  }));
}

/** Anonymous read. Preview purchases must never increment the ending counter. */
export async function community(signal) {
  const origin = globalThis.location?.origin;
  if (origin !== 'https://tc.cclabs.app' && origin !== 'http://127.0.0.1:8817') return null;
  const result = await request('/community', { method: 'GET', cache: 'default' }, signal);
  if (!result || typeof result !== 'object') return null;
  const { discord, ccp } = result;
  const dated = row => typeof row?.asOf === 'string' && Number.isFinite(Date.parse(row.asOf));
  return {
    discord: dated(discord) && valid(discord.online) && valid(discord.members) ? discord : null,
    ccp: dated(ccp) && valid(ccp.active) && ccp.windowSeconds === 180 ? ccp : null,
  };
}
