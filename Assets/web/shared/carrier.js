// shared/carrier.js - one host carrier for every desktop page: WPF's WebView2 chrome.webview unchanged, or the
// Avalonia head's WebKitGTK string carrier (invokeCSharpAction out, __ccpRnPush in) in the same shape; else null.

/** True inside a desktop host: WPF's WebView2 or the Avalonia head's string carrier. */
export function desktopHosted(g = globalThis) {
  return !!(g?.chrome?.webview || typeof g?.invokeCSharpAction === 'function');
}

/** A chrome.webview-shaped carrier for whichever host this page is in, or null. */
export function carrier(g = globalThis) {
  const webview = g?.chrome?.webview;
  if (webview) return webview;
  if (!g || typeof g.invokeCSharpAction !== 'function') return null;
  const listeners = [];
  try {
    g.__ccpRnPush = (json) => {
      let data;
      try { data = typeof json === 'string' ? JSON.parse(json) : json; } catch { return; }
      for (const fn of listeners.slice()) { try { fn({ data }); } catch { /* keep listening */ } }
    };
  } catch { /* frozen window: never throw at import */ }
  return {
    postMessage: (msg) => g.invokeCSharpAction(JSON.stringify(msg)),
    addEventListener: (type, fn) => { if (type === 'message' && typeof fn === 'function') listeners.push(fn); },
  };
}
