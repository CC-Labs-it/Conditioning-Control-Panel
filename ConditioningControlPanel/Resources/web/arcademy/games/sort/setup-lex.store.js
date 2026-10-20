/* ============================================================================
 * games/sort/setup-lex.store.js - THE DOOR's fallback rows for the phone's
 * STORE TREE (Google Play, the App Store).
 *
 * The vendoring script copies this over setup-lex.js in the store tree
 * (core/storesafe.js). The keys are setup-lex.js's, every one of them; only
 * the rows that name subreddits, subs or niches differ, and each of those reads
 * exactly what store-build.json's lexicon set writes into the store tree's
 * lexicon.json, so a row the host never sends falls back to the same words the
 * host would have sent. Tests/arcademy/storebuild.test.mjs holds the keys and
 * those rows together. Change a store row in both places or in neither.
 *
 * setup-lex.js's RULES apply here unchanged.
 * ==========================================================================*/

export const SETUP_LEX = Object.freeze({
  /* ---- the door itself -------------------------------------------------- */
  sort_door_title: 'Set your sort',
  sort_door_sub: 'Right is yours. Left is the rest.',
  sort_door_step: 'Step',
  sort_leave: 'Leave',
  sort_back: 'Back',
  sort_next: 'Next',
  sort_play: 'Deal me in',
  sort_dealing: 'Dealing your deck',
  sort_vetting: 'Checking your cards',
  sort_vet_more: 'Fetching more cards',
  sort_vs: 'vs',

  /* ---- first night hand holding (hidden by Skip class tutorials) --------- */
  sort_tut_rule: 'One rule all class: yours goes right, the rest goes left.',
  sort_tut_pick: 'You pick both piles now. They do not change once the bell rings.',
  sort_tut_ghost: 'Watch two cards sort themselves, then it is your turn.',

  /* ---- step 1: the source ----------------------------------------------- */
  sort_step_source: 'Where from',
  sort_source_online: 'Online',
  sort_source_online_hint: 'Pictures from the online feed',
  sort_source_online_off: "Online pictures aren't available in this version",
  sort_source_local: 'My folders',
  sort_source_local_hint: 'Folders and presets from your own assets',
  sort_source_local_off: 'Not enough folders or presets to make two piles',

  /* ---- step 2 / 3: the piles -------------------------------------------- */
  sort_step_target: 'Your pile',
  sort_step_noise: 'The rest',
  sort_target_head: 'What do you want?',
  sort_target_hint: 'These go RIGHT. Pick one or more.',
  sort_noise_head: 'What is the rest?',
  sort_noise_hint: 'These go LEFT. Pick one or more.',
  sort_catalog_head: 'Collections',
  sort_lib_head: 'My library',
  sort_lib_empty: 'Nothing here yet. Search for a collection below.',
  sort_starter_head: 'Easy noise',
  sort_starter_hint: 'One tap. Checked once, then yours forever.',

  /* ---- the search box ---------------------------------------------------- */
  sort_search_head: 'Add a collection',
  sort_search_ph: 'collection name',
  sort_search_btn: 'Add',
  sort_probe_probing: 'Checking',
  sort_probe_ok: 'Added to your library',
  sort_probe_missing: 'Not found',
  sort_probe_bad: 'That is not a collection name',
  sort_probe_dupe: 'Already on your list',

  /* ---- library pills ----------------------------------------------------- */
  sort_clips: 'clips',
  sort_stills_only: 'stills only',
  sort_verified: 'verified',
  sort_unverified: 'never checked',
  sort_missing: 'gone from the feed',
  sort_remove: 'Remove from my library',

  /* ---- local picking ----------------------------------------------------- */
  sort_folders_head: 'Folders',
  sort_presets_head: 'Or a whole preset',
  sort_preset_none: 'No preset',
  sort_folder_taken: 'on the other pile',
  sort_counts: 'items',

  /* ---- the rules the door enforces --------------------------------------- */
  sort_need_pick: 'Pick at least one first',
  sort_need_split: 'The two piles cannot be the same',
  sort_overlap_note: 'Shared picks were dropped from the rest',
  sort_spice_mild: 'Mild. These two are easy to tell apart.',
  sort_spice_mid: 'Warm. Your own picks on both sides.',
  sort_spice_hot: 'Hot. These two collections share ground.',
  sort_thin: 'Thin pick. Expect repeats.',
  sort_thin_add: 'Add another pick',

  /* ---- night two --------------------------------------------------------- */
  sort_same: 'Same sort',
  sort_change: 'Change my sort',
  sort_stale: 'That sort is gone. Pick again.',

  /* ---- QUICK SORT (the dark campus fallback) ----------------------------- */
  sort_quick_head: 'Quick sort',
  sort_quick_rule: 'Moving goes right. Still goes left.',
  sort_quick_nag: 'Add a second folder for a real sort.',

  /* ---- the ghost round --------------------------------------------------- */
  sort_ghost_head: 'Like this',
  sort_ghost_target: 'yours',
  sort_ghost_noise: 'the rest',
  sort_stamp_yes: 'YES',
  sort_stamp_no: 'NO',
});

export default SETUP_LEX;
