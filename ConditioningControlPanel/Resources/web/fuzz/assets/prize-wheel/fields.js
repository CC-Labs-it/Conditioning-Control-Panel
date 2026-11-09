// Each screen-sized frame is a vertical stack of six swatches. Narrow sectors
// have shorter swatches so the wheel can decode the whole loop as one image.
export const FIELD_FRAMES = 80;
export const FIELD_FPS = 18;
export const FIELD_COLUMNS = 16;
export const FIELD_WIDTH = 92;
export const FIELDS = [
  { id: 'small', source: 'velvet-aurora', height: 112, color: ['#613057', '#cf608e', '#ffc5a7', '#fff3d6'] },
  { id: 'large', source: 'caustic-drift', height: 42, color: ['#07565b', '#33998b', '#9ce7cf', '#fff0c9'] },
  { id: 'nothing', source: 'granular-current', height: 88, color: ['#161328', '#403353', '#806d8e', '#c4b0c6'] },
  { id: 'regular', source: 'phase-textile', height: 92, color: ['#403372', '#8353b1', '#daa2d7', '#fbd6c4'] },
  { id: 'jackpot', source: 'spectral-shear', height: 23, color: ['#85421a', '#d8963d', '#ffe49b', '#fff4c0'] },
  { id: 'double', source: 'neon-mycelium', height: 78, color: ['#164363', '#316ea8', '#68d4e6', '#ffc7f0'] },
];
let top = 0;
export const FIELD_BY_ID = new Map(FIELDS.map(field => {
  const cell = { ...field, top };
  top += cell.height;
  return [field.id, cell];
}));
export const FIELD_HEIGHT = top;
