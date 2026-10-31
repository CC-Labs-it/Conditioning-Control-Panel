# Racing Thoughts 3D on Linux - bench report

Bar: fps mean >= 57, worst 1 s >= 50, no frame over 100 ms, course hash = Node. Per-dir detail: each dir's report.md.

| GPU | run | engine | backend | window | fps mean | worst 1 s | worst frame ms | >100 ms | hash | verdict |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | chrome-goonworks | Chrome/155.0.0.0 Safari/537.36 | webgpu | 1920x1080 dpr 1 | 293 | 39 | 172.9 | 1 | yes | BELOW BAR: worst 1 s 39, 1 frames > 100 ms |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | chrome-threshold | Chrome/155.0.0.0 Safari/537.36 | webgpu | 1920x1080 dpr 1 | 266 | 176 | 35.1 | 0 | yes | MEETS BAR |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | electron-gl-goonworks | Electron 45.2.1 ozone=auto | webgl2 | 1920x994 dpr 1 | 342 | 270 | 17.2 | 0 | yes | MEETS BAR |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | electron-gl-threshold | Electron 45.2.1 ozone=auto | webgl2 | 1920x994 dpr 1 | 310 | 232 | 20.1 | 0 | yes | MEETS BAR |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | electron-gpu-goonworks | Electron 45.2.1 ozone=auto | webgpu | 1920x994 dpr 1 | 302 | 202 | 18 | 0 | yes | MEETS BAR |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | electron-gpu-threshold | Electron 45.2.1 ozone=auto | webgpu | 1920x994 dpr 1 | 256 | 175 | 31 | 0 | yes | MEETS BAR |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | electron-x11-gl-goonworks | Electron 45.2.1 ozone=x11 | - | 1108x1080 dpr 1 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | electron-x11-gl-threshold | Electron 45.2.1 ozone=x11 | - | 1108x1052 dpr 1 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | electron-x11-gpu-goonworks | Electron 45.2.1 ozone=x11 | webgpu | 1108x1052 dpr 1 | 318 | 217 | 21.4 | 0 | yes | MEETS BAR |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | electron-x11-gpu-threshold | Electron 45.2.1 ozone=x11 | webgl2 | 1108x1052 dpr 1 | 166 | 106 | 68.2 | 0 | yes | MEETS BAR |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | firefox-goonworks | Gecko/20100101 Firefox/158.0 | webgl2 | 1920x964 dpr 1 | 250 | 174 | 33 | 0 | yes | MEETS BAR |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | firefox-threshold | Gecko/20100101 Firefox/158.0 | webgl2 | 1920x964 dpr 1 | 226 | 171 | 46 | 0 | yes | MEETS BAR |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | flatpak-goonworks | - | - | - | - | - | - | - | - | FAIL (no result (engine died or timed out)) |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | flatpak-threshold | - | - | - | - | - | - | - | - | FAIL (no result (engine died or timed out)) |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | flatpak-x11-goonworks | WebKitGTK 2.54.1 flatpak GDK=x11 | - | 1920x1022 dpr 1.3 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | flatpak-x11-nodmabuf-goonworks | WebKitGTK 2.54.1 flatpak GDK=x11 no-dmabuf | webgl2 | 1920x1022 dpr 1.3 | 22 | 11 | 98 | 0 | yes | BELOW BAR: mean 22, worst 1 s 11 |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | flatpak-x11-nodmabuf-threshold | WebKitGTK 2.54.1 flatpak GDK=x11 no-dmabuf | webgl2 | 1920x1022 dpr 1.3 | 22 | 11 | 99 | 0 | yes | BELOW BAR: mean 22, worst 1 s 11 |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | flatpak-x11-threshold | WebKitGTK 2.54.1 flatpak GDK=x11 | - | 1920x1022 dpr 1.3 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-goonworks | - | - | - | - | - | - | - | - | FAIL (no result (engine died or timed out)) |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-threshold-auto | - | - | - | - | - | - | - | - | FAIL (no result (engine died or timed out)) |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-threshold | - | - | - | - | - | - | - | - | FAIL (no result (engine died or timed out)) |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-x11-goonworks | WebKitGTK 2.54.1 GDK=x11 | - | 1920x1022 dpr 1.3 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-x11-nodmabuf-goonworks | WebKitGTK 2.54.1 GDK=x11 no-dmabuf | webgl2 | 1920x1022 dpr 1.3 | 24 | 11 | 94 | 0 | yes | BELOW BAR: mean 24, worst 1 s 11 |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-x11-nodmabuf-threshold-auto | WebKitGTK 2.54.1 GDK=x11 no-dmabuf | webgl2 | 1920x1022 dpr 1.3 | 21 | 7 | 188 | 37 | yes | BELOW BAR: mean 21, worst 1 s 7, 37 frames > 100 ms |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-x11-nodmabuf-threshold | WebKitGTK 2.54.1 GDK=x11 no-dmabuf | webgl2 | 1920x1022 dpr 1.3 | 21 | 11 | 119 | 3 | yes | BELOW BAR: mean 21, worst 1 s 11, 3 frames > 100 ms |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-x11-threshold-auto | WebKitGTK 2.54.1 GDK=x11 | - | 1920x1022 dpr 1.3 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-x11-threshold | WebKitGTK 2.54.1 GDK=x11 | - | 1920x1022 dpr 1.3 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-threshold-auto | - | - | - | - | - | - | - | - | FAIL (no result (engine died or timed out)) |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-x11-nodmabuf-threshold-auto | WebKitGTK 2.54.1 GDK=x11 no-dmabuf | webgl2 | 1920x1022 dpr 1.3 | 22 | 10 | 112 | 8 | yes | BELOW BAR: mean 22, worst 1 s 10, 8 frames > 100 ms, tier mid |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-x11-threshold-auto | WebKitGTK 2.54.1 GDK=x11 | - | 1920x1022 dpr 1.3 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | chrome-goonworks | Chrome/155.0.0.0 Safari/537.36 | - | 1920x1080 dpr 1 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | chrome-threshold | Chrome/155.0.0.0 Safari/537.36 | - | 1920x1080 dpr 1 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | electron-gl-goonworks | Electron 45.2.1 ozone=auto | - | 1108x1052 dpr 1 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | electron-gl-threshold | Electron 45.2.1 ozone=auto | - | 1920x994 dpr 1 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | electron-gpu-goonworks | Electron 45.2.1 ozone=auto | - | 1108x1052 dpr 1 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | electron-gpu-threshold | Electron 45.2.1 ozone=auto | - | 1108x1052 dpr 1 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | electron-x11-gl-goonworks | Electron 45.2.1 ozone=x11 | - | 1108x1052 dpr 1 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | electron-x11-gl-threshold | Electron 45.2.1 ozone=x11 | - | 1108x1052 dpr 1 | - | - | - | - | yes | FAIL (never reached the garage (failed)) |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | electron-x11-gpu-goonworks | Electron 45.2.1 ozone=x11 | webgpu | 1108x1052 dpr 1 | 41 | 21 | 92.5 | 0 | yes | BELOW BAR: mean 41, worst 1 s 21 |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | electron-x11-gpu-threshold | Electron 45.2.1 ozone=x11 | webgpu | 1108x1052 dpr 1 | 27 | 11 | 586.9 | 3 | yes | BELOW BAR: mean 27, worst 1 s 11, 3 frames > 100 ms |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | firefox-goonworks | Gecko/20100101 Firefox/158.0 | webgl2 | 1920x964 dpr 1 | 26 | 13 | 191 | 31 | yes | BELOW BAR: mean 26, worst 1 s 13, 31 frames > 100 ms |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | firefox-threshold | Gecko/20100101 Firefox/158.0 | webgl2 | 1920x964 dpr 1 | 16 | 9 | 263 | 136 | yes | BELOW BAR: mean 16, worst 1 s 9, 136 frames > 100 ms |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | flatpak-goonworks | WebKitGTK 2.54.1 flatpak | webgl2 | 1920x1022 dpr 1 | 21 | 11 | 156 | 9 | yes | BELOW BAR: mean 21, worst 1 s 11, 9 frames > 100 ms |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | flatpak-threshold | WebKitGTK 2.54.1 flatpak | webgl2 | 1920x1022 dpr 1 | 15 | 10 | 134 | 70 | yes | BELOW BAR: mean 15, worst 1 s 10, 70 frames > 100 ms |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | flatpak-x11-goonworks | WebKitGTK 2.54.1 flatpak GDK=x11 | webgl2 | 1920x1022 dpr 1.3 | 17 | 8 | 197 | 66 | yes | BELOW BAR: mean 17, worst 1 s 8, 66 frames > 100 ms |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | flatpak-x11-threshold | WebKitGTK 2.54.1 flatpak GDK=x11 | webgl2 | 1920x1022 dpr 1.3 | 11 | 7 | 175 | 135 | yes | BELOW BAR: mean 11, worst 1 s 7, 135 frames > 100 ms |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | wk-goonworks | WebKitGTK 2.54.1 | webgl2 | 1920x1022 dpr 1 | 25 | 13 | 127 | 10 | yes | BELOW BAR: mean 25, worst 1 s 13, 10 frames > 100 ms |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | wk-threshold-auto | WebKitGTK 2.54.1 | webgl2 | 1920x1022 dpr 1 | 37 | 24 | 79 | 0 | yes | BELOW BAR: mean 37, worst 1 s 24, tier mid |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | wk-threshold | WebKitGTK 2.54.1 | webgl2 | 1920x1022 dpr 1 | 15 | 10 | 132 | 84 | yes | BELOW BAR: mean 15, worst 1 s 10, 84 frames > 100 ms |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | wk-x11-goonworks | WebKitGTK 2.54.1 GDK=x11 | webgl2 | 1920x1022 dpr 1.3 | 18 | 8 | 190 | 43 | yes | BELOW BAR: mean 18, worst 1 s 8, 43 frames > 100 ms |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | wk-x11-threshold-auto | WebKitGTK 2.54.1 GDK=x11 | webgl2 | 1920x1022 dpr 1.3 | 26 | 16 | 82 | 0 | yes | BELOW BAR: mean 26, worst 1 s 16, tier mid |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | wk-x11-threshold | WebKitGTK 2.54.1 GDK=x11 | webgl2 | 1920x1022 dpr 1.3 | 11 | 7 | 173 | 150 | yes | BELOW BAR: mean 11, worst 1 s 7, 150 frames > 100 ms |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-nvidia-high-rs0.5 | WebKitGTK 2.54.1 GDK=x11 no-dmabuf | webgl2 | 1920x1022 dpr 1.3 | 25 | 11 | 93 | 0 | yes | BELOW BAR: mean 25, worst 1 s 11 |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-nvidia-high-rs1-art0 | WebKitGTK 2.54.1 GDK=x11 no-dmabuf | webgl2 | 1920x1022 dpr 1.3 | 26 | 12 | 89 | 0 | yes | BELOW BAR: mean 26, worst 1 s 12 |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-nvidia-high-rs1-win960x540 | WebKitGTK 2.54.1 GDK=x11 no-dmabuf | webgl2 | 738x415 dpr 1.3 | 57 | 40 | 38 | 0 | yes | BELOW BAR: worst 1 s 40 |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-nvidia-high-rs1 | WebKitGTK 2.54.1 GDK=x11 no-dmabuf | webgl2 | 1920x1022 dpr 1.3 | 24 | 11 | 104 | 2 | yes | BELOW BAR: mean 24, worst 1 s 11, 2 frames > 100 ms |
| RTX 4080 (01:00.0, NVIDIA 615.78.08) | wk-nvidia-low-rs1 | WebKitGTK 2.54.1 GDK=x11 no-dmabuf | webgl2 | 1920x1022 dpr 1.3 | 24 | 12 | 92 | 0 | yes | BELOW BAR: mean 24, worst 1 s 12, tier mid |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | wk-amd-high-rs0.5 | WebKitGTK 2.54.1 | webgl2 | 1920x1022 dpr 1 | 29 | 22 | 85 | 0 | yes | BELOW BAR: mean 29, worst 1 s 22 |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | wk-amd-high-rs1-art0 | WebKitGTK 2.54.1 | webgl2 | 1920x1022 dpr 1 | 14 | 9 | 131 | 60 | yes | BELOW BAR: mean 14, worst 1 s 9, 60 frames > 100 ms |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | wk-amd-high-rs1 | WebKitGTK 2.54.1 | webgl2 | 1920x1022 dpr 1 | 15 | 10 | 133 | 67 | yes | BELOW BAR: mean 15, worst 1 s 10, 67 frames > 100 ms |
| Radeon 610M iGPU (0c:00.0, radeonsi Mesa 26.2.5) | wk-amd-low-rs1 | WebKitGTK 2.54.1 | webgl2 | 1920x1022 dpr 1 | 17 | 11 | 131 | 55 | yes | BELOW BAR: mean 17, worst 1 s 11, 55 frames > 100 ms, tier mid |

