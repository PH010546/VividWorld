# Changelog

*[中文](CHANGELOG.md)*

Every release states three things:

- **What changed**
- **Whether existing campaigns can simply be continued**
- **Whether you need to edit your config file yourself**: updates only add new settings; values you already have are never changed

## Unreleased

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
