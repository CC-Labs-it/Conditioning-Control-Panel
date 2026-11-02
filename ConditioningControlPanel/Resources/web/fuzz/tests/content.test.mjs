/* The writing: house style, honest ads, complete endings, safe fakes.
 * Design rows are read when the handoff folder is around (desk), and the pinned
 * list below is the fallback everywhere else: handoff-1102 is not in the repo. */
import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import * as content from '../data/content/index.js';
import { CIRCE } from '../data/words.js';

const {
  JOKE_ADS, CCP_ADS, CCP_THEME_ADS, SPONSORED_THOUGHTS, NATIVE_LINES, EMI_AD_READS,
  THOUGHTS, thoughtsFor, QUESTIONS, TRICKS, ENDINGS, DROP_WORDS, THEMES, THEME_IDS,
  FAKE_ANTIVIRUS, FAKE_PAYWALL, FAKE_RENEWAL, NEW_TERMS, SUPPORT_MENU, FAKE_KEYHOLDER,
  FAKE_UPDATE, FAKE_CALL, FAKE_INBOX, FAKE_CHAT, FAKE_DESKTOP,
} = content;

const PINNED_ENDS = ['drop', 'o_out', 'p_life', 'tr_end', 'bi_end', 'dr_end', 'si_end', 'go_end', 'lo_end', 'ce_end', 'in_end', 'outside', 'onemore'];
const ROWS = 'C:/Projects/Conditioning-Control-Panel---CSharp-WPF/handoff-1102/fuzz-tree/rows.json';

/** Every string under a value, with a path for the failure message. */
function strings(value, path = '') {
  if (typeof value === 'string') return [{ path, text: value }];
  if (Array.isArray(value)) return value.flatMap((v, i) => strings(v, `${path}[${i}]`));
  if (value && typeof value === 'object') return Object.entries(value).flatMap(([k, v]) => strings(v, `${path}.${k}`));
  return [];
}
const data = Object.fromEntries(Object.entries(content).filter(([, v]) => typeof v !== 'function'));
const all = strings(data);
const fail = (rows) => rows.map((r) => `${r.path}: ${r.text}`).join('\n');
const none = (rows, re) => assert.equal(fail(rows.filter((r) => re.test(r.text))), '');
const fakes = strings({ FAKE_ANTIVIRUS, FAKE_PAYWALL, FAKE_RENEWAL, NEW_TERMS, SUPPORT_MENU, FAKE_KEYHOLDER, FAKE_UPDATE, FAKE_CALL, FAKE_INBOX, FAKE_CHAT, FAKE_DESKTOP });
const allCcp = [...CCP_ADS, ...Object.values(CCP_THEME_ADS).flat()];

test('house style: no em-dash, en-dash or exclamation mark anywhere', () => {
  none(all, /[\u2014\u2013!]/);
});

test('the game and currency names only ever come from tokens', () => {
  none(all, /fuzz/i);
  assert.ok(all.some((r) => r.text.includes('{cur}')));
  assert.ok(all.some((r) => r.text.includes('{game}')));
});

test('no duplicate ids across ads, and none inside questions', () => {
  const ads = [...JOKE_ADS, ...allCcp].map((a) => a.id);
  assert.equal(new Set(ads).size, ads.length);
  const qs = QUESTIONS.map((x) => x.id);
  assert.equal(new Set(qs).size, qs.length);
});

test('no line in a bank repeats', () => {
  const lines = [
    ...Object.values(THOUGHTS.general).flat(), ...Object.values(THOUGHTS.routes).flat(), ...Object.values(THOUGHTS.themes).flat(),
    ...JOKE_ADS.map((a) => a.headline), ...CCP_ADS.map((a) => a.headline), ...QUESTIONS.map((x) => x.text + x.trick),
  ];
  const seen = new Set();
  const twice = lines.filter((l) => (seen.has(l) ? true : (seen.add(l), false)));
  assert.deepEqual(twice, []);
});

test('the ads have the fields the banner slot reads, at about the sizes asked for', () => {
  assert.ok(JOKE_ADS.length >= 70 && JOKE_ADS.length <= 95, `joke ads ${JOKE_ADS.length}`);
  assert.ok(CCP_ADS.length >= 22 && CCP_ADS.length <= 30, `ccp ads ${CCP_ADS.length}`);
  for (const a of JOKE_ADS) {
    assert.ok(['banner', 'popup'].includes(a.kind), a.id);
    for (const k of ['id', 'headline', 'sub', 'cta', 'tone']) assert.ok(a[k], `${a.id}.${k}`);
  }
  for (const a of allCcp) {
    assert.ok(['banner', 'popup'].includes(a.kind), a.id);
    for (const k of ['id', 'headline', 'sub', 'cta']) assert.ok(a[k], `${a.id}.${k}`);
    assert.equal(a.url, 'https://cclabs.app', a.id);
    assert.equal(a.clip, null, a.id);
  }
});

test('the ccp ads stay honest: no invented numbers or prices, no banned words', () => {
  const rows = strings(allCcp.map(({ id, url, ...shown }) => shown));
  none(rows, /\d|[$\u20ac\u00a3]|\b(usd|eur|per month|a month|partner|subject|lab)\b/i);
  assert.ok(rows.some((r) => /Basic/.test(r.text)));
  assert.ok(rows.some((r) => /Prime/.test(r.text)));
});

test('themes that name a mod have their ads', () => {
  for (const [theme, name] of [['bimbo', /Bambi/], ['drone', /Drone/], ['sissy', /Sissy/], ['goon', /Goon/], ['locked', /Chaster|Locked/], ['infection', /Infection/]]) {
    assert.ok(CCP_THEME_ADS[theme].length >= 2, theme);
    assert.ok(CCP_THEME_ADS[theme].some((a) => name.test(a.headline + a.sub)), theme);
  }
});

