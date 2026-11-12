/* ============================================================================
 * data/content/ads.js - the ad network. Pure data.
 *
 * JOKE_ADS      a slightly unhinged ad network. Nothing here is a real product.
 * CCP_ADS       honest cards for the real app. Never lie, no numbers, no prices.
 * CCP_THEME_ADS extra honest cards once a theme that names a mod is picked.
 * SPONSORED_THOUGHTS, NATIVE_LINES, EMI_AD_READS: the More Ads lane.
 *
 * Tokens: {game} and {cur}, filled from data/brand.js.
 * ==========================================================================*/

const CCP_URL = 'https://cclabs.app';

const joke = (id, kind, tone, headline, sub, cta) => ({ id, kind, headline, sub, cta, tone });
const ccp = (id, kind, headline, sub, cta) => ({ id, kind, headline, sub, cta, url: CCP_URL, clip: null });

export const JOKE_ADS = Object.freeze([
  /* The network's regulars: socks, soap, hats, buttons. They come back. */
  joke('j_socks', 'banner', 'deadpan', 'Socks. Buy socks.', 'They go on feet. We checked.', 'Buy socks'),
  joke('j_socks2', 'banner', 'deadpan', 'More socks.', 'You bought the last ones. We saw.', 'Get more'),
  joke('j_socks3', 'banner', 'desperate', 'Please, the socks.', 'The warehouse is full and we have started to hear them.', 'Help us'),
  joke('j_socks4', 'popup', 'corporate', 'A message from Socks', 'We know you have been seeing other socks. We are not angry. We are on your feet right now.', 'Apologise'),
  joke('j_soap', 'banner', 'local', 'Hot soap near you', '3 bars within 2 miles want to meet.', 'Meet soap'),
  joke('j_soap2', 'banner', 'local', 'Soap in your area is lonely', 'It has been in the dish since March.', 'Say hello'),
  joke('j_soap3', 'popup', 'scam', 'Your soap has matched', 'A bar of lavender has viewed your profile 40 times. It does not have eyes. Think about that.', 'See who'),
  joke('j_hat', 'banner', 'scam', 'One weird hat', 'Milliners are furious and cannot say why.', 'See hat'),
  joke('j_hat2', 'banner', 'wellness', 'The Weighted Hat', 'Anxiety, meet your neck.', 'Feel held'),
  joke('j_hat3', 'banner', 'deadpan', 'Hats for the hat you have', 'It deserves a little something.', 'Layer up'),
  joke('j_btn', 'banner', 'local', 'Hot buttons near you', 'Lonely. Round. Ready to be pressed.', 'Press here'),
  joke('j_click', 'banner', 'scam', 'One weird click', 'Doctors hate it. Doctors have not seen it.', 'Click once'),
  joke('j_btn2', 'banner', 'wellness', 'Buttons Anonymous', 'Meetings on Thursday. Nobody has left since 2019.', 'Attend'),
  joke('j_fing', 'banner', 'pharma', 'Is your finger tired', 'Ask your doctor about Other Finger.', 'Learn more'),

  /* Goods */
  joke('j_ladder', 'popup', 'scam', 'You have won a ladder', 'Collect it in person. Bring a friend and a van. Do not tell the ladder.', 'Claim ladder'),
  joke('j_matt', 'banner', 'desperate', 'Mattress warehouse closing for ever', 'Reopening Tuesday. Closing for ever again on Thursday.', 'Panic buy'),
  joke('j_bread', 'banner', 'corporate', 'Bread, on subscription', 'One loaf a day, left outside, at an hour we choose.', 'Subscribe to bread'),
  joke('j_umb', 'banner', 'deadpan', 'Single-use umbrellas', 'Good for one rain.', 'Stock up'),
  joke('j_air', 'banner', 'wellness', 'Bottled indoor air', 'From a room in Belgium. Notes of carpet.', 'Inhale'),
  joke('j_lamp', 'banner', 'deadpan', 'Buy one lamp, get a lamp opinion', 'It thinks you could do better.', 'Hear it out'),
  joke('j_stairs', 'banner', 'local', 'Discount Stairs', 'All of the height. Some of the steps.', 'Climb cheap'),
  joke('j_gravel', 'banner', 'local', 'Family recipe gravel', 'Passed down four generations. It has not changed.', 'Taste gravel'),
  joke('j_moat', 'banner', 'corporate', 'Free sample of a moat', 'One bucket. Pour it around something you own.', 'Get bucket'),
  joke('j_rice', 'banner', 'deadpan', 'Your name on a grain of rice', 'Sold in bags of unrelated rice.', 'Find it'),
  joke('j_lanyard', 'banner', 'wellness', 'Emotional support lanyard', 'Nobody asks who you are any more.', 'Wear it'),
  joke('j_chair', 'banner', 'wellness', 'The Chair That Agrees', 'You were right about all of them.', 'Sit down'),
  joke('j_pillow', 'banner', 'deadpan', 'A pillow that has seen things', 'Priced to move. It wants to move.', 'Take it'),
  joke('j_plant', 'banner', 'wellness', 'Fake plant. Real feelings.', 'It will not die. It will outlive the relationship.', 'Adopt'),
  joke('j_trap', 'banner', 'corporate', 'Trapdoor installation', 'Now taking bookings. You will not hear us arrive.', 'Book a visit'),
  joke('j_stash', 'banner', 'deadpan', 'A moustache for the car', 'Have you considered it. Your neighbours have.', 'Fit one'),
  joke('j_apol', 'banner', 'corporate', 'Second-hand apologies', 'Lightly used. One careful owner, sorry.', 'Browse sorry'),
  joke('j_regret', 'banner', 'local', 'Mid-size Regrets Outlet', 'Off the ring road, next to the other one.', 'Get directions'),
  joke('j_soup', 'banner', 'corporate', 'Cloud storage for soup', 'Access your minestrone anywhere.', 'Upload soup'),

  /* Services */
  joke('j_chairchk', 'popup', 'scam', 'Is your chair watching you', 'Free chair inspection. We already know the answer. It told us.', 'Book inspection'),
  joke('j_law', 'banner', 'corporate', 'Lawyers for people who pressed something', 'Did you press something. You may be owed a feeling.', 'Check eligibility'),
  joke('j_latin', 'banner', 'local', 'Learn Latin in a parked car', 'Lessons come to you. They do not leave.', 'Salve'),
  joke('j_gutter', 'popup', 'scam', 'Your gutters are worse than average', 'A satellite told us. It seemed upset. Same-day visit, nobody will speak.', 'Clear gutters'),
  joke('j_goose', 'banner', 'corporate', 'Rent a goose', 'Security, companionship, consequences.', 'Rent goose'),
  joke('j_router', 'banner', 'wellness', 'Your horoscope, about your router', 'Mercury is in 2.4 gigahertz. Avoid your neighbours.', 'Read it'),
  joke('j_queue', 'banner', 'corporate', 'Professional queue standers', 'We wait in line so you can wait at home.', 'Hire one'),
  joke('j_isoup', 'banner', 'deadpan', 'Is it soup', 'Send a photo. We will tell you. We are always sure.', 'Ask the panel'),
  joke('j_ssheet', 'banner', 'corporate', 'Spreadsheets, exorcised', 'Column F moved on its own again, did it.', 'Call us'),
  joke('j_bees', 'banner', 'corporate', 'Bees as a Service', 'Flat monthly rate. Bees.', 'Start free trial'),
  joke('j_stapler', 'banner', 'local', 'Meet singles who also own a stapler', 'You already have so much in common.', 'Staple together'),
  joke('j_notary', 'banner', 'scam', 'Become a notary by accident', 'It happens to thousands every year. Learn the signs.', 'Learn signs'),
  joke('j_opin', 'banner', 'corporate', 'Rent our opinion of you', 'Weekly or monthly. You can see it from the road.', 'View opinion'),
  joke('j_ghost', 'banner', 'corporate', 'A ghostwriter for your texts', 'We will say you are on your way.', 'Be on your way'),
  joke('j_shadow', 'popup', 'scam', 'Find out who owns your shadow', 'Public records do not lie. A surprising number of them are held in Delaware.', 'Search now'),
  joke('j_gym', 'banner', 'wellness', 'Gym membership, no gym', 'Same result as your last one, lower price.', 'Join nothing'),
  joke('j_nap', 'banner', 'pharma', 'Side effects may include nap', 'We have not said what the product is. It is the nap.', 'Lie down'),
  joke('j_whistle', 'banner', 'desperate', 'Learn to whistle before it is too late', 'Nobody will say what happens. It happens soon.', 'Start now'),
  joke('j_warranty', 'popup', 'corporate', 'Extended warranty on your last decision', 'We have been trying to reach you about it. It is out of cover and making a noise.', 'Extend it'),
  joke('j_grief', 'banner', 'pharma', 'Sad Trombone Insurance', 'If it happens to you, we send the trombone.', 'Get covered'),
  joke('j_dogh', 'banner', 'local', 'Hypnotist for your dog', 'It will bark when you snap. It already did that.', 'Book session'),
  joke('j_cult', 'popup', 'desperate', 'Our beginner cult has spaces', 'Robes included. Lunch is not. Nobody has asked to leave, because the door is a long way off.', 'Reserve a robe'),

  /* Money */
  joke('j_retire', 'banner', 'scam', 'Retire at 40', 'From one specific job. We pick the job.', 'Find out which'),
  joke('j_bridge', 'banner', 'scam', 'Own a small portion of a bridge', 'Historic returns on a historic bridge. Mostly the middle bit.', 'Invest'),
  joke('j_banana', 'banner', 'corporate', 'Banana futures', 'The future is yellow, then brown. Act now.', 'Go long'),
  joke('j_crypto', 'banner', 'scam', 'Crypto for pets', 'Your dog is already in. Your dog did not tell you.', 'Check dog'),
  joke('j_loan', 'popup', 'corporate', '{cur} Loans', 'Borrow {cur} at a rate we can only describe as a rate. Repay in {cur}, or in something else.', 'Read nothing, sign'),
  joke('j_swap', 'banner', 'scam', 'Sell your {cur} for more {cur}', 'A real offer. We have run the numbers and they ran away.', 'Sell it all'),
  joke('j_biz', 'banner', 'corporate', '{game} for Business', 'Tap together. Synergy of up to one button.', 'Talk to sales'),
  joke('j_reverse', 'banner', 'corporate', 'Reverse mortgage on your attention', 'We pay you monthly. You just stop owning the afternoon.', 'See your rate'),

  /* The network knows what site it is on */
  joke('j_spinfix', 'banner', 'local', 'Spiral turning the wrong way', 'Same-day call-out. We do clockwise and the other one.', 'Call now'),
  joke('j_second', 'banner', 'pharma', 'Get a second opinion on your spiral', 'The first one was also a spiral.', 'Ask again'),
  joke('j_pend', 'banner', 'deadpan', 'Buy a pendulum, get a nap', 'The offer is the nap. The pendulum is shipping.', 'Add to basket'),
  joke('j_blink', 'banner', 'corporate', 'Blink Premium', 'Now on both eyes.', 'Upgrade'),
  joke('j_breathe', 'banner', 'wellness', 'Deep breathing, now with ads', 'In for four. Hold for this message. Out for four.', 'Keep holding'),
  joke('j_insure', 'popup', 'corporate', 'Trance Insurance', 'If you go under, we go under too. It is in the terms. We also did not read them.', 'Get a quote'),
  joke('j_blank', 'banner', 'corporate', 'Blankness consultants', 'We look at your head and invoice it.', 'Book a look'),
  joke('j_declutter', 'popup', 'wellness', 'Declutter your mind today', 'Everything must go, starting with the name of your street. You will not miss it. You will not know.', 'Start clearing'),
  joke('j_7days', 'banner', 'pharma', 'Stop thinking in 7 days', 'Or 6. You will lose count, which is how it works.', 'Begin'),
  joke('j_drops', 'banner', 'pharma', 'Eye drops for competitive staring', 'Banned in four leagues.', 'Stop blinking'),
  joke('j_adfree', 'popup', 'corporate', 'Try Ad-Free Ads', 'All of the ads, none of the ads. We are not able to explain it and legal asked us to stop trying.', 'Go ad-free'),
  joke('j_thisad', 'banner', 'deadpan', 'This space available', 'So is the space next to it. Nobody wants that one either.', 'Enquire'),
  joke('j_review', 'banner', 'scam', 'Five stars, would press again', 'A real review from a bar of soap.', 'Read reviews'),
  joke('j_survey', 'popup', 'scam', 'You are our one millionth tap', 'Not your millionth. Ours. We have been counting everybody. Nothing happens now, we just thought you should know.', 'Thank you'),
  joke('j_cookie', 'popup', 'corporate', 'We use cookies', 'They are oatmeal. They are in the car. Accepting changes nothing about the car.', 'Accept oatmeal'),
  joke('j_near', 'banner', 'local', 'Nothing near you', 'Open now. Nothing has great reviews.', 'Get directions'),
]);