## Notes (Mich's box, 2026-10-31, run by the port's worker)

Box: CachyOS, kernel 7.3, KDE Plasma Wayland. Ryzen 7 9800X3D, 60 GB. GPUs: RTX 4080 (01:00.0, NVIDIA 615.78.08,
drives both monitors) and Radeon 610M iGPU (0c:00.0, Mesa 26.2.5 radeonsi/RADV, no monitor attached).
Monitors: DP-3 1920x1080 @ 360 Hz, scale 1.0, primary (right). DP-1 2560x1440 @ 144 Hz, portrait, scale 1.3 (left).
The left monitor runs at 144 Hz, not 60. Game tree: feat/glassroad-a 5e42e0c7d1, pinned for both GPUs.
Electron: the kit fetched "latest" = 45.2.1 / Chrome 156. The B2 pin is 45.1.0, which was not measured.

### Which monitor
Every spike window went onto DP-3 (360 Hz) at the top left, sized to its work area (1920x1050 frame). A KWin script
did this outside the kit (`kwin-place.js`). Its log is `*.placement.log` in the evidence. Without it, KWin opened the
windows on DP-1 and clamped them to 1108 px wide.
Exceptions:
- KWin scripting never saw the Electron `ozone=x11` windows. They report 1108 px wide, which matches DP-1's logical
  width, so they were probably on DP-1 (144 Hz).
