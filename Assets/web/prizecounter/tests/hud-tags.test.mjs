// Prices on the 3D price tags: which tag shows what (scene/tags.js tagFace, pure).
import test from 'node:test';
import assert from 'node:assert/strict';
import { tagFace } from '../scene/tags.js';

test('tag faces: price in ink, red when short, inked for locked Prime, none for owned or soon', () => {
  const row = { id: 'x', priceSp: 730, sale: 'on', owned: null, needs: null };
  assert.deepEqual(tagFace(row, 1850, 1), { text: '730', ink: 'ink' });
  assert.deepEqual(tagFace(row, 50, 1), { text: '730', ink: 'short' });
  assert.deepEqual(tagFace({ ...row, needs: 'prime' }, 50, 1), { text: '730', ink: 'prime' });
  assert.deepEqual(tagFace({ ...row, needs: 'prime' }, 1850, 2), { text: '730', ink: 'ink' });
  assert.equal(tagFace({ ...row, owned: { at: 'x', paidSp: 730 } }, 1850, 1), null);
  assert.equal(tagFace({ ...row, sale: 'soon' }, 1850, 1), null);
});
