# Local r186 fixes

Keep the MIT licence and the pinned r186 distribution. Reapply and verify these patches before replacing the vendor files.

- `build/three.core.js`, `yieldToMain`: share an 8 ms work budget across small asynchronous compilation steps. Use the native scheduler, or Firefox's animation-frame fallback, when that budget expires. Start the next budget after the yield resumes. This preserves main-thread turns without waiting a frame for every tiny shader-builder stage.
- `build/three.webgpu.min.js`, `Renderer.compileAsync`: optional fifth argument selects the intended render context depth; the default is the next direct render depth. `render/post.js` supplies the nested scene-pass depth and restores render target/MRT after preparation. The original depth-zero preparation used a different node-builder cache from actual drawing, causing duplicate construction. Recursion counters and distinct nested render contexts remain intact.
- Preparation overlaps at most eight pipeline jobs whose graphs have no before/after hooks, on WebGPU or WebGL2 with `KHR_parallel_shader_compile`. Node construction and binding updates remain serial. Drain the queue before hook-bearing graphs and at completion; preserve completion order and drain started work on failure. WebGL2 without the extension remains serial. Close each WebGPU validation scope immediately after starting its pipeline, then await that pipeline and its own validation result together.

Behavioral regressions live in `tests/compile-scheduling.test.mjs`, `tests/compile-depth.test.mjs` and `tests/compile-pipeline-queue.test.mjs`. Native loading and first-race checks must cover WebGPU and WebGL2, including Firefox's frame fallback, before shipping a vendor update.
