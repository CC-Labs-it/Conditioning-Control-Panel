import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRun } from '../sim/economy.js';
import { createLife, recordEnding } from '../sim/save.js';
import { snapshotRun } from '../sim/stickers.js';
import { cardModel } from '../ui/adcard/model.js';
import { ENDINGS } from '../data/content/endings.js';
import { BY_ID } from '../data/items.js';
import { isButtonFinaleReview } from '../ui/adcard/button-ending.js';

test('the held viewpoint is limited to the local seeded preview', () => {
  const query = '?buy=v_wall&buttonCheckpoint=finale';
  assert.equal(isButtonFinaleReview(new URL('http://127.0.0.1:8817/fuzz/' + query)), true);
  assert.equal(isButtonFinaleReview(new URL('https://tc.cclabs.app/fuzz/' + query)), false);
  assert.equal(isButtonFinaleReview(new URL('http://127.0.0.1:8817/fuzz/?buttonCheckpoint=finale')), false);
});

test('the Ads purchase, earned sticker, recap and reprinted share card use the same ending', () => {
  const run = createRun({ seed:132, mood:null });
  run.picks.f5 = 'trance';
  run.ended = { id:'o_out', at:run.clock };
  const life = createLife();
  recordEnding(life, run, run.ended.id);
  assert.equal(BY_ID.o_out.name, 'Ending: ' + ENDINGS.o_out.title);
  assert.equal(ENDINGS.o_out.title, 'Living Button');
  assert.equal(life.endings.o_out, 1);
  assert.ok(life.stickers.e_o_out);
  const original = cardModel({run,life});
  const reprinted = cardModel({run:snapshotRun(life.adcard),life});
  assert.match(original.head,/became the button/);
  assert.match(original.serial,/^Button #/);
  assert.equal(reprinted.serial, original.serial);
  assert.equal(reprinted.head, original.head);
  run.ended.id = 'outside';
  assert.match(cardModel({run,life}).serial,/^Ad #/);
});
