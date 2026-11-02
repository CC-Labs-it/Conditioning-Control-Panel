// Defects the review lane found: each test holds one fix.
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cardUp } from '../ui/unlocks/extras/kit.js';

test('a closed Guide or Control Panel sheet is not a card up', () => {
  const was = globalThis.document;
  const host = { children: [{ hidden: true }] };
  globalThis.document = { getElementById: () => host };
  try {
    assert.equal(cardUp(), false, 'only a hidden sheet');
    host.children = [{ hidden: true }, { hidden: false }];
    assert.equal(cardUp(), true, 'a real card over it');
    host.children = [];
    assert.equal(cardUp(), false, 'an empty host');
  } finally { globalThis.document = was; }
});