- The 960x540 experiment row was not placed.
Chromium is missing from the placement log. Its 1920x1080 viewport only fits on DP-3.
vsync is on, so fps is capped at 360 on DP-3. The bar (57 fps) is far below both refresh rates.

### Which GPU rendered (proof)
- RTX 4080: the Electron, Chrome and Firefox WebGL2 strings name the 4080 ("ANGLE (NVIDIA Corporation, NVIDIA
  GeForce RTX 4080...)"; Firefox shows "NVIDIA GeForce GTX 980, or similar"). WebKitGTK masks
  WEBGL_debug_renderer_info as "Apple GPU". For WebKit, the proof comes from the sidecar (`gpu-watch.py`, `gpu-proof.md`):
  the engine pids appear in `nvidia-smi pmon` at 31-46 % SM. Their amdgpu fdinfo gfx time is 0.
- Radeon 610M: selected with `DRI_PRIME=pci-0000_0c_00_0 __EGL_VENDOR_LIBRARY_FILENAMES=/usr/share/glvnd/egl_vendor.d/50_mesa.json
  __GLX_VENDOR_LIBRARY_NAME=mesa VK_ICD_FILENAMES=/usr/share/vulkan/icd.d/radeon_icd.json`. Inside the Flatpak the
  same variables point at `/usr/lib/x86_64-linux-gnu/GL/default/...`. Without DRI_PRIME, Mesa EGL picks llvmpipe
  (software rendering) on this box. Proof:
  - the Electron WebGL2 string is "ANGLE (AMD, AMD Ryzen 7 9800X3D (radeonsi raphael_mendocino ACO))";
  - for every WebKitGTK, Flatpak and Firefox run, the engine process held renderD129 (0c:00.0) and used 56-68 s of
    amdgpu gfx time per run (/proc fdinfo), with mean AMD busy at 65-93 %.
  - Caveat: the monitors are wired to the NVIDIA card, so every iGPU frame is PRIME-copied to it. These iGPU numbers
    include that copy. A machine whose only GPU is a 610M would composite on the iGPU itself.
  - Chromium-family on Wayland fails in this offload setup (cross-device dmabuf import, `eglCreateImage` 0x3009). That
    is a property of this setup, not proof that a 610M-only machine fails.

