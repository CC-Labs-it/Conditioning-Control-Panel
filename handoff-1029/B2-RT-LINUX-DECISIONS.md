# B2 - Racing Thoughts 3D on Linux: decisions

Brainstorm B2 (brief `handoff-1029/B2-RT-LINUX-BRAINSTORM.md`). Build lane A5 consumes this file.
Status: DECIDED 2026-10-29 (owner picked in round 5). Owed: Mich's Linux numbers (kit `handoff-1029/rt-linux-spike/`),
which set the WebGL2 / WebGPU switch and can still stop the build (see "Gate before A5 merges").
Comparison page: https://claude.ai/artifact/U6a9EeFXSg2JXaaiDMCSPH

## Owner answers (survey)

### Round 1 (2026-10-29)

- Q: Which real Linux machine can we measure on?
  A: "Mich's box (Recommended)"
- Q: Which Linux setups must Racing Thoughts work on for the 7.2 preview?
  A: "No idea"
- Q: How should the Linux preview be packaged?
  A: "No idea"
- Q: Which GPU types should the Linux numbers cover?
  A: "No idea"

Reading: Mich runs the spike; the setup, packaging and GPU questions are delegated to our recommendation
(defaults recorded in "Decisions" below once the spike numbers are in).

### Round 2 (2026-10-29)

- Q: If the in-app game window is too slow on Linux, may Racing Thoughts open in the user's own browser?
  A: "Labelled fallback only (Recommended)"
- Q: How much extra download is acceptable for a bundled Chromium (CEF) on the Linux preview?
  A: "I got no idea"
- Q: What is the lowest acceptable look for Racing Thoughts on a Linux PC?
  A: "Full desktop look only"

Reading: the in-app window stays the default and panic must close it; the browser is a labelled escape hatch.
Full desktop look is the bar, so a phone-like Linux tier is OUT: if WebKitGTK cannot hold the full look,
the engine changes (bundled Chromium), not the look. Download size delegated to us.

### Round 3 (2026-10-29)

- Q: Should online races work in the 7.2 Linux preview, or solo first?
  A: "Online at launch (Recommended)"
- Q: Scrolller pictures on Linux if the Scrolller gate is not ready on that host?
  A: "My files only on Linux"

Reading: Linux joins the same rooms at launch (course hash must match across engines). On Linux the picture
choice is "My files" / Off until the Scrolller gate is verified on the Linux game host; Scrolller never runs ungated.

### Round 4 (2026-10-29)

- Q: What frame rate counts as holding the full look on a mid-range Linux PC?
  A: "60 fps, small dips OK (Recommended)"
- Q: If WebKitGTK cannot hold the full look, where should our own Chromium for the game live?
  A: "Download on first race (Recommended)"
- Q: A weak Linux machine cannot hold the full look under any engine. What does it get?
  A: "Race anyway, auto quality (Recommended)"

Reading: bar = steady 55-60 fps at the full look on Threshold and GOONWORKS, no mid-race shader freezes. The
full look is the default and the target; the existing auto quality ladder may still step down on weak GPUs
(no Linux-only phone tier, no gate on the door). A Chromium engine, if needed, is fetched on the first
Linux race, never bundled.

### Round 5, the pick (2026-10-29, after the comparison page)

- Q: Which engine should lane A5 build?
  A: "B: own Chromium (Recommended)"
- Q: If the race engine cannot be fetched or will not start on someone's Linux PC, what do they get?
  A: "Both, labelled (Recommended)"
- Q: Scrolller on Linux through our own Chromium's request gate, after the preview?
  A: "Yes, a 7.2.x follow-up (Recommended)"

## Spike (2026-10-29)

- No Linux machine here (no WSL on the owner PC, so not even WSLg numbers). Real Linux numbers come from
  Mich's box with the kit in `handoff-1029/rt-linux-spike/` (`bash run.sh`, ~25 min, no sudo, report + tar.gz
  back through the owner; README.md is the note for Mich).
