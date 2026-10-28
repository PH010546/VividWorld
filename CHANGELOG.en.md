# Changelog

*[中文](CHANGELOG.md)*

Every release states three things:

- **What changed**
- **Whether existing campaigns can simply be continued**
- **Whether you need to edit your config file yourself**: updates only add new settings; values you already have are never changed

## Unreleased

## v1.2.3

- **Fixed: relation changes toward the player from situations showed no native relation notification**: VividWorld writes the native relation directly (`SetPersonalRelation`) rather than through `ChangeRelationAction`, and the native notification is only raised by the latter. Every change whose source is a situation, whose target is the player, that is actually written to the native value and whose actual change is non-zero now raises a native-style quick notice after the write (`MBInformationManager.AddQuickInformation`, sound `event:/ui/notification/relation`) using the native strings: `str_your_relation_increased/decreased_with_notable` (person name) when the hero written to is the original hero, `str_your_relation_increased/decreased_with_clan` (clan name) when it was redirected to the clan leader (for example with `relation.personalWithPlayer` off). The clan-escalation write follows the same rule. `MAGNITUDE` is the difference before and after the write; `VALUE` is `GetRelation(Hero.MainHero)` of the hero actually written to. Ledger-only changes, changes clamped to zero by the limits, and rumor-sourced changes raise nothing. Which pair is written and by how much are unchanged, and the write is still direct. The log adds `relation notice shown (person|clan): …`, or `relation notice skipped for …: <reason>`.
- Existing campaigns can simply be continued; no config changes needed.

## v1.2.2

- **Fixed: with "Personal relations" turned off, others' feelings about you from events were not reflected in the encyclopedia**: with the setting off, the encyclopedia shows the one number a clan shares (its leader's relation with you), but changes from situations in front of you, and the opinion an NPC forms of you on hearing a rumor, were written to that person's own relation, so the number did not move when the person was not the clan leader. With the setting off they now go to the number the encyclopedia shows (with only VividWorld installed, the clan leader's relation with you), and no longer also draw in the clan, so the same thing is not counted twice against the leader. When a rumor is proven false and the change is taken back, it is taken back from the pair it was actually written to. What lands on the clan leader while the setting is off stays there after it is turned back on. With the setting on, and between NPCs, nothing changes; changes already written to a person's own relation are not moved. In this case the `grudge …` log line adds `on effective pair … (per-person relations with the player are off)`, and the clan line prints `not escalated - per-person relations with the player are off …`.
- Existing campaigns can simply be continued; no config changes needed.

## v1.2.1

- **Fixed: choosing an approach in a situation in front of you did not change how the others feel about you**: since v1.1.0 (the release that introduced it), the "relation ↑/↓" column in that window (others toward you) never reached the game, whatever the settings and other mods. It is now applied to that person's own relation with you, using the numbers for the approach; the approaches that can draw in a whole clan (refusing to hand over your man, or turning the accusation around, in the market brawl) follow the existing rule for whether the clan leader is drawn in. Your own feelings toward others are still not recorded. Earlier choices are not made up for. The log no longer shows `grudge skipped … main_hero: main_hero is the player`; it shows `grudge … -> … main_hero: requested … (base before -> after)` instead.
- Existing campaigns can simply be continued; no config changes needed.

## v1.2.0

- **Changed: with Calradia Remembers 2.7.9 or later, the news an NPC remembers reaches the AI through `heard_now.txt`**: that file sits ahead of CR's output rules, so longer news no longer weakens CR's own rules the way it could in `post_history_instructions.txt`, and the limit goes from 500 to 1000 characters (lean prompts stay at 300). Each piece of news appears in only one of the two files: 2.7.7–2.7.8 keep using `post_history_instructions.txt` (500/300), older versions still get memories added, and an unreadable CR version is treated as 2.7.9 or later. How news is picked, the opening line and the language are unchanged; memories already added to CR are left alone. The `AI live news for …` log line now names the variable it served, and the one that stands aside prints `empty (given through the other file)`.
- Existing campaigns can simply be continued; no config changes needed.

## v1.1.2

- **Fix: five lines from a wording review**: the outcome line where someone speaks up at the tavern uses him/her the second time (`{LISTENER:him|her|them}`), and the credit-deferring outcome becomes `The toast is meant for … who hands the credit to … instead`, so neither names the same person twice; the hostile reaction to an execution goes from `calls that noble?` to `What kind of noble is …?`, matching the Chinese; the close reaction to someone losing face now says who the drink is with (`have a drink with {him}`); in Traditional Chinese 咽不下 becomes 嚥不下, as in the two other lines with the same phrase.
- Existing campaigns can simply be continued; no config changes needed.

