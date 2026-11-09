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
  THOUGHTS, thoughtsFor, QUESTIONS, TRICKS, quizPay, ENDINGS, DROP_WORDS, THEMES, THEME_IDS,
  FAKE_ANTIVIRUS, FAKE_PAYWALL, FAKE_RENEWAL, NEW_TERMS, SUPPORT_MENU, FAKE_KEYHOLDER,
  FAKE_UPDATE, FAKE_CALL, FAKE_INBOX, FAKE_CHAT, FAKE_DESKTOP,
  PRAISE, PRAISE_NAMED, REST_LINES, TUCK_LINES, GLOW_LINES, WARM_LINES, NAP_LINES,
  ORDERS, DENIED_LINES, COUNT_LINES, BEAT_LINES, STILL_TEST, PHRASES, PHRASE_LINES, CORNER_LINES, REVIEWS,
  IMAGINE, CAPTIONS, STORIES, RADIO_LINES, FILL_BLANKS, INK_ANSWERS, INK_REPLIES, FALSE_MEMORIES, LAZY_CAPTIONS,
  EMI_HELLO, EMI_IDLE, EMI_PET, EMI_BUY_ANY, EMI_BUY_LANE, EMI_BUY_ITEM, EMI_HATS, EMI_JEALOUS, EMI_MINI, EMI_NAP, EMI_PLAY,
  EMI_RENAMES, EMI_RENAME_TEMPLATES, EMI_BOSS_LINES, EMI_EARLY,
  LISTENING_TEST, OPINIONS, LUCKY_WORDS, BOUNCE_WORDS, BIG_WORDS, EDGE_WORDS, STILL_WORDS, STARE_LINES, PHONES_PROMPT, TAB_TITLES,
  GOLDEN, SCREEN_TIME, LEVELS, WHEEL, BOXES, APP_ONLY, FALLING_WORDS, REMOTE, DRIFT_LINES, FEED_LINES, ACHIEVEMENTS,
  COMBO_LINES, SLEEP_STORY, AUDIO_DRAMA, INDUCTION, MOODS, MOOD_CARD, AFTER_RUN,
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

test('every question has a listed trick and four answers', () => {
  assert.ok(QUESTIONS.length >= 50 && QUESTIONS.length <= 75, `questions ${QUESTIONS.length}`);
  for (const x of QUESTIONS) {
    assert.ok(Object.hasOwn(TRICKS, x.trick), `${x.id} trick ${x.trick}`);
    assert.equal(x.answers.length, 4, x.id);
    assert.equal(new Set(x.answers.map((a) => a.label)).size, 4, `${x.id} says the same thing twice`);
    for (const a of x.answers) {
      assert.ok(a.label, x.id);
      assert.ok(['yes', 'no'].includes(a.tone), x.id);
      assert.ok(a.label.length <= (x.trick === 'bubble-pop' ? 12 : x.trick === 'crowd' || x.trick === 'lean-slider' ? 14 : 24), `${x.id} "${a.label}" is too long for its card`);
    }
  }
});

test('exactly one answer is what the game wants, except on Only Yes, where they all are', () => {
  for (const x of QUESTIONS) {
    const wanted = x.answers.filter((a) => a.tone === 'yes').length;
    assert.equal(wanted, x.trick === 'mono' ? 4 : 1, x.id);
  }
});

test('the slider ends on the answer it leans to', () => {
  for (const x of QUESTIONS.filter((y) => y.trick === 'lean-slider')) assert.equal(x.answers[3].tone, 'yes', x.id);
});