- The kit's server + probe + report were proven end to end on the owner PC with Edge (Windows 11, RTX 5080):
  same pins (quality high, renderScale 1, bot, seed 7, 45 s after the countdown, vsync on). REFERENCE ONLY, not Linux:

  | Edge 156 | backend | garage s | fully warm s | fps mean | 1% low | worst 1 s | >50/>100 ms | hash |
  |---|---|---|---|---|---|---|---|---|
  | Threshold | WebGPU | 3.7 | 17.3 | 59 | 36 | 55 | 1/0 | match |
  | GOONWORKS | WebGPU | 4.2 | 12.5 | 60 | 57 | 59 | 0/0 | match |
  | Threshold | WebGL2 | 5.1 | 46.3 | 60 | 49 | 58 | 1/0 | match |
  | GOONWORKS | WebGL2 | 6.1 | 30.7 | 60 | 55 | 59 | 0/0 | match |

  CPU per frame 5.6-8.3 ms median, audio clock drift < 7 ms/min, output latency 40 ms. WebGL2 warms 2-3x slower
  than WebGPU (programs link one by one): the race may start before "fully warm", so mid-race stalls on WebGL2 are
  the number to watch in Mich's report. Firefox / Electron / Playwright WebKit rows were CUT: the owner was at the
  desk and the windows ran behind their browser (occluded windows throttle), so the runs stalled. The Linux-only
  pieces (`run.sh`, `wk-bench.py`, `electron-main.js`) are syntax-checked, not yet run on Linux: expect Mich's
  first run to shake out a bug or two.
- Facts found while building it:
  - The app's Linux web view is WebKitGTK through `Avalonia.Controls.WebView` 12.1 (webkit2gtk-4.1, GTK3
    NativeControlHost); the Flatpak runs it from `org.gnome.Platform//50`. No custom scheme on WebKitGTK, so pages
    come from the loopback `WebAssetServer`. WPE is opt-in only (`CCP_LINUX_WEBVIEW=wpe`).
  - WebKitGTK has no WebGPU by default (no GTK default-enable found); Glassroad runs WebGL2 there. Chromium on
    Linux has WebGPU only behind `--enable-unsafe-webgpu --enable-features=Vulkan`; the kit measures both.
  - The Scrolller block filter the app puts on every WebKitGTK view blocks scrolller.com outright; that is why
    Linux gets "My files" (blob urls, never fetched) until a gate exists on the Linux game host.
  - Electron 45.1.0 linux-x64 zip = 124 MB download (Windows unzipped 357 MB). CefGlue.Avalonia lists Linux x64
    but its preview builds lag Chromium and embedding a 60 fps game through it is unproven.
  - Course hash: every engine measured so far builds the same `onlineCourseHash` as Node for all five courses.

## Decisions (2026-10-29)

### 1. Renderer path

- **Racing Thoughts on Linux runs in our own Chromium: a pinned Electron build, fetched on the first Linux race,
  in its own window that CCP starts and owns.** Every other Linux web surface stays on WebKitGTK.