## v1.1.1

- **Fix: English rumor lines no longer hard-code man/his for someone who may be a woman**: the three lines about a prisoner let go without a ransom (`the man he had defeated` becomes `the foe he had defeated`; the fallback piece used when only the reason is left becomes `someone … had defeated was let go` and no longer mentions the ransom, matching the bystander sentences), and the seat-dispute clan leader's own closing line (`a man who doesn't even head his own` becomes `someone … their own`).
- **Fix: other hard-coded genders in English**: the veteran's jibe `some men` becomes `some people`; `a drunk man's words` becomes `words spoken in drink`; the captor's closing line `A man keeps his word` becomes `I keep my word`; the single retainer who struck first in the brawl rumors goes from `the man`/`him` to `the retainer`/`them` (plural `men` for troops stays). Developer-tool labels, event type names (`A veteran shared all they knew`, `A lord shielded their own after a brawl`), the settings hint and the start-up notice use they/their instead of he/his.
- **Changed: Traditional Chinese always uses 他**: it no longer switches to 她 by gender. The female pronoun cells now match the male ones, and the reaction lines, the seven outcome lines of the situation window and the two probe reasons all read 他; English still follows gender (him/her). What v1.1.0 said about the window writing 他/她 by gender no longer holds for Chinese.
- **Fix: a few Chinese lines no longer assume the other person is younger**: 晚輩 in the two seat-dispute pieces becomes 不當家的人; 年輕人 is dropped from the two veteran pieces of the advice rumors (English `the young` likewise), matching the whole-sentence closing lines. A stray comma in 「{SLIGHTED}當眾，要…」 is removed.
- Existing campaigns can simply be continued; no config changes needed.

## v1.1.0

- **Fix: English strings**: the chronicle hop count now reads `hand(s)` (no plural for a single hand), two developer-tool lines get the missing space between sentences, `apologise` is spelled the US way like the rest, and three NPC lines are reworded (Chinese unchanged).
- **New: situations happen in front of you** (`situations.playerPresence`): when the player is in a town or castle with at least two usable lords or wanderers, a roll is made on arrival and once a day while staying (`chance` 0.25); on a hit a situation plays out in front of them, followed by a cooldown (`cooldownDays` 7). It does not take from the world's daily situation budget. The player takes one of three parts: **bystander** (seat dispute, counsel, drunken words, retainers' brawl, credit dispute, hunting wager), **decider** (the clan leader seated lower, the veteran asked for counsel, the one speaking in drink, the master of the brawling retainer) or **listener** (someone talks about the ruler behind their back to the player).
  - The window pauses the game and looks like the base game's random-event window (its own data and buttons, not the native incident flow), with an illustration for each situation on the right. As decider, each response is an option whose hint gives directions only ("relation with X ↓, renown ↑, influence ↓, Valor ↑"); the chosen one records the others' grudges toward the player with the situation's own numbers (none from the player toward others), moves the player's own renown, influence (only when in a kingdom), party morale and trait XP, and the event spreads among NPCs as usual. As bystander, the second paragraph is the outcome sentence of the response rolled; responses that only produce secret events are left out of a bystander's roll.
  - Lines in the window follow each person's gender (him/her, `them` when the gender cannot be found), and translators can use gender-select tokens there too; in English the window always shows the English string table's version.
  - What the player witnesses or overhears counts as first-hand and goes into the chronicle as soon as it is shown ("you were there").
  - The log has one line for each roll, for choosing the situation (with every one left out and why), and for the player's choice with every value before and after. Developer tool "(dev) Situations in front of me": previews first and lists only the situations and parts possible here, or describes who is present when none are; another line prints the cooldown and the last roll.
- **Changed: the chronicle window is now called "What you have seen and heard"**, and the settings for its hotkey and size say so.
- Existing campaigns can simply be continued: the new cooldown record (save key `VividWorld_PlayerPresenceLastDay`) counts as no cooldown when missing; events, grudges and the chronicle keep their formats. The three new settings are added automatically, so no config changes are needed; to turn the windows off, untick "Situations happen in front of you" (`situations.playerPresence.enabled`) in the settings.

## v1.0.0

