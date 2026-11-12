// Canvas text has no CSS cascade. Read the same palette and font as the active HUD.
export function bubbleInk(canvas) {
  const doc = canvas.ownerDocument;
  if (!doc.documentElement.hasAttribute('data-bubble-type')) return null;
  const style = doc.defaultView.getComputedStyle(canvas);
  return {
    font: style.getPropertyValue('--game-bubble-font').trim(),
    fill: style.getPropertyValue('--game-bubble-fill').trim(),
    alt: style.getPropertyValue('--game-bubble-alt').trim(),
    edge: style.getPropertyValue('--game-bubble-edge').trim(),
  };
}
