# Changelog

*[中文](CHANGELOG.md)*

Every release states three things:

- **What changed**
- **Whether existing campaigns can simply be continued**
- **Whether you need to edit your config file yourself**: updates only add new settings; values you already have are never changed

## Unreleased

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
