/* Lanes: eyes ears sweet strict slides scroll blind sell premium. See ENGINE.md. One file per lane in ./routes/. */

import eyes from './routes/eyes.js';
import ears from './routes/ears.js';
import sweet from './routes/sweet.js';
import strict from './routes/strict.js';
import slides from './routes/slides.js';
import scroll from './routes/scroll.js';
import blind from './routes/blind.js';

export default { ...eyes, ...ears, ...sweet, ...strict, ...slides, ...scroll, ...blind };