### Renderers against the bar (mean >= 57, worst 1 s >= 50, no frame > 100 ms), Threshold / GOONWORKS
RTX 4080 (DP-3 360 Hz):
- Electron 45.2.1, native Wayland:
  - WebGL2: 310/232/20 and 342/270/17 ms. PASS both.
  - WebGPU: 256/175/31 and 302/202/18. PASS both.
- Electron 45.2.1, `--ozone-platform=x11` (XWayland, which the B2 shell plans to use):
  - WebGL2: FAIL. The GPU process segfaults (SIGSEGV, exit 139) three times, then the page gets no WebGL2.
  - With the WebGPU/Vulkan switches the GPU process survives: 166/106/68 (fell back to WebGL2) and 318/217/21
    (WebGPU). PASS both.
  - System Chromium 155 with `--ozone-platform=x11` works on the same box. So this is specific to Electron 45.2.1 here.
- WebKitGTK 2.54.1 (system and Flatpak GNOME 50):
  - Native Wayland: the window dies at once (Gdk "Error 71 (Protocol error)").
  - GDK_BACKEND=x11, which is the app's own path (CCP.Avalonia pins X11): "Failed to create GBM buffer", and the game
    stalls at "Setting the course". FAIL.
  - x11 + WEBKIT_DISABLE_DMABUF_RENDERER=1 races: 21/11/119 and 24/11/94 (system), 22/11/99 and 22/11/98 (Flatpak).
    FAIL.
  - Quality Auto (mid, 0.7): 22/10/112. FAIL.