export const CCP_ADS = Object.freeze([
  ccp('c_actual', 'banner', 'We also make an actual app.', 'Conditioning Control Panel. Spirals, flashes and subliminals on your whole desktop, not one tab.', 'See the app'),
  ccp('c_honest', 'banner', 'An honest ad. It has been a while.', 'CCP is a free Windows app for adults who like this sort of thing. Yes, this sort of thing.', 'Have a look'),
  ccp('c_socks', 'banner', 'The only product on this network that exists.', 'CCP. Sorry about the socks.', 'See it exist'),
  ccp('c_cutbutton', 'banner', 'You are tapping a button to watch a spiral.', 'CCP cuts out the button.', 'Lose the button'),
  ccp('c_dayjob', 'banner', 'The spiral has a day job.', 'In CCP it runs over your desktop, when you ask it to.', 'See it work'),
  ccp('c_panic', 'popup', 'It has a panic key.', 'Press it in CCP and everything stops. No other advertiser on this network will tell you that about their goose.', 'Read more'),
  ccp('c_free', 'banner', 'Free to download. We asked ourselves.', 'Some of it is Basic or Prime. We label which. You find out by wanting it.', 'Get CCP'),
  ccp('c_tiers', 'banner', 'Basic and Prime.', 'Those are the plans. Someone had seen enough plan names.', 'Compare them'),
  ccp('c_race', 'banner', 'Racing Thoughts is a real kart game.', 'Five courses, songs, words on the road. It lives in CCP and it is for Prime.', 'See the race'),
  ccp('c_chess', 'banner', 'Chess, except it distracts you.', 'Piece by Piece is free for everyone in CCP. Your IQ goes down and it stays down.', 'Lose at chess'),
  ccp('c_dtrh', 'banner', 'Down the Rabbit Hole.', 'A trance game in CCP where the big moments get quieter. Basic.', 'Go down'),
  ccp('c_drop', 'banner', 'Just Drop.', 'In CCP, for everyone, on every plan. You can probably guess the rest.', 'Guess correctly'),
  ccp('c_bubbles', 'banner', 'You can pop bubbles in CCP as well.', 'Over your real desktop, in front of your real spreadsheet.', 'Pop something'),
  ccp('c_video', 'popup', 'CCP can play a video you cannot skip.', 'You set that up yourself, on purpose, in a settings page. That is the funny part.', 'Set it up'),
  ccp('c_goon', 'banner', 'Goon is a real game too.', 'Inside CCP. Subscribers host a room, anyone signed in can join.', 'Find a room'),
  ccp('c_chaster', 'banner', 'CCP talks to Chaster.', 'Link it and a slip-up can add real time to a real lock. Your idea, your call.', 'Read how'),
  ccp('c_intake', 'popup', 'These pop-ups have a parent.', 'Graded Intake is a whole questionnaire in CCP, and it cheats like this for the full length.', 'Take the test'),
  ccp('c_leash', 'banner', 'In CCP a friend can hold a leash.', 'You can cut it any time, and cutting it never costs a thing.', 'Read how'),
  ccp('c_toys', 'banner', 'CCP can link a toy to the screen.', 'You will have to explain that to the toy.', 'See what works'),
  ccp('c_emi', 'banner', 'EMI has a desk job.', 'In CCP she sits on your actual desktop and sulks there. She has heard about this game.', 'Meet EMI'),
  ccp('c_madeby', 'banner', 'Made by CC Labs.', 'The people who made this game also made the app, so adjust your expectations in either direction.', 'Meet the app'),
  ccp('c_session', 'banner', 'Your finger can rest.', 'CCP runs whole sessions, start to finish, with no tapping required.', 'See sessions'),
  ccp('c_sublim', 'banner', 'Words on screen for a blink.', 'CCP has those too. You write the list. You have only yourself to blame.', 'Pick your words'),
  ccp('c_local', 'banner', 'Bring your own pictures.', 'CCP plays your own folders of images and videos. Your taste, now full screen.', 'Open a folder'),
  ccp('c_adults', 'popup', 'For adults, as you confirmed earlier.', 'CCP is the same deal as this game with far more settings. Every switch in it is yours.', 'See the switches'),
  ccp('c_bar', 'banner', 'Worth more than {cur}.', 'A low bar, and we still checked. CCP.', 'Clear the bar'),
]);

