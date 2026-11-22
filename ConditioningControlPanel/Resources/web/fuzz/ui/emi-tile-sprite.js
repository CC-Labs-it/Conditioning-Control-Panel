// face-neutral.png is createFace(canvas).draw('0_0') exported after the bundled
// Noto Sans Mono font loaded. The glass bounds match the live desk sprite.
export function emiTileSprite(pose = 'body-idle', x = 0, y = 0, size = 64) {
  return `<svg x="${x}" y="${y}" width="${size}" height="${size}" viewBox="0 0 100 100" aria-hidden="true">
    <image href="assets/img/emi/${pose}.png" width="100" height="100" preserveAspectRatio="none"/>
    <image href="assets/img/emi/face-neutral.png" x="34.46" y="29.46" width="41.68" height="37.63" preserveAspectRatio="none" style="image-rendering:pixelated"/>
  </svg>`;
}