test('the ticker has about 150 lines and later general lines drift further', () => {
  const total = [...Object.values(THOUGHTS.general), ...Object.values(THOUGHTS.routes), ...Object.values(THOUGHTS.themes)].flat().length;
  assert.ok(total >= 135 && total <= 175, `thoughts ${total}`);
  const mean = (a) => a.reduce((n, s) => n + s.length, 0) / a.length;
  assert.ok(mean(THOUGHTS.general.late) < mean(THOUGHTS.general.mid));
  assert.ok(new Set(THEME_IDS).size === 8 && THEME_IDS.every((t) => THOUGHTS.themes[t]?.length));
  assert.ok(thoughtsFor({ stage: 'late', routes: ['eyes'], themes: ['goon'] }).length > thoughtsFor().length);
});

test('every question has a listed trick and at least 2 answers', () => {
  assert.ok(QUESTIONS.length >= 50 && QUESTIONS.length <= 75, `questions ${QUESTIONS.length}`);
  for (const x of QUESTIONS) {
    assert.ok(Object.hasOwn(TRICKS, x.trick), `${x.id} trick ${x.trick}`);
    assert.ok(x.answers.length >= 2, x.id);
    for (const a of x.answers) {
      assert.ok(a.label, x.id);
      assert.ok(['yes', 'no', 'odd'].includes(a.tone), x.id);
    }
  }
});

test('every trick has questions and every question can be finished', () => {
  for (const t of Object.keys(TRICKS)) assert.ok(QUESTIONS.some((x) => x.trick === t), `no question uses ${t}`);
  for (const x of QUESTIONS) assert.ok(x.answers.some((a) => a.tone !== 'no'), `${x.id} only refuses`);
  for (const x of QUESTIONS.filter((y) => y.trick === 'verify-transcribe')) assert.ok(x.phrase, x.id);
});

test('the endings cover every kind end row and each card is complete', () => {
  const ids = existsSync(ROWS)
    ? JSON.parse(readFileSync(ROWS, 'utf8')).filter((r) => r.kind === 'end').map((r) => r.id)
    : PINNED_ENDS;
  assert.deepEqual([...ids].sort(), [...PINNED_ENDS].sort());
  assert.deepEqual(Object.keys(ENDINGS).sort(), [...ids].sort());
  for (const [id, e] of Object.entries(ENDINGS)) {
    assert.equal(e.id, id);
    assert.ok(e.title && e.result && e.closing, id);
    assert.ok(e.credits.length >= 3 && e.credits.length <= 6, id);
    if (e.link) assert.equal(e.link.url, 'https://cclabs.app', id);
  }
});

test('Drop has its word list, and only names clips the tree ships', () => {
  assert.ok(DROP_WORDS.length >= 6);
  for (const w of DROP_WORDS) {
    assert.ok(w.word, 'word');
    if (w.clip !== null) assert.ok(CIRCE.includes(w.clip), w.clip);
  }
});

test('all eight themes are written, and locked is shown as Chastity', () => {
  assert.deepEqual(Object.keys(THEMES).sort(), [...THEME_IDS].sort());
  for (const t of THEME_IDS) {
    const m = THEMES[t];
    assert.equal(m.id, t);
    assert.ok(m.label && m.buttonWords.length >= 8 && m.ticker.length >= 6 && m.shopLines.length >= 5, t);
    assert.ok(m.buttonWords.every((w) => w.length <= 16), t);
  }
  assert.equal(THEMES.locked.label, 'Chastity');
});

test('nothing asks for a card, a password or an email, anywhere', () => {
  none(all, /pass ?word|pass ?code|\bpin\b|e-?mail|credit card|debit card|\bcvv\b|\bcvc\b|card number|your card|billing address|sort code|social security|login|log in|sign in/i);
});

test('the fakes use in-game money only, are not real links, and never fake Wake Up', () => {
  none(fakes, /[$\u20ac\u00a3]|\b(usd|eur|euro|euros|dollar|dollars|pounds)\b/i);
  none(fakes, /https?:|www\./i);
  none(fakes, /wake ?up/i);
  /* Nothing in the data offers a free text field. */
  for (const r of strings(data)) assert.ok(!r.path.match(/\.(input|placeholder|field)$/), r.path);
});

test('every link in the content goes to cclabs.app', () => {
  const urls = all.filter((r) => /https?:/.test(r.text));
  for (const u of urls) assert.equal(u.text, 'https://cclabs.app', u.path);
});

test('adult, not explicit, and nothing young', () => {
  none(all, /\b(cock|dick|pussy|cunt|cum|tits|penis|vagina|orgasm|anal|fuck\w*|slut\w*|whore\w*)\b/i);
  none(all, /\b(school\w*|student\w*|pupil\w*|class\w*|teen\w*|underage|minor\w*|kid\w*|child\w*|baby|babies|little (one|girl|boy)|young\w*|homework|detention|teacher\w*)\b/i);
});

test('the writing does not assume a gender of the player', () => {
  const rows = strings({ THOUGHTS, QUESTIONS, THEMES, ENDINGS, SPONSORED_THOUGHTS, NATIVE_LINES, FAKE_CHAT, FAKE_INBOX, FAKE_KEYHOLDER });
  none(rows, /\b(girl|girls|boy|boys|princess|lady|ladies|gentleman|sir|madam|miss|mister|woman|women|wife|husband)\b/i);
  none(rows.filter((r) => !/EMI|Circe/.test(r.text)), /\b(she|her|hers|he|him|his)\b/i);
});

test('EMI ad reads are short beats', () => {
  for (const read of EMI_AD_READS) assert.ok(read.length >= 3 && read.every((b) => b.length <= 24), read.join(' / '));
});
