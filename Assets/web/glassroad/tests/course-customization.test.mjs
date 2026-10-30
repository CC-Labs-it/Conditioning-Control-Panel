import { test } from 'node:test';
import assert from 'node:assert/strict';
import { CUSTOMIZATION_KEY, loadCustomization, saveCustomization, selectedSongCharts } from '../host/courseCustomization.js';

test('course choices survive reload independently without storing files or arbitrary URLs', () => {
  const data = new Map(), store = { getItem: k => data.get(k), setItem: (k,v) => data.set(k,v) };
  saveCustomization({ threshold: {source:'files',songId:'boudoir'}, keep: {source:'off',songId:'default'} }, store);
  assert.deepEqual(loadCustomization(store), {threshold:{source:'files',songId:'boudoir'},keep:{source:'off',songId:'default'}});
  data.set(CUSTOMIZATION_KEY, JSON.stringify({ threshold: {source:'bad',songId:'https://other/song'}, unknown:{source:'files'} }));
  assert.deepEqual(loadCustomization(store), {threshold:{}});
  assert.deepEqual(loadCustomization({getItem(){throw Error('blocked');}}), {});
});

test('a selected recording must come from the validated catalog, with safe default fallback', () => {
  const rows = [{id:'keep',url:'https://room/song/keep/chart.json'}], defaults = ['/own/chart.json'];
  assert.deepEqual(selectedSongCharts('keep', rows, defaults), [rows[0].url]);
  for (const id of ['default','missing','https://other/song',null]) assert.equal(selectedSongCharts(id, rows, defaults), defaults);
});