test('the right answer pays many times a wrong one, on either route', () => {
  for (const rates of [{ fps: 100, click: 1 }, { fps: 0, click: 50 }]) {
    assert.ok(quizPay(rates, true) >= 10 * quizPay(rates, false), JSON.stringify(rates));
    assert.ok(quizPay(rates, false) > 0, 'a wrong answer still pays');
  }
  /* Before: the better answer paid 4 s or 10 taps. */
  assert.ok(quizPay({ fps: 100, click: 1 }, true) >= 4 * 400);
  assert.ok(quizPay({ fps: 0, click: 50 }, true) >= 4 * 500 * 0.9);
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
  /* x_listener is the row's own joke about the podcast host, never about the player. */
  const { x_listener: _host, ...combos } = COMBO_LINES;
  const rows = strings({
    THOUGHTS, QUESTIONS, THEMES, ENDINGS, SPONSORED_THOUGHTS, NATIVE_LINES, FAKE_CHAT, FAKE_INBOX, FAKE_KEYHOLDER,
    PRAISE, PRAISE_NAMED, REST_LINES, TUCK_LINES, GLOW_LINES, WARM_LINES, NAP_LINES,
    ORDERS, DENIED_LINES, COUNT_LINES, BEAT_LINES, STILL_TEST, PHRASES, PHRASE_LINES, CORNER_LINES, REVIEWS,
    IMAGINE, CAPTIONS, STORIES, RADIO_LINES, FILL_BLANKS, INK_ANSWERS, INK_REPLIES, FALSE_MEMORIES, LAZY_CAPTIONS,
    EMI_HELLO, EMI_IDLE, EMI_PET, EMI_BUY_ANY, EMI_BUY_LANE, EMI_BUY_ITEM, EMI_HATS, EMI_JEALOUS, EMI_MINI, EMI_NAP, EMI_PLAY,
    EMI_RENAMES, EMI_RENAME_TEMPLATES, EMI_BOSS_LINES, EMI_EARLY,
    LISTENING_TEST, OPINIONS, STILL_WORDS, STARE_LINES, GOLDEN, SCREEN_TIME, LEVELS, WHEEL, BOXES, FEED_LINES, ACHIEVEMENTS,
    combos, SLEEP_STORY, AUDIO_DRAMA, INDUCTION, MOODS, AFTER_RUN,
  });
  /* The Bimbodoll ending's "GOOD GIRL BAMBI" flash and "Good girl." closing are the owner's own words (2026-11-08,
   * handoff-1108/TC-BIMBODOLL-ENDING-BRIEF.md): the Bambi theme's sign-off, named exception. */
  const owned = new Set(['.ENDINGS.bi_end.closing', '.ENDINGS.bi_end.flash[0]']);
  none(rows.filter((r) => !owned.has(r.path)),/\b(girl|girls|boy|boys|princess|lady|ladies|gentleman|sir|madam|miss|mister|woman|women|wife|husband)\b/i);
  none(rows.filter((r) => !/EMI|Circe/.test(r.text)), /\b(she|her|hers|he|him|his)\b/i);
});

test('EMI ad reads are short beats', () => {
  for (const read of EMI_AD_READS) assert.ok(read.length >= 3 && read.every((b) => b.length <= 24), read.join(' / '));
});

test('no export name is lost to a clash in the index', async () => {
  const names = ['ads', 'thoughts', 'questions', 'endings', 'themes', 'fakes', 'sweet', 'strict', 'blind', 'emi', 'side', 'combos'];
  const keys = (await Promise.all(names.map((n) => import(`../data/content/${n}.js`)))).flatMap((m) => Object.keys(m));
  assert.equal(new Set(keys).size, keys.length);
  assert.deepEqual([...keys].sort(), Object.keys(content).sort());
});

test('Praise: enough for a run, and the named lines name the buy', () => {
  assert.ok(PRAISE.length >= 40 && new Set(PRAISE).size === PRAISE.length);
  assert.ok(PRAISE_NAMED.length >= 10 && PRAISE_NAMED.every((l) => l.includes('{item}')));
  assert.ok(REST_LINES.resting.length && REST_LINES.broken.length && TUCK_LINES.length && GLOW_LINES.length && WARM_LINES.length);
  assert.ok(NAP_LINES.welcome.some((l) => l.includes('{pay}')));
});

test('Tap route: orders are shouted, phrases can be typed on a phone, every grade has a note', () => {
  assert.ok(ORDERS.words.includes('TAP NOW') && ORDERS.words.every((w) => w === w.toUpperCase()));
  assert.ok(PHRASES.length >= 10);
  for (const p of PHRASES) assert.match(p, /^[a-z ]{4,32}$/);
  for (const g of ['A', 'B', 'C', 'D', 'F']) assert.ok(REVIEWS[g].length >= 3, g);
  for (const bank of [DENIED_LINES.refused, DENIED_LINES.next, COUNT_LINES.won, COUNT_LINES.lost, BEAT_LINES.faster, STILL_TEST.passed, PHRASE_LINES.right, CORNER_LINES.sent]) assert.ok(bank.length >= 2);
});

test('No Pictures: stories are three short lines, blanks come back, inkblots have four answers', () => {
  assert.ok(CAPTIONS.length >= 20 && IMAGINE.length >= 8 && RADIO_LINES.length >= 12 && FALSE_MEMORIES.length >= 12 && LAZY_CAPTIONS.length >= 8);
  assert.ok(STORIES.length >= 20, 'a new story each minute');
  assert.equal(new Set(STORIES.map((s) => s.id)).size, STORIES.length);
  for (const s of STORIES) {
    assert.equal(s.lines.length, 3, s.id);
    assert.ok(s.lines.every((l) => l.length <= 34), s.id);
  }
  for (const f of FILL_BLANKS) {
    assert.ok(f.text.includes('____') && f.echo.includes('{word}') && f.options.length >= 3, f.id);
  }
  for (const a of INK_ANSWERS) assert.equal(a.length, 4);
  assert.ok(INK_REPLIES.length >= 6);
  for (const m of FALSE_MEMORIES) assert.equal(m.length, 2);
});

