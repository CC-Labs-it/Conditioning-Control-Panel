using System;

namespace ConditioningControlPanel.Services.Video.Browser
{
    /// <summary>
    /// Decides what audio-output device label rides a player-page <c>load</c> message (#938
    /// plumbing). The page matches the label against <c>enumerateDevices()</c> and applies
    /// <c>setSinkId</c>; every failure path page-side reports back and leaves audio on the
    /// Windows default, so a wrong or stale label can never silence a clip.
    ///
    /// This is deliberately dormant in the field today: <c>BrowserVideoGate</c> (WPF head) still
    /// routes device-pinned users to LibVLC (#938), so a real user with a chosen device never
    /// reaches the player page. The label still flows for the surfaces the gate does not govern
    /// once that demotion is retired — a separate change, made only after this path has proven
    /// itself. Until then the test-override environment variable is the way to exercise it
    /// ears-on without touching the gate.
    /// </summary>
    internal static class BrowserSinkLabel
    {
        /// <summary>Manual-verification override: set to a device's friendly name (or a prefix of
        /// it) and the player page routes there even with the picker on "System default" — which
        /// is the one configuration where the browser engine actually runs today (see the gate
        /// note above). Absent in production use.</summary>
        public const string TestOverrideVariable = "CCP_BROWSER_SINK_TEST_LABEL";

        /// <summary>Label from live settings + environment; null when no routing is wanted.</summary>
        public static string? Resolve()
        {
            string? env = null;
            try { env = Environment.GetEnvironmentVariable(TestOverrideVariable); } catch { }
            var s = CoreSettings.Current;
            return Resolve(s.AudioOutputDeviceId, s.AudioOutputDeviceName, env);
        }

        /// <summary>
        /// Pure core, one rule per line so the tests can pin them: a non-blank test override wins
        /// outright; an empty device id is the "System default" picker choice and asks for no
        /// routing; a chosen id with no persisted friendly name yields null too, because the page
        /// can only match by label. Trimmed, never empty-string (null is the "don't" signal).
        /// </summary>
        public static string? Resolve(string? deviceId, string? deviceName, string? testOverride)
        {
            if (!string.IsNullOrWhiteSpace(testOverride)) return testOverride.Trim();
            if (string.IsNullOrEmpty(deviceId)) return null;
            if (string.IsNullOrWhiteSpace(deviceName)) return null;
            return deviceName.Trim();
        }

        /// <summary>Only the audio-bearing surface gets a label: secondaries are permanently muted
        /// (one WASAPI session per clip), and handing them one would spend a mic-probe stream per
        /// monitor for audio nobody hears.</summary>
        public static string? ForWindow(bool primary, string? resolved) => primary ? resolved : null;

        /// <summary>WPF ChaosWebViewHost.cs:1257-1350 (#938): the document-start script a hosted local page
        /// gets. Media elements are routed on first play and every AudioContext at creation; the device id is
        /// resolved lazily, so a silent page never runs the microphone-label probe. Every failure leaves audio
        /// on the default output. The label rides as a JSON string literal, so quotes cannot break out.</summary>
        public static string RoutingScript(string label) =>
            RoutingTemplate.Replace("__CCP_SINK_LABEL__", System.Text.Json.JsonSerializer.Serialize(label));

        private const string RoutingTemplate = """
            (function () {
              'use strict';
              var WANTED = __CCP_SINK_LABEL__;
              if (typeof WANTED !== 'string' || !WANTED.trim()) return;
              WANTED = WANTED.trim();
              var md = navigator.mediaDevices;
              function report(ok, detail) {
                try {
                  window.chrome.webview.postMessage({ type: 'log', level: ok ? 'info' : 'warn',
                    msg: ok ? ('audio routed to output device "' + WANTED + '" via setSinkId (' + detail + ')')
                            : ('could not route audio to "' + WANTED + '" (' + detail + '), staying on the default output') });
                } catch (e) { }
              }
              if (!md || typeof md.enumerateDevices !== 'function') return;
              function norm(s) { return (s || '').trim().toLowerCase(); }
              function pick(devices) {
                var w = norm(WANTED);
                var outs = devices.filter(function (d) {
                  return d.kind === 'audiooutput' && d.deviceId && d.deviceId !== 'default' && d.deviceId !== 'communications';
                });
                var hit = outs.find(function (d) { return norm(d.label) === w; });
                if (hit) return hit;
                var open = w.indexOf('(');
                var close = w.lastIndexOf(')');
                var driver = open >= 0 && close > open ? w.substring(open + 1, close).trim() : w;
                hit = outs.find(function (d) {
                  var l = norm(d.label);
                  return !!l && (l.indexOf(driver) === 0 || driver.indexOf(l) === 0 || l.indexOf(driver) >= 0 || w.indexOf(l) >= 0);
                });
                return hit || null;
              }
              var idPromise = null;
              function resolveId() {
                if (idPromise) return idPromise;
                idPromise = md.enumerateDevices()
                  .then(function (ds) {
                    if (ds.some(function (d) { return d.kind === 'audiooutput' && d.label; })) return ds;
                    return md.getUserMedia({ audio: true }).then(function (stream) {
                      stream.getTracks().forEach(function (t) { try { t.stop(); } catch (e) { } });
                      return md.enumerateDevices();
                    });
                  })
                  .then(function (ds) {
                    var hit = pick(ds);
                    if (!hit) { report(false, 'no audiooutput label matched'); return null; }
                    return hit.deviceId;
                  })
                  .catch(function (err) {
                    report(false, (err && (err.name || err.message)) || 'error');
                    return null;
                  });
                return idPromise;
              }
              var reported = false;
              function route(target, kind) {
                try {
                  if (!target || typeof target.setSinkId !== 'function') return;
                  resolveId().then(function (id) {
                    if (!id) return;
                    return Promise.resolve(target.setSinkId(id)).then(function () {
                      if (!reported) { reported = true; report(true, kind); }
                    });
                  }).catch(function (e) { });
                } catch (e) { }
              }
              try {
                var origPlay = HTMLMediaElement.prototype.play;
                HTMLMediaElement.prototype.play = function () {
                  try {
                    if (!this.__ccpSinkDone) { this.__ccpSinkDone = true; route(this, 'media element'); }
                  } catch (e) { }
                  return origPlay.apply(this, arguments);
                };
              } catch (e) { }
              try {
                if (window.AudioContext && AudioContext.prototype && typeof AudioContext.prototype.setSinkId === 'function') {
                  var OrigCtx = window.AudioContext;
                  var Patched = class extends OrigCtx {
                    constructor() { super(...arguments); route(this, 'AudioContext'); }
                  };
                  if (window.webkitAudioContext === OrigCtx) window.webkitAudioContext = Patched;
                  window.AudioContext = Patched;
                }
              } catch (e) { }
            })();
            """;
    }
}