- Browser fallback:
  - Chrome 155 (WebGPU): 266/176/35 PASS; 293/39/173 FAIL (one 173 ms stall).
  - Firefox 158 (WebGL2): 226/171/46 and 250/174/33. PASS both.
Radeon 610M iGPU:
- WebKitGTK:
  - Native Wayland: 15/10/132 and 25/13/127.
  - x11: 11/7/173 and 18/8/190.
  - Flatpak: 15/10/134 and 21/11/156.
  - Auto (mid, 0.6): 37/24/79.
  - FAIL on every row.
- Electron:
  - WebGL2 fails on Wayland and on x11 (no context).
  - x11 + WebGPU switches: 27/11/587 and 41/21/93. FAIL.
- Chrome: FAIL (no context).
- Firefox: 16/9/263 and 26/13/191. FAIL.
Course hash matches Node in every run that loaded. The Scrolller block filter compiles and blocks scrolller.com; the
control site is reached.
WebKitGTK reports audio drift of -60000 ms/min. That means its AudioContext clock never advanced in the muted view,
so it is not a real drift figure.

### WebGPU in WebKitGTK (third row): not possible
WebKitGTK 2.54.1 (Arch and Flatpak GNOME 50) lists a "WebGPU" feature with status "unstable". It can be turned on with
`webkit_settings_set_feature_enabled`, and `get_feature_enabled` then returns true. `navigator.gpu` still stays
undefined, in a secure context, under X11 and Wayland, and also with the GPU-process features on.
`WEBKIT_ENABLE_WEBGPU` does not exist: the library holds no such string.

### Experiment: does the lag come from assets, or from pixels?
WebKitGTK only, Threshold course, one run per row, nothing else running. The kit's own server, wk-bench.py and spike.js
were used; only the options record and page params changed (`exp.sh`). The game was not changed.
- Not possible: renderScale 0.25. `hud/options.js` only accepts auto/1/0.75/0.5, and any other value falls back to Auto.
- Used as "hide scenery": `?art=0`. It races the greybox with no art library, which halves the draw calls.
- Added one row using the kit's own `--size` knob: a 960x540 window.

| GPU / WebKit mode | row | fps | worst 1 s | cpu median ms | draw calls | triangles | frames > 50 ms |
| --- | --- | --- | --- | --- | --- | --- | --- |
| RTX 4080, x11 no-dmabuf | high, scale 1 | 24 | 11 | 9 | 246 | 1.73 M | 131 |
| RTX 4080, x11 no-dmabuf | high, scale 0.5 | 25 | 11 | 8 | 246 | 1.73 M | 111 |
| RTX 4080, x11 no-dmabuf | low (game chose tier mid), scale 1 | 24 | 12 | 9 | 205 | 1.45 M | 129 |
| RTX 4080, x11 no-dmabuf | high, scale 1, art=0 | 26 | 12 | 6 | 124 | 1.22 M | 107 |
| RTX 4080, x11 no-dmabuf | high, scale 1, window 960x540 | 57 | 40 | 6 | 254 | 1.74 M | 0 |
| Radeon 610M, native (DMABuf) | high, scale 1 | 15 | 10 | 60 | 245 | 1.72 M | 562 |
| Radeon 610M, native (DMABuf) | high, scale 0.5 | 29 | 22 | 9 | 254 | 1.74 M | 313 |
| Radeon 610M, native (DMABuf) | low (tier mid), scale 1 | 17 | 11 | 55 | 204 | 1.46 M | 553 |
| Radeon 610M, native (DMABuf) | high, scale 1, art=0 | 14 | 9 | 68 | 129 | 1.23 M | 576 |