test('EMI: every buy lane has lines, the pet ladder matches the row, her lines name real unlocks', () => {
  assert.ok(EMI_HELLO.length >= 3 && EMI_IDLE.length >= 15 && EMI_BUY_ANY.length >= 12 && EMI_EARLY.length >= 4);
  assert.deepEqual(EMI_PET.stages.map((s) => s.mood), ['happy', 'happy', 'glee', 'annoyed', 'rage']);
  assert.equal(EMI_HATS.length, 4);
  assert.ok(EMI_JEALOUS.blocking.length && EMI_MINI.length && EMI_NAP.woken.length && EMI_PLAY.miss.length && EMI_BOSS_LINES.length);
  assert.ok(EMI_RENAME_TEMPLATES.every((t) => t.includes('{name}')));
  if (existsSync(ROWS)) {
    const rows = JSON.parse(readFileSync(ROWS, 'utf8'));
    const ids = new Set(rows.map((r) => r.id));
    for (const id of [...Object.keys(EMI_RENAMES), ...Object.keys(EMI_BUY_ITEM), ...Object.keys(COMBO_LINES)]) assert.ok(ids.has(id), id);
    for (const lane of new Set(rows.map((r) => r.lane))) {
      if (lane !== 'meta' && lane !== 'after') assert.ok(EMI_BUY_LANE[lane]?.length >= 3, lane);
    }
    assert.deepEqual(MOODS.map((m) => m.id).sort(), rows.filter((r) => r.lane === 'meta' && r.id !== 'm_mood').map((r) => r.id).sort());
    assert.deepEqual([...Object.keys(COMBO_LINES), 'x_story', 'x_audio', 'x_induct', 'x_what'].sort(), rows.filter((r) => r.lane === 'combo').map((r) => r.id).sort());
  }
});

test('Listening Test and Voice Reactions only ever use recorded clips', () => {
  assert.ok(LISTENING_TEST.words.length >= 16);
  for (const w of LISTENING_TEST.words) {
    assert.ok(CIRCE.includes(w.clip), w.clip);
    assert.ok(w.decoys.length >= 2 && !w.decoys.includes(w.word), w.word);
  }
  assert.ok(LISTENING_TEST.wrong.every((l) => l.includes('{word}')));
  for (const bank of Object.values(OPINIONS)) {
    for (const o of bank) if (o.clip !== null) assert.ok(CIRCE.includes(o.clip), o.clip);
  }
});

test('side toys: words, achievements and the honest app-only lines', () => {
  for (const bank of [LUCKY_WORDS, BOUNCE_WORDS, BIG_WORDS, EDGE_WORDS, STILL_WORDS]) assert.ok(bank.length >= 8);
  assert.ok(STARE_LINES.start.length && PHONES_PROMPT.ask && TAB_TITLES[0] === 'Quarterly Report.xlsx');
  assert.deepEqual(GOLDEN.prizes.map((p) => p.key), ['lump', 'frenzy', 'free', 'deep']);
  assert.ok(SCREEN_TIME.tips.length && LEVELS.up.length && WHEEL.results.length && BOXES.opened.length && DRIFT_LINES.length && FEED_LINES.endNear.length);
  assert.deepEqual(FALLING_WORDS.map((w) => w.word), ['FOCUS', 'BLANK', 'DEEPER']);
  assert.ok(APP_ONLY.sparkle.tag === 'app only' && APP_ONLY.toy.tag === 'app only');
  assert.ok(ACHIEVEMENTS.length >= 25);
  assert.equal(new Set(ACHIEVEMENTS.map((a) => a.id)).size, ACHIEVEMENTS.length);
  /* Remote: presets only, and panic stays off the list. */
  assert.ok(REMOTE.presets.length >= 3 && REMOTE.presets.every((p) => !/panic|wake/i.test(p.label)));
});

test('combos, moods, after the run: scripts are there and the induction comes back up', () => {
  assert.ok(SLEEP_STORY.length >= 12 && AUDIO_DRAMA.lines.length >= 8);
  assert.ok(INDUCTION.length >= 25);
  assert.match(INDUCTION.at(-1), /Awake/);
  assert.equal(MOOD_CARD.button, 'Fine');
  for (const m of MOODS) assert.ok(m.name && m.rule && m.line, m.id);
  assert.ok(AFTER_RUN.map && AFTER_RUN.ends && AFTER_RUN.share.play.includes('{game}') && AFTER_RUN.save.done);
});
