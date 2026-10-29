/** Safari sends several viewport sizes while rotating or moving its browser bars.
 * Keep drawing the current buffer until the burst settles, then resize once. */
export function createViewportResize({ renderer, size, ratio, delayMs = 0 }) {
  let timer = null, disposed = false, previous = null;
  function apply() {
    if (timer !== null) clearTimeout(timer);
    timer = null;
    if (disposed) return false;
    const { width, height } = size(), pixelRatio = ratio();
    if (!(width > 0 && height > 0 && pixelRatio > 0)) return false;
    if (previous && previous.width === width && previous.height === height && previous.pixelRatio === pixelRatio) return false;
    // setPixelRatio followed by setSize reallocates twice, even when nothing changed.
    renderer.setDrawingBufferSize(width, height, pixelRatio);
    previous = { width, height, pixelRatio };
    return true;
  }
  return {
    apply,
    request() {
      if (disposed) return;
      if (!delayMs) { apply(); return; }
      if (timer !== null) clearTimeout(timer);
      timer = setTimeout(apply, delayMs);
    },
    dispose() { disposed = true; if (timer !== null) clearTimeout(timer); timer = null; },
  };
}