Answer: fps follows pixel count, not triangles or draw calls.
- Hiding the art cut the draw calls in half and the triangles by 30 %. fps did not move on either GPU (24 -> 26, 15 -> 14).
- Quality low moved fps by at most 2.
- On the 610M, a quarter of the canvas pixels (scale 0.5) doubled fps (15 -> 29), and the per-frame JS time fell from
  60 to 9 ms. That 60 ms is the main thread blocking on the GPU, not script work.
- On the 4080, canvas scale did nothing (24 -> 25). A quarter of the window area, with the same 1.74 M triangles and
  254 draws, took it from 24 to 57 fps with no frame over 50 ms. On NVIDIA, WebKitGTK can only run without its DMABuf
  renderer. Its cost is therefore the per-frame compositing and readback of the whole window-sized surface, and that
  cost does not shrink when the game renders fewer pixels.
- So the cost is WebKitGTK compositing and readback, plus pure GPU fill on the iGPU. Lazy or visibility-based asset
  loading would not help.
- Electron and Chrome render the same scene at 250-340 fps on the same 4080.

Asset loading shows up only in the boot milestones (seconds from boot). Threshold:
- WebKitGTK (4080, x11 no-dmabuf), ready -> shaders -> water -> lights: 3.1 -> 9.9 -> 10.9 -> 14.5. So 6.8 s shaders,
  1.0 s water, 3.6 s lights; warm at 14.8 s.
- WebKitGTK (610M native): 2.9 -> 11.9 -> 14.7 -> 18.1. So 9.0 / 2.8 / 3.4 s; warm at 18.3 s.
- Chrome 155 (4080): 1.6 -> 4.2 -> 4.8 -> 6.4. So 2.6 / 0.6 / 1.6 s; warm at 6.7 s.
- Electron (4080): 1.6 -> 4.0 -> 4.7 -> 6.0; warm at 6.3 s.
- GOONWORKS: WebKitGTK (4080) warm at 19.6 s (shaders 9.0 s) against Chrome at 6.8 s (shaders 2.5 s).
- art=0 cuts WebKit's warm-up on the 4080 from 12.7 to 9.8 s, but changes nothing while racing.
Conclusion: loading costs seconds before the race, but it is not the lag during it.

### Kit fixes (in handoff-1029/rt-linux-spike; no change to what is measured)
1. run.sh: WebKitGTK ran with `python3` from PATH. On a mise/pyenv/conda box that python has no GObject, so every
   WebKitGTK row was skipped as "no typelib". run.sh now uses the first of `python3` or `/usr/bin/python3` that
   imports WebKit2 4.1/4.0, and records it in system.txt.
2. run.sh: the Flatpak run lacked `--share=ipc`, which the app manifest has (packaging/flatpak). Without it, an X11
   window dies with MIT-SHM BadAccess.
3. run.sh: `flatpak run` does not forward the caller's GDK_BACKEND or WEBKIT_* variables, so they are now passed with
   `--env`. New knob `FLATPAK_ENV="K=V ..."`, used here for GPU selection with the sandbox's own GL paths.
4. run.sh + spike.js: the `-auto` rows were not on Auto.
   - Cause 1: run.sh added the pinned options record (quality high) to every run.
   - Cause 2: engines keep localStorage between runs, so an earlier record pinned quality again.
   - Fix: `-auto` runs get no spikeOpts, and spike.js removes the record when there is none.
   - Proof: tier `high` before the fix, `mid`/scale 0.6-0.7 after.
   - The 4080 main run's three `-auto` rows were measured before the fix and are pinned (tier high). The valid 4080
     Auto rows are in the separate `-nvidia-auto` set.