- **Full release**: a month public (since 2026-09-21) with no bug reports and the long-campaign performance test passed (next entry), so all three conditions for 1.0 are met. Same features as v0.9.11.
- **Changed: performance logging (`debug.metricsEnabled`) prints one summary line per game day**: `Perf day N:` lists, for the previous day, each measured scope's count, average and maximum, with the day and hour of the maximum, its call number and whether it was the first call after loading; the hourly pass is also split into looking up news, choosing a topic, passing it on and other. The line ends with the size of the world at the time (people in the teller ring, active rumors, secrets being watched, events on disk). Rebuilding on load has its own scope, with one line per load. The developer tool's performance report also prints the details of the all-time maximum. With the setting off, nothing extra is spent.
- Measured on a long campaign up to day 131: the hourly pass averages 1.1–1.7 ms and the daily situation scan 50–120 ms, both flat from about day 80 on (news stops spreading after 120 days, so active rumors settle at 540–600). The 47→84 ms spike measured earlier is the first hourly pass after loading a save when the game has just been started; reloading within the same game session doesn't produce it.
- Existing campaigns can simply be continued; no config changes needed.

## v0.9.11

- **Fixed: a literal `{{vividworld_news}}` no longer shows up in Calradia Remembers' prompt**: the variable used to be registered only when a campaign started or loaded, and not at all when Vivid World was off (`enabled`), AI integration was off (`ai.enabled`) or CR's memory API version didn't match; CR still read `cr_patch/post_history_instructions.txt` and leaves an unregistered variable as written, so the AI saw that line on every reply. Now, whenever CR has the interface, the variable is registered before the main menu and returns an empty string while those settings are off; a failed registration logs a warning.
- **Changed: news is given live only with Calradia Remembers 2.7.7 or later**, as v0.9.10 said. The variable interface exists in earlier versions, but whether they read the shipped prompt file was never verified; Vivid World now checks CR's `SubModule.xml` version, and older versions still get the variable registered (empty) and memories added as before. If the version can't be read it is treated as 2.7.7 or later and a warning is logged. The startup log line names the version found and which way it went.
- Existing campaigns can simply be continued; no config changes needed.

## v0.9.10

- **Changed: Calradia Remembers AI chat now goes by what the NPC remembers right now, on every reply**: news used to be added to CR as memories when a chat started, and CR kept them forever, so an NPC kept talking about things he had forgotten in Vivid World, news that was out of date, or events undone by loading an older save. Now, with Calradia Remembers 2.7.7 or later, Vivid World registers the prompt variable `{{vividworld_news}}` at startup (shipped with `ModuleData/cr_patch/post_history_instructions.txt`); CR asks for it every time it builds a prompt (every reply), and Vivid World hands over what this NPC remembers at that moment (500 characters, 300 for compact prompts) and no longer adds memories. Each call logs one `AI live news for …` line listing what was given, what was left out and why; the "AI integration" developer preview also prints the live mode and the full block under both limits. Older CR versions still get memories added as before; ImmersiveAI is unchanged.
- Existing campaigns can simply be continued; no config changes needed. Memories already added to Calradia Remembers can't be removed (CR 2.7.7 has no removal API) and the AI will still see them alongside the new block.

## v0.9.9

- **Fixed: when probing, the answer comes right after you pick what to ask**: after pressing "Ask about this" (or "Never mind"), the screen used to stay on "Go ahead. What is it?" until another click, so players often thought nothing had happened. The picking window opens after the player's line, by which time the NPC has already said the prompt line; now the conversation is moved on one step as soon as the window closes (only when the NPC has just said that prompt line).
- **Changed: memories handed to AI chat mods carry a kind**: they were all `Other`, which Calradia Remembers treats as "history between this character and the player", so rumors crowded out real history. Now anything heard is `Hearsay`; things the character lived through or saw in person are `OwnLife`; only things the player was also part of are `Other`. The kind never depends on whether the story was made up. Calradia Remembers 2.7.5 or later understands all three; older versions treat them all as `Other`, as before. The "AI integration" developer preview and the log now print `kind=` for each item.
- Existing campaigns can simply be continued; no config changes needed. Memories already handed to Calradia Remembers can't be changed; only new ones use the new kinds.

## v0.9.8