/* Rows: bi_head, dr_head, si_head, go_head, lo_head, in_head make CCP ads name a mod or game. */
export const CCP_THEME_ADS = Object.freeze({
  bimbo: Object.freeze([
    ccp('c_bi_mod', 'banner', 'This theme has a grown-up version.', 'The Bambi mod for CCP, with its own look and thousands of recorded clips. You will not remember reading this.', 'What was it'),
    ccp('c_bi_talks', 'banner', 'The companion in the Bambi mod has a voice.', 'Recorded clips, never synthetic. It has more lines than you have thoughts at the moment.', 'Hear it'),
    ccp('c_bi_read', 'popup', 'Too many words in this ad.', 'CCP. Bambi mod. Pretty. Press the pink one.', 'Pink one'),
  ]),
  drone: Object.freeze([
    ccp('c_dr_mode', 'banner', 'Drone mode, in CCP.', 'The companion is DroneOS and you are a Unit. Names are not required.', 'Acknowledge'),
    ccp('c_dr_unit', 'banner', 'Unit, your next upgrade is an app.', 'Drone mode runs on your desktop. This has been a status report and also an ad.', 'Comply here'),
    ccp('c_dr_notice', 'popup', 'Notice to all units.', 'CCP has a Drone mode. No further information is required by this unit, but it is available.', 'Query'),
  ]),
  sissy: Object.freeze([
    ccp('c_si_mod', 'banner', 'The Sissy mod, in CCP.', 'A whole wardrobe of recorded lines, and a companion who calls everyone babe.', 'Dress for it'),
    ccp('c_si_manners', 'banner', 'Good manners extend to your desktop.', 'The Sissy mod for CCP. Sit up while you read about it.', 'Enquire nicely'),
    ccp('c_si_invite', 'popup', 'You are invited.', 'The Sissy mod in CCP requests the pleasure of your company. Dress is whatever it says it is.', 'Accept, please'),
  ]),
  goon: Object.freeze([
    ccp('c_go_game', 'banner', 'You are in the right mood for Goon.', 'The game inside CCP. Subscribers host, anyone signed in joins, nobody knows what time it is.', 'Find a room'),
    ccp('c_go_duels', 'banner', 'Goon has duels now. Goon has always had duels.', 'Real minigames, real opponents, in CCP. Practice is free.', 'Practise'),
    ccp('c_go_room', 'popup', 'Other people are doing this as well.', 'That is the whole pitch of Goon, in CCP. Curtains not included.', 'Join them'),
  ]),
  locked: Object.freeze([
    ccp('c_lo_chaster', 'banner', 'This timer is pretend. Chaster is not.', 'CCP links to the real thing, and only adds what you let it add.', 'See how'),
    ccp('c_lo_mod', 'banner', 'The Locked mod, in CCP.', 'Voiced by Circe. She calls you pet and she means it.', 'Listen to her'),
    ccp('c_lo_never', 'popup', 'Panic and unlinking are never priced.', 'In CCP, no slip-up charge ever lands on those. Everything else, you should ask nicely.', 'Ask nicely'),
  ]),
  infection: Object.freeze([
    ccp('c_in_mod', 'banner', 'Infection Control, a mod in CCP.', 'It has a nurse called Amber, and Amber has news. The news is that it progresses.', 'See Amber'),
    ccp('c_in_cure', 'banner', 'There is a real ward for this.', 'Infection Control runs on your desktop in CCP. Visiting hours are up to you.', 'Get admitted'),
    ccp('c_in_chart', 'popup', 'Your chart has been forwarded.', 'To Infection Control, a mod in CCP. Amber has read it and made a face.', 'See the ward'),
  ]),
});

