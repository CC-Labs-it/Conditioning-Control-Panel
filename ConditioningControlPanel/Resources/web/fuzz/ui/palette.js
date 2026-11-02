/* ui/palette.js - the run's colours: the theme picked, mixed with a second one. STUB until the colour lane fills it in.
 * ctx.palette() returns { a, b, soft, ink, spiral: [c1, c2], themes: [...] }; event 'palette' fires when it changes. */

const NONE = Object.freeze({ a: '#8a867d', b: '#8a867d', soft: '#e4e1da', ink: '#000000', spiral: Object.freeze(['#ff4fa8', '#6a3cff']), themes: Object.freeze([]) });

export function paletteOf() { return NONE; }