- **New: what you hear isn't always true (false rumors)**: Lords now talk about things that never happened — smearing someone they hold a grudge against, smearing a clan leader of similar standing to get ahead, making up good deeds for their own family, or spinning a real capture or death (he was captured because he chased too rashly; he chained his prisoner like a common captive; it was poison, not old age).
  Made-up talk is its own event (`WorldEvent.Fabricated`), produced by the situation engine and spread like any other news. Whoever starts it speaks as if passing on hearsay, using the same sentences as real events, so it can't be told apart by wording. World-wide daily cap `falseRumors.maxPerDay` (0.8), then a cause is picked by three weights.
- **New: five shabby things that really happen**: speaking ill of the ruler in private, refusing a comrade's plea for supplies, a rash pursuit ending in capture, mistreating a prisoner, and an old-age death that was really poison (a secret). Bystanders now follow these with two new feeling categories, "did something shabby" and "wronged in private" (16 categories in all).
- **New: listeners judge whether they believe it**: based on how close they are to the person being talked about, whether they trust whoever told them, whether it fits that person's character, and how many hands it passed through. Someone who doesn't believe it doesn't change their opinion, passes it on at 0.3x the chance, and when telling you ends with a line like "Mind you, I don't believe a word of it myself."
  How strongly they react is also scaled by personality (people who care about that kind of thing react more) and by their tie to whoever suffered (same clan x2, friend x1.5, hostile x0.5).