/* Sponsored Thoughts: ad reads that slip in among the ticker lines. Tag them AD. */
export const SPONSORED_THOUGHTS = Object.freeze([
  'I should really call about the, this thought is brought to you by soap',
  'What I need right now is a hat. One weird hat',
  'I was going to remember my, have you considered socks',
  'A word from our sponsor, your head',
  'The thing about silence is that it is available to rent',
  'My mother always said buy local gravel',
  'I feel a sudden calm about bridges and owning part of one',
  'Deep down I know what this tap needs, and it is bees, as a service',
  'This idle moment was paid for by Discount Stairs',
  'Nothing is near me and it has great reviews',
  'Thinking about CCP for no reason at all, the reason is that this is an ad',
  'Wherever I go there I am, and there are socks',
]);

/* Disguised Ads: shop-tile descriptions start recommending things. */
export const NATIVE_LINES = Object.freeze([
  'Pairs well with soap.',
  'Buyers of this also bought socks.',
  'Tastes like a hat you have not met.',
  'Now with notes of Belgian carpet.',
  'Our editors loved this, and also a goose.',
  'Not available in stores. The stores asked.',
  'Nine in ten chairs agree.',
  'Goes with most moats.',
  'Ask your doctor about Other Finger.',
  'See also: Conditioning Control Panel, which is a real app.',
  'As recommended by a satellite.',
  'Comes with a free opinion of you.',
]);

/* Sponsored Mascot: EMI reads ad copy, badly. One beat per bubble. */
export const EMI_AD_READS = Object.freeze([
  ['Buy...', 'soap?', 'Um.', 'Soap.'],
  ['Hot socks.', 'No.', 'It says hot socks.', 'I do not make the socks.'],
  ['This episode.', 'There is no episode.', 'Is brought to you.', 'By bees.'],
  ['Rent a goose.', 'I rented one.', 'It is still here.', 'Help.'],
  ['Trance insurance.', 'If you go under.', 'We go under.', 'That is bad, right.'],
  ['Now a word.', 'From our sponsor.', 'The word is.', 'I lost the card.'],
  ['CCP is an app.', 'That one is true.', 'They made me say it.', 'I also like it.'],
  ['Discount Stairs.', 'Some of the steps.', 'Which steps.', 'Do not ask me.'],
  ['Banana futures.', 'What is a future.', 'Is it a banana.', 'Fine.'],
  ['Say the name.', 'Of the brand.', 'With a smile.', 'This is my smile.'],
]);