5. run.sh: opt-in rows, no existing row changed.
   - `WK_GDK="native x11 x11nodmabuf"` (default `native x11`): the app runs WebKitGTK through XWayland, and on NVIDIA
     only x11 + no-DMABuf races.
   - `ELECTRON_OZONE="auto x11"` (default both): the B2 shell plans XWayland. electron-main.js gets `SPIKE_OZONE` and
     records it in the info file.
6. make-report.mjs also writes report.html through the new report-html.mjs:
   - inline CSS for light and dark;
   - the table, plus an inline SVG chart of mean, worst 1 s and worst frame per run per GPU;
   - the raw numbers in `<script type="application/json" id="rt-bench">`.
   `node report-html.mjs --out <base> [--notes f] <dir>...` combines several result dirs into one md + html. A
   `<dir>/gpu.txt` file overrides the GPU label.

Noted, not changed:
- run.sh fetches Electron "latest" (45.2.1), not the B2 pin (45.1.0).
- The `curl: (23)` line in run.log is harmless: `grep -m1` closes the pipe.
- On Wayland, `x:0,y:0` and `--window-position` place nothing, so the windows land wherever KWin decides.
- WebKitGTK masks WEBGL_debug_renderer_info.
- wk-bench.py and Electron keep profiles in ~/.local/share/wk-bench.py and ~/.config/Electron. So shader caches are
  warm after the first run, and localStorage carries over between runs. Both folders were deleted after the bench.

### Answers to the kit's "By hand" questions
1. Desktop. NVIDIA 615.78.08 proprietary plus Mesa 26.2.5 for the iGPU. Refresh 360 Hz on DP-3 (the runs) and 144 Hz on DP-1.
2. Wayland (KDE Plasma). WebKitGTK and Electron were also run through XWayland, as described above.
3. The runs were unattended. Failures are recorded in the tables; no one watched the windows.
4. Not observed (unattended).
5. No gamepad attached.
6. Not run: the port build was not part of this task.
7. Nothing noted. The desktop stayed responsive.

### WebKitGTK: which GPU each row used (its renderer string is masked as "Apple GPU")
Source: `gpu-watch.py`, sampling every 5 s. The columns are:
- the device nodes the WebKitWebProcess held open;
- its maximum `nvidia-smi pmon` SM %;
- the amdgpu gfx time it used, from /proc fdinfo.

The 4080 WebKit processes held only NVIDIA nodes (/dev/nvidia0 plus renderD128 = 01:00.0).
The 610M WebKit processes held only renderD129 (0c:00.0) and no /dev/nvidia* node.

Not sampled:
- The native-Wayland 4080 rows (wk-threshold, wk-goonworks, wk-threshold-auto, flatpak-threshold,
  flatpak-goonworks). They died with Gdk Error 71 before the first 5 s sample, so no process was ever sampled.
- The experiment rows (`exp.sh`). They ran with the same environment as the rows proven below, but were not
  sampled themselves.