- **New: the accused can deny it, and those who know the truth can set it straight**: someone who learns they were smeared holds a grudge against whoever started it (x1.5, not limited by the daily cap) and may deny it publicly; someone who was there may say it didn't happen that way; made-up praise can be dismissed by the person it supposedly helped. Listeners re-judge; anyone who stops believing has the opinion change withdrawn and that day's allowance refunded. The old "misconception" setting (`consequences.misconception`) is no longer read.
- **New: probing**: pick "There's something I want to ask you." and choose an item from your chronicle. They answer from what they know: never heard of it, tell one account they know, say which version they believe and why, deny it or refuse to talk (if it's about them), stand by it or let something slip (if they started it), or explain what really happened.
  Each NPC can be probed `dialogue.probesPerHeroPerDay` (1) time per day, tracked in `probes.json`. Ask about something they told you themselves and they'll say "Isn't this something I already told you?" — it doesn't count and they don't repeat it.
- **Changed: "What you've heard" groups each matter into one block**: follow-ups and differing accounts of the same thing sit together, marked "Conflicting accounts" when they don't agree; titles say who did what ("Ergeon spoke ill of the ruler in private"), and old entries switch over too.
- **Changed: lords pass private news based on how the two of them get along**: hostile pairs don't talk, acquaintances only share big news (weight 5+), friends and family share everything (`propagation.tellTiers`), all read from the personal relation between the two rather than their clan leaders. Shameful news isn't told to the shamed person's friends and family, and lords don't spread their own friends' or family's disgrace (`propagation.shamefulNews`).
- Developer tools gained two categories ("belief and made-up talk", "made-up talk") plus several tools such as "Who would they tell the shameful news they know?".
- Existing campaigns can simply be continued: event shards, the index and `player_heard.json` only gain fields; missing fields mean a real event and not yet judged (treated as believed). Opinions already settled in an existing campaign are not recalculated. The new `probes.json` starts from zero if absent.
  No config edits needed: the whole `falseRumors` block, `propagation.tellTiers`, `propagation.shamefulNews` and `dialogue.probesPerHeroPerDay` are added automatically. The settings menu gains three entries: false rumors on/off, the daily cap on made-up talk, and how many probes per person per day.

## v0.9.7

- **Fixed: leaving with "Save and Exit" and the like no longer logs a VividWorld error**: when the save-finished notification arrived after the campaign had already ended, the post-save cleanup read the game time and threw a `NullReferenceException` (`RumorCampaignBehavior.OnSaveOver`).
  That cleanup is now skipped for that one save with a log line, and the next save does it; flushing and snapshots are unchanged. No data was ever affected.
- **New: everyone keeps their own relation with you ("Personal relations", on by default)**: before reading or writing a relation between two people, the base game swaps both for their clan leaders, so a non-leader's relation with you is really their leader's —
  raise it with a clan leader's wife and the leader's goes up too. Now any pair that includes the player is no longer swapped: the number on screen, the game's own changes, and VW's checks on who is willing to talk to you and how they feel when news involves you all land on that person.
  Relations between NPCs follow the base game. This is VW's first patch that changes the game's own behavior (Harmony, on `DefaultDiplomacyModel.GetHeroesForEffectiveRelation` and `ChangeClanLeaderAction.ApplyInternal`).
  - **The first time it is on in a campaign** (a new campaign, the first load of an existing campaign after updating, or the first time it is switched on later), every non-leader of another clan gets the relation shown on screen at that moment as their own, so nobody's number changes then; the save records a marker, once per campaign.
    If another mod already keeps player pairs per person (such as AoC's personal relation mode), nothing is copied and only the marker is recorded
  - **When a clan changes leader**, the new leader keeps their own relation with you (the base game would also add 70% of the old leader's, which would double-count what was copied)
  - **Switched off**: the game is back to the original rule, and VW reads the clan-level value for you and anyone; changes made while it is off land on the leader pair. Switched back on, non-leaders have the values they had before; nothing is copied again
  - **The notice window** now opens at the start of every new campaign (also when the rumor mode is fixed in the settings), explaining the rumor mode and this switch; it also opens on an existing campaign right after the copy, with one extra line saying past relations were passed on at the values shown then
  - At startup the log says whether both patches applied, the actual diplomacy model type, and other mods' patches on that method; the copy logs the detection result, how many people, and those whose own value was not 0; a new dev dialogue line "his relation with you" (clan-level, personal, the switch, whether this campaign was copied)
- Existing campaigns can be continued as they are: the save gains a key `VividWorld_PersonalRelationsSeeded`; old saves lack it, so the copy happens on first load. Removing VW returns the game to the original rule without breaking the save (personal values written meanwhile stay in the save, invisible to the base game).
  No config changes needed: `relation.personalWithPlayer` (`true`) is added automatically.

## v0.9.6

- **NPCs now tell news as one whole sentence, followed by a line of their own**: each rumor used to be a few fixed fragments joined with commas, word for word the same whoever told it.
  Every kind of news, and every combination of "what is still remembered", now has its own complete sentence (string keys `VividWorld_Sentence_…`; fragments are joined as before only when the current language lacks that key, and `presentation.wholeSentences=false` falls back entirely).
  People telling their own story end with how they feel about it, and temperament changes the tone (cruel, merciful, daring…); hearing it from someone involved, or having seen it, each has its own wording.
  Dying of old age and dying in childbirth are told apart; a released prisoner says how the release came about; when someone escapes from a town's dungeon, the town's lord no longer tells it as an escape from their own hands.
- **New: the speaker's own reaction**: when an NPC tells you news, the facts are followed by a line on what they think of the person involved. A friend, an enemy and a bystander tell the same event differently;
  a grudge against a friend, or a debt owed to someone disliked, shows. How they refer to the person follows rank and affection. The lines live in `vividworld_feelings.json` (14 categories × 5 moods).
  Only when an NPC tells the player; not between NPCs and not in the text pushed to AI dialogue mods.
- **Who tells you things, and what, has been reworked**:
  - **Bringing things up depends on relation plus personality** (relation + generosity ×6 + honor ×4 − calculating ×8, the same formula as answering questions); the line is 0 in Casual and 10 in Realistic. Your spouse, companions and clan members always count as close
  - **They only bring up what touches them**: themselves, their kin and clan, people they like or dislike strongly, people they hold a grudge or a debt against; people tied to you; or the sequel to something they told you before
  - **"Only the gist" is gone**: if they tell it, they tell it in full, with their reaction
  - **When you ask**: someone not close but willing tells only the big news everyone is talking about (weight 5 or more), without a reaction; someone close tells what touches them first, big news otherwise. Anyone who would bring things up also answers when asked
  - **Two different refusals**: "We hardly know each other. If it's news you want, ask someone else." from people who are not close, and "I have nothing to say to you." from people who dislike you
  - A person only tells their own secret to someone very close (willingness 30); leaked secrets are not told by honest people, and cautious people tell them only to those close to them
  - The "Full-story relation gate" setting is removed from MCM; the "what if I talk to everyone right now" preview also prints why each piece would be told and how many people give each reply
- **News weight goes from 5 levels to 10**: weight = how serious the event is + the standing of the person (ruler +4 / clan leader +2 / ordinary clan member 0 / minor faction −2); marriages and births go by the clan's standing (royal +4 / clan tier 5 or above +2).
  How far it spreads and how long it is remembered still come in five bands (two levels each). An ordinary person being captured is no longer as big as a death in battle; the fates of rulers and clan leaders and royal marriages travel furthest.
- **The chronicle keeps each person's own words**: when two people told you about the same event, the chronicle shows two blocks, each "who: "the whole sentence they said"", with a small "at a remove of N" line beneath.
  The hop count now only records how many times the news was passed on. Captures, releases and escapes name the person in the title.
- **For translators**: pronouns now come from the string table (`VividWorld_Pronoun_<He|Him|His|Himself>_<M|F|N>`, `VividWorld_Self_Reflexive`) instead of English words fixed in code;
  a new marker `{NAME:masculine|feminine}` picks a word by gender (NAME is an event role, `ME`, `YOU` or `FOCUS`), and the ten fixed lines of the dialogue menu can use it too. English and Traditional Chinese sentences are unaffected. See `CONTRIBUTING.md`.
- Developer dialogue lines are gathered behind one entry with three categories (this person / the world / change things); new tools: "make them tell me a piece of news with a reaction" and "what reaction this person would give to each piece of news".
- Existing campaigns continue as-is: the event format only gains a field marking the weight scale, old events are read as the former 1–5 (spreading and memory unchanged), and the new weights apply only to events after the update;
  `player_heard.json` gains a per-person source list, old entries are read as a single source and show the facts alone when no spoken line was saved; hop counts of old entries are not changed. A few combinations in news from before the update have no whole sentence and are still told with joined fragments.
  No config edits needed: `presentation.wholeSentences`, `presentation.feelings.*`, `dialogue.casualVolunteerLine` (0), `dialogue.realisticVolunteerLine` (10),
  `dialogue.secretLine` (30), `dialogue.bigNewsLine` (5), `events.weightBonusByProminence`, `events.weightBonusByClanStanding` and `events.highClanMinTier` (5) are added automatically.
  **These old keys stay in the file but are no longer read; if you changed them, set the new keys instead**: `dialogue.npcVolunteerRelationGate`, `casualChatRelationGate`, `realisticChatRelationGate`, `gistExtraHops`,
  the six `dialogue.score*` keys and the four `events.*DramaByProminence` groups. The log prints one line for each at startup.

- **Fix: after loading a save, the grudge ledger could no longer tell rumor-born grudges from ones made on the spot in a situation**: the ledger rebuild dropped the source, so every rumor-born grudge came back as a situation grudge. No feature reads that field yet, so players see no difference; this is groundwork for features that will. Existing campaigns continue as-is (the data was always saved correctly, only the rebuild dropped it); no config changes needed.

## v0.9.5

- **New: heroes captured by bandits or deserters now become news**: previously, when a hero was taken by a party with no leader, VW could not tell who had taken them and skipped it
  (7 skips over 43 days in one campaign, mostly lords taken by deserters), and when the hero was later freed, the news was linked to an older capture by a lord. It is now recorded as
  "fell into the hands of a band of Deserters" and the like, naming which kind of bandit (Looters, Deserters, Mountain Bandits…; "outlaws" when it can't tell),
  the captive's family hears of it right away as "I heard that" (up to 8 of them), and it spreads further than a capture by a lord.
  When someone routs the bandits and frees the captive, the news names the rescuer ("Ergeon routed a band of Deserters and freed Yorig"); escaping has its own line; people who heard of the capture stop saying the hero is still held.
  **If you rout the bandits and free the captive yourself, the news says you rescued them.** The people involved speak of it in the first person.
- **Every rumor now puts the place first**: "Near Dunglanys, Ergeon routed a band of Deserters and freed Yorig." English capitalizes the first word when there is no lead-in (Near …). Set `presentation.placeFirst` to `false` to get the old order back.
- Developer dialogue gains "(dev) Have the closest bandits capture this person" (the conversation closes first; the capture happens once you leave the encounter); `(dev) World status` gains a line counting bandit captures / rescues / escapes.
- Existing campaigns can simply be continued: the new news starts after the update. Someone already held by bandits at the moment of updating will, when freed, still be linked to their older capture by a lord (one-off, not handled).
  No config edits needed: `presentation.placeFirst` (`true`), `events.banditCaptureDramaByProminence` and `events.banditReleaseDramaByProminence` are added automatically.

## v0.9.4

- **Each NPC can now share one piece of news with you per day, instead of only one NPC in the whole world volunteering per day**: previously at most 1 NPC per day
  volunteered a rumor world-wide, and the same NPC then stayed quiet for 3 days, so touring a whole town got you one speaker at most; asking, on the other hand, had no limit,
  so a willing NPC could be drained in one sitting. It is now **per person**: on any day, every NPC may bring up one rumor, but the same person shares only one per day
  (what they volunteer and what you ask for count together). Ask again that day and they say "I've said my piece for today. Come find me another time."; the next day they talk again.
  Memories pushed to AI dialogue mods do not count. MCM gains "News per person per day" (default 1, 0 = no limit, max 10).
  Existing campaigns can simply be continued: the count is kept in a new `shares.json` in the campaign folder (rolls back with snapshots) and starts after the update; the old `volunteers.json` is left in place and no longer used.
  No config changes needed: `dialogue.sharesPerHeroPerDay` (`1`) is added automatically; the old `dialogue.maxVolunteersPerDay` and `dialogue.volunteerCooldownDays` stay in the file but are no longer read, so you may delete them or leave them.

## v0.9.3

- **Fix: a hero taken prisoner kept passing news on (and hearing it) for the rest of that day**: whether a hero is a prisoner was cached together with trait values
  and re-read only once a day, so a hero captured mid-day kept talking until the next day (in one campaign, 4 of 221 capture events were spread by the prisoner within seconds of being captured).
  Prisoner / alive / lord-or-wanderer status is now read live every time; trait values are still cached daily.
  Two wording bugs fixed too: English described a release as "someone set … free", and a Traditional Chinese fragment named no one because a variable was missing.
  Existing campaigns can simply be continued. No config changes needed.
- **New: AI dialogue mods now know the rumors an NPC heard in Vivid World**:
  with Calradia Remembers installed, when you open an AI chat with a lord or wanderer, VW hands CR the rumors he still remembers and would tell
  (in the version he heard, including who told him), and CR knows them from the first line. These become permanent CR memories, so at most 2 new ones per chat,
  and the same version is never pushed twice (recorded in `ai_pushed.json` in the campaign folder, which rolls back with snapshots).
  Forgotten, outdated, unleaked-secret and won't-tell-own rumors are never handed over.
  ImmersiveAI is wired up as well (each chat re-sends up to 8 in their current version, never written into ImmersiveAI's permanent memory),
  **but it needs an ImmersiveAI version that supports this interface, which has not been released yet**; with the current ImmersiveAI, VW logs one "disabled" line at startup and skips it.
  Nothing changes if neither mod is installed. MCM gains an "AI Integration" group with "Language for AI mods" (English / Game Language, default English).
  A new developer dialogue line "(dev) AI integration: what would be handed over for this NPC" lists every candidate and every exclusion reason without pushing anything.
  Existing campaigns can simply be continued: the push record starts accumulating after the update. No config changes needed: `ai.enabled` (`true`),
  `ai.pushLanguage` (`english`), `ai.persistentMaxNewPerChat` (2) and `ai.chatOnlyMaxPerChat` (8) are added automatically.
- **NPCs now say where a rumor came from**: "I saw it myself:" (an eyewitness), "X told me that", "I heard it said that"; if you had heard someone was captured and now hear he was released,
  it becomes a correction ("Later, X told me that"). 12 fragments that described a current state were rewritten to describe what happened,
  so they do not go stale in an AI mod's permanent memory. Existing campaigns can simply be continued. No config changes needed.
- **Participants tell their own stories in their own voice**: when the speaker is one of the people in the event, it is no longer "I saw it myself: X …";
  he speaks as "I" using a spoken line for that role (173 lines, English and Traditional Chinese, e.g. "科爾 got away from me, near 鄧葛蘭尼, …"),
  and a retelling opens with "You may have heard about this already—". Some people keep embarrassing things to themselves:
  the credit-grabber, the drunken boaster and the one who spoke harshly never tell; a mocked student tells only with some valor.
  English he/him/his follow the hero's gender in the game, falling back to they only when it cannot be found.
  Existing campaigns can simply be continued and existing events use the new lines immediately. No config changes needed.
- **Two situation events retired: "private complaint" and "backbiting"**: a secret told by its own owner came out framed wrong ("I told someone a secret");
  they return when the secret system is reworked. No new ones are created; existing ones still load and stay in the chronicle,
  but are no longer spread, told or handed to AI mods. Existing campaigns can simply be continued. No config changes needed.

## v0.9.2

- **New "rumor mode" (Auto / Casual / Realistic), so new characters hear rumors too**: lords used to bring up rumors only at relation 30 or higher,
  so a new character (relations all 0-9) heard almost nothing. There are now two tiers: at relation 30 a lord still tells the full story;
  below that but at the "chat gate" he tells the gist (the news has passed through two more hands, with fewer details).
  The chat gate is 0 in Casual (anyone who does not dislike you may talk) and 10 in Realistic.
  In Casual, players with a commoner-status mod such as Not-a-Noble or Lowborn can also ask at clan tier 0; Realistic still requires tier 1.
  The default is Auto: Realistic when a commoner-status mod is detected, Casual otherwise. The first time (or when the result changes) a window explains it;
  after that, each load shows a light-green line at the bottom left. The setting is at the top of the MCM "Dialogue" group and takes effect immediately, no reload needed.
  Measured on the same new character: lords who would volunteer went from 0 to 440 in Casual (all gist). The one-volunteer-per-day limit,
  the 3-day per-person cooldown and the willingness check when you ask are unchanged.
  Existing campaigns can simply be continued. No config changes needed: `dialogue.volunteerMode` (`auto`), `casualChatRelationGate` (0),
  `realisticChatRelationGate` (10) and `gistExtraHops` (2) are added automatically, **so after updating you are in Auto mode**; to keep the old behaviour
  (only relation 30+ talks), choose Realistic in MCM and set `realisticChatRelationGate` to 30.
  The old MCM entry "commoner-status compatibility" is gone (`commonerCompatMode` stays in `config.json` and still works);
  "Volunteer relation gate" is renamed "Full-story relation gate".
- **Passing on someone else's news no longer starts with "I was there, in fact"**: that opening now appears only when a participant or witness retells it.
  Existing campaigns can simply be continued, no config changes needed.
- **When you ask "Any news on the road?" and they have nothing for you, the reply now says why**: unwilling to tell you, heard nothing, forgot,
  the news is outdated, or you have already heard it all, each with its own line (it used to be the same line whatever the reason).
  Who is willing to talk is unchanged. Existing campaigns can simply be continued, no config changes needed.
- **Developer tool: measuring why the player hears so little**: every conversation records which gate stopped a volunteer or an ask
  (relation, cooldown, daily limit, nothing to tell, bypassed by another mod, ...), summed per game day into `listen_tally.json` in the campaign folder
  with a daily summary line; on load and once a day it also previews "what would happen if you talked to everyone in the network now".
  Two new developer dialogue lines: "(dev) Why do I hear so little?" and "(dev) What happens if I talk to everyone right now?".
  Existing campaigns can simply be continued: the tally starts from the update. `debug.listenTally` is added automatically (default `true`; `false` turns it off).
- **Log messages no longer carry internal references**: a dozen or so lines in `log.txt` ended with tags such as `ledger X-17` that point to private working notes; they now keep only the explanation (the localization self-check line now says OK or FAILED and why). Existing campaigns can simply be continued, no config changes needed.

## v0.9.1

- **The chronicle now lists only news you have heard yourself, and every entry is visible**: the `Ctrl+L` chronicle reads
  a dedicated record of what you have heard, newest first; news stays there even after everyone else in the world has forgotten it.
  Existing campaigns can simply be continued: on the first load, news you already knew is filled in from existing data. No config changes needed.
- **Names in the chronicle are now clickable**: people and places are coloured like in the dialogue box, and clicking one opens
  its encyclopedia page; the chronicle comes back by itself when you close the encyclopedia. Existing campaigns can simply be continued,
  no config changes needed (with `presentation.encyclopediaLinksEnabled` set to `false`, names stay plain text and are not clickable).
- **Saving now clears out old news that nobody remembers any more**: once no one in the world remembers a piece of news,
  it is deleted from the mod's data when you save, so the data no longer keeps growing over a long campaign (in one test campaign, 336 of 831 were removed).
  News you have heard is not affected and stays in the chronicle; secrets, grudges and news still spreading are kept.
  Loading an earlier save brings back the news as it was then.
  Existing campaigns can simply be continued: the index is rebuilt once on the first load. No config changes needed.
- **The mod's memory use no longer only grows over a long campaign**: old news data that has not been used for a while is released
  from memory and read back from disk when needed. Also, after loading a save, NPCs taking turns to pass on news no longer waste turns
  on people who have forgotten everything (in one test campaign, 666 people went down to 608 after loading).
  Existing campaigns can simply be continued, no config changes needed (the new setting `persistence.shardCacheIdleFlushes` is added
  automatically, default 8; set it to 0 to never release).

## v0.9.0

First public beta.