- Pin: one Electron version per CCP release (start at 45.1.0), URL + SHA256 per CPU type (linux-x64, linux-arm64)
  in a manifest in the app; never "latest" at runtime. Download 124 MB (x64) once, into
  `CorePaths.UserData/engines/electron-<ver>/` (Flatpak: the app's own data dir). Old versions are deleted after
  a new one verifies.
- Renderer: WebGL2 by default (`init.caps.forceRenderer = 'webgl2'`, host/bridge.js), until Mich's numbers show
  Electron WebGPU (`--enable-unsafe-webgpu --enable-features=Vulkan`) clean on Linux, then WebGPU with WebGL2 as
  the page's own fallback. One setting in the race host, no Linux-specific game code.
- Fallback, both labelled (owner, round 5): when the engine cannot be fetched or will not start,
  1. "Play in the app (slower)" = today's WebKitGTK path (`GameWindow.Glassroad.cs`), panic still closes it;
  2. "Open in browser" = `app.cclabs.app/race3d` with the line "Panic cannot close a browser tab."
- Panic (hard rule 6): the race process is registered in the panic registry (PanicSurfaces "games") and panic ends
  it by its exact process id (then its process group), the same funnel as every game window. Escape pauses (the
  shell's `before-input-event` hands Escape to CCP, which keeps the 2 s double-Escape panic rule). Leash, Lockdown
  and Strict Lock refusals are the host's, unchanged.

### 2. Linux tier settings

- No Linux tier. Full desktop look by default on every engine; the existing auto quality ladder and the
  LOOK/MEMORY split (`render/deviceClass.js`) apply as on Windows (owner: weak PCs race anyway on auto quality).
- Bar for "holds the full look" (owner): fps mean >= 57, worst 1 s >= 50, no frame over 100 ms while racing, on
  Threshold and GOONWORKS, quality high, render scale 100%. `rt-linux-spike/make-report.mjs` prints this verdict.
- Online at launch: the Linux engine must build the same `onlineCourseHash` as Node (the kit checks all five
  courses). Same rooms, same tier rules (online tier 2+).
- Pictures on Linux: "My files" / Off only (init caps say no Scrolller on Linux); Scrolller never runs ungated.

### 3. Packaging changes

- Nothing is bundled: tarball and Flatpak sizes do not change. Both need network (Flatpak already has
  `--share=network`) and `--device=dri` (already there).
- Chromium sandbox: Ubuntu 24.04+ blocks the unprivileged user namespaces it needs, and Flatpak forbids them.
  Start with the sandbox when the kernel allows it (probe once), else `--no-sandbox`; acceptable ONLY because the
  shell loads nothing but our loopback game server (navigation, new windows and permission requests refused, no
  Scrolller). Flatpak later may switch to zypak. Record the choice in the shell's log line.
- Licences: keep Electron's `LICENSE` and `LICENSES.chromium.html` inside the engine folder; the About / licence
  page names "Electron (MIT) / Chromium (BSD)". Electron's ffmpeg carries H.264/MP3 decoders: owner/legal call
  before shipping, else use the proprietary-codec-free ffmpeg Electron publishes (`ffmpeg-<ver>-linux-x64.zip`).
- Release checklist (A6, Linux): bump the Electron pin and its SHA256 in the manifest; check its download still
  resolves before the pre-release.
- Display: the shell runs under XWayland by default (same as the app's X11 socket in the Flatpak), so the X11
  panic key keeps working with the race window focused. Verify on a Wayland session in the desk run.

### 4. Lane A5 build steps (each PR <= 600 changed lines, each builds and lands alone, on `port/7.2` after A1/A2)

1. **Engine manifest + fetch** (Core + Avalonia): pinned version, URL, SHA256 per arch; download with progress
   and cancel, verify before unpack, zip-slip guard, chmod, delete old versions; tests for bad checksum, partial
   file cleanup, path confinement. ~400 lines.
2. **Race shell** (`Assets/web/glassroad-shell/main.js` + `preload.js`, plain JS, no npm deps): one window,
   loads only the `WebAssetServer` URL with its token, refuses navigation / `window.open` / permission requests
   (gamepad and fullscreen allowed), muted until the page's own audio unlocks; exposes the `chrome.webview` shape
   the page bridge already speaks (host/bridge.js:104; if `window.chrome` cannot be set from the preload, add an
   additive `window.ccpHost` seam to bridge.js on feat/glassroad-a first); talks to CCP over stdin/stdout JSON
   lines; Escape goes to CCP via `before-input-event`. Node tests for the nav/permission rules. ~350 lines.
3. **Linux race host** (Avalonia): starts the shell, routes its messages through the SAME Glassroad message rules
   as `GameWindow.Glassroad.cs` (extract the shared switch: init, ready, heartbeat, net relay on `/v2/race/` with
   `HostNetPathRule`, exit), watchdog, presence, panic registry entry (kill by pid + group), double-Escape panic,
   Gentle in the init frame. Tests mirror `GameWindowTests.Glassroad.cs`. ~500 lines.
4. **First race card + fallbacks**: "Racing Thoughts needs its race engine. 124 MB, once." Download / Cancel /
   "Play in the app (slower)" / "Open in browser" with "Panic cannot close a browser tab."; failure copy names the
   reason. Loc keys in all TEN language files (dry, short, no dashes, no exclamation marks). ~400 lines.
5. **Linux picture source**: init caps turn the Scrolller choice off on Linux, Options shows My files / Off;
   test that no Scrolller URL is requested on that host. ~150 lines.
6. **Packaging + docs**: sandbox probe, Flatpak/tarball notes, licence files, the A6 checklist line, the
   `rt-linux-spike` kit moved under `packaging/linux/rt-spike/` for re-runs. ~150 lines.

### 5. Gate before A5 merges anything

- Mich's report (`rt-linux-spike-<host>-<date>.tar.gz`) is back and Electron meets the bar on WebGL2 on his box.
  If Electron misses the bar there, stop and bring the numbers to the owner before PR 3 lands.
- Desk run of the built path on Linux: first-race download, race, panic with the race window focused (X11 and a
  Wayland session), Escape pause, double Escape, online seat, My files pictures.

### 6. Follow-ups (not in the preview)

- 7.2.x: Scrolller in the Linux race through the shell's session request hooks (honest UA, block list, daily
  share charged), verified against the `ScrolllerWebGate` rules before Scrolller turns on there (owner, round 5).
- WebGPU on Linux by default once measured clean on more than one GPU family.
- Steam Deck / arm64: not targeted for the preview (owner had no preference); the manifest already carries arm64.