| pass | run | nodes held by WebKitWebProcess | NVIDIA SM % (pmon) | amdgpu gfx ms | GPU used |
| --- | --- | --- | --- | --- | --- |
| RTX 4080 | wk-x11-threshold | /dev/nvidia-modeset, /dev/nvidia0, /dev/nvidiactl, 01:00.0 render | - | 0 | RTX 4080 (opened, no GPU time sampled: run stalled/died) |
| RTX 4080 | wk-x11-goonworks | /dev/nvidia-modeset, /dev/nvidia0, /dev/nvidiactl, 01:00.0 render | - | 0 | RTX 4080 (opened, no GPU time sampled: run stalled/died) |
| RTX 4080 | wk-x11-threshold-auto | /dev/nvidia-modeset, /dev/nvidia0, /dev/nvidiactl, 01:00.0 render | - | 0 | RTX 4080 (opened, no GPU time sampled: run stalled/died) |
| RTX 4080 | wk-x11-nodmabuf-threshold | /dev/nvidia-modeset, /dev/nvidia0, /dev/nvidiactl, 01:00.0 render | 31 | 0 | RTX 4080 |
| RTX 4080 | wk-x11-nodmabuf-goonworks | /dev/nvidia-modeset, /dev/nvidia0, /dev/nvidiactl, 01:00.0 render | 39 | 0 | RTX 4080 |
| RTX 4080 | wk-x11-nodmabuf-threshold-auto | /dev/nvidia-modeset, /dev/nvidia0, /dev/nvidiactl, 01:00.0 render | 33 | 0 | RTX 4080 |
| RTX 4080 | flatpak-x11-threshold | /dev/nvidia-modeset, /dev/nvidia0, /dev/nvidiactl, 01:00.0 render | - | 0 | RTX 4080 (opened, no GPU time sampled: run stalled/died) |
| RTX 4080 | flatpak-x11-goonworks | /dev/nvidia-modeset, /dev/nvidia0, /dev/nvidiactl, 01:00.0 render | 7 | 0 | RTX 4080 |
| RTX 4080 | flatpak-x11-nodmabuf-threshold | /dev/nvidia-modeset, /dev/nvidia0, /dev/nvidiactl, 01:00.0 render | 46 | 0 | RTX 4080 |
| RTX 4080 | flatpak-x11-nodmabuf-goonworks | /dev/nvidia-modeset, /dev/nvidia0, /dev/nvidiactl, 01:00.0 render | 42 | 0 | RTX 4080 |
| RTX 4080 auto re-run | wk-x11-threshold-auto | /dev/nvidia-modeset, /dev/nvidia0, /dev/nvidiactl, 01:00.0 render | - | 0 | RTX 4080 (opened, no GPU time sampled: run stalled/died) |
| RTX 4080 auto re-run | wk-x11-nodmabuf-threshold-auto | /dev/nvidia-modeset, /dev/nvidia0, /dev/nvidiactl, 01:00.0 render | 35 | 0 | RTX 4080 |
| Radeon 610M | wk-threshold | 0c:00.0 render | - | 58506 | Radeon 610M |
| Radeon 610M | wk-goonworks | 0c:00.0 render | - | 59297 | Radeon 610M |
| Radeon 610M | wk-threshold-auto | 0c:00.0 render | - | 57677 | Radeon 610M |
| Radeon 610M | wk-x11-threshold | 0c:00.0 render | - | 64639 | Radeon 610M |
| Radeon 610M | wk-x11-goonworks | 0c:00.0 render | - | 59318 | Radeon 610M |
| Radeon 610M | wk-x11-threshold-auto | 0c:00.0 render | - | 53678 | Radeon 610M |
| Radeon 610M | flatpak-threshold | 0c:00.0 render | - | 59127 | Radeon 610M |
| Radeon 610M | flatpak-goonworks | 0c:00.0 render | - | 59034 | Radeon 610M |
| Radeon 610M | flatpak-x11-threshold | 0c:00.0 render | - | 66313 | Radeon 610M |
| Radeon 610M | flatpak-x11-goonworks | 0c:00.0 render | - | 60402 | Radeon 610M |

### WebKitGTK is capped at 60 Hz
On the 360 Hz DP-3 monitor, a trivial WebGL2 page in WebKitGTK 2.54.1 runs requestAnimationFrame at 60.7-60.8 Hz in
every mode tried:
- RTX 4080, XWayland, no-DMABuf;
- RTX 4080, XWayland, no-DMABuf, with `__GL_SYNC_TO_VBLANK=0 vblank_mode=0`;
- RTX 4080, XWayland, no-DMABuf, with `WEBKIT_FORCE_VBLANK_TIMER=1`;
- Radeon 610M, native Wayland, with and without `vblank_mode=0`;
- Radeon 610M, XWayland, with and without `vblank_mode=0`.

Chromium, Electron and Firefox reach 226-342 fps on the same monitor. So the 60 Hz ceiling comes from WebKitGTK's own
frame scheduling, not from GL vsync or from KWin throttling the window. The driver vsync variables do not lift it, so
no uncapped row can be made with an environment variable, and none was added.

What this means:
- WebKitGTK can never show more than about 60 fps, so the bar (mean 57) leaves it only 3 fps of headroom.
- The 960x540 experiment row (57 mean, worst second 40) is at the cap. That row shows the small window removes the
  bottleneck, not how much faster it would go.
- The early 57-58 fps smoke means on the 4080 came from a 1108x830 window (CSS px) and were also cap-bound.
- The full-size WebKitGTK rows (11-25 fps) are far below the cap. Their verdicts and the pixels-versus-assets answer
  do not depend on it.
