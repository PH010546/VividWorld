# Vivid World — A Life of Their Own

*[繁體中文](README.md)*

A singleplayer mod for Mount & Blade II: Bannerlord. Things happen between the people of Calradia; word gets around; each listener decides whether to believe it; people remember and forget. The player is one more node in that network.

This README is for people who want to read the code, change it, or understand how it actually works. If you only want to play, the [Nexus page](https://www.nexusmods.com/mountandblade2bannerlord/mods/13395) is the friendlier read.

---

## The core idea: an event and knowing about it are two different things

An **event** is something that settled in the world: who, where, which day, what happened. It is recorded once, in full, and never changes.

A **rumor** is one person's copy of that event, and copies are lossy. Each time it is retold, every fact in the event (the day, the place, who else was there…) may drop out according to its own fragility, so the version that reaches the sixth person really holds less than the witness's.

Nothing in the mod asks "is this important enough to tell the player?" The player is just another node, hearing whatever reaches them by the same rules as everyone else. If nobody tells them, they never find out.

---

## Architecture

```
VividWorld.Core    netstandard2.0   the rules. No game types, ever.
VividWorld.Module  net472           the game side: campaign events, dialogue, UI, Harmony patches.
```

`Core` knows people only as string ids; it has no idea what a `Hero` is. Propagation, forgetting, belief, grudges and sentence composition all live in `Core`, so the whole rumor system runs in unit tests with no game process (two thousand-odd tests at present).
`Module` is the only place that touches Bannerlord: it reads the campaign, hands `Core` plain values, and turns the answers back into dialogue, log lines and screens.

Three rules that do not bend:

1. **Core never references the game** — a check enforces it
2. **Core does not roll its own dice** — every random choice goes through a deterministic RNG seeded from the campaign, the day and what is being decided. Reloading must give back the same world
3. **No tunable numbers in code** — they live in `config.json` with defaults and clamping; an existing config only gains new keys, never loses the player's values

---

## The life of a piece of news

1. **It happens.** Three sources:
   - The game's own big events (death, capture, release, marriage, childbirth), told apart by cause and circumstance
   - **Situations**: scenes described in data (conditions → branches), whose outcome is weighted by the participants' traits
   - **Made-up talk**: people with a grudge, a rival for position, or family to flatter produce events that never happened. Type and sentences are identical to the real thing; only one flag in the data differs
2. **It travels.** Six channels in two groups. In person (same party, army, settlement) is not chosen, so relations barely matter; at a distance (same clan, kin married away, same kingdom) is a choice of whom to write to, so relations matter a lot. A round runs on a game-time schedule; a limited number of people take a turn, each picking one story. Whether something shameful gets told depends on how both the teller and the listener stand with the person shamed
3. **It is judged.** A listener first decides whether to believe it, based on how they stand with the person talked about and with the teller, and whether it fits that person's character. The person talked about may deny it, someone who knows the truth may set it straight, and those who heard it judge again
4. **It settles.** A believer's opinion of the people involved moves by the event's weight (in full for a witness, less with each retelling). Friction also goes into the mod's own grudge ledger; only when it runs deep and involves a clan leader or their close kin does it escalate into a feud between clans
5. **It is forgotten.** How long someone remembers = their tie to the people involved × the event's weight × how often they have been reminded. Once everyone who knew has forgotten, the event is purged at the next save (except those carrying grudges, and secrets that have not leaked)
6. **It reaches the player.** The player asks, an NPC volunteers, or the player probes about something already heard. Each line is a fact sentence plus the speaker's reaction according to their character and where they stand; every sentence lives in the string tables

Every relation the mod reads is the two people's own value, not the clan-leader value the game substitutes.

---

## What it changes in the game itself

- **Two Harmony patches**, both only for the pair "the player and someone": that pair's relation is no longer swapped for the two clan leaders' relation, and a change of clan leader does not add the old leader's relation on top of the new one. Both can be switched off in the settings to restore the original rule
- Relations and clan feuds are written through the game's own API
- Beyond that it does not change the game's AI decisions and overwrites no native files

---

## Persistence: nothing goes into the save

Campaign data lives in the mod's own files (`Documents\Mount and Blade II Bannerlord\Configs\VividWorld\`), not in the `.sav`. That is why removing the mod cannot break a save.

Older saves are handled with **snapshots**: every save mints a token and snapshots the campaign data; loading finds the matching snapshot and restores it. Reload an old save and the rumors roll back with it, so the world never knows things that, from that save's point of view, have not happened yet. Players manage snapshots in game with `F9`.

Existing campaigns always carry on: the persisted format is only ever added to, and unknown fields are kept as they are.

---

## Content is data, not code

Four JSON files under `module/ModuleData/`: event templates, situations, the events situations emit, and reaction categories. A new scene or kind of news mostly needs no C#, only data and strings. The false-rumor machinery applies itself to new content from what the data file says: who looks bad, and who the blame can be shifted onto.

Details in [`CONTRIBUTING.md`](CONTRIBUTING.md).

---

## Interfaces for other mods

- **`VividWorld.Api.VividWorldEventApi`**: a static facade using only primitive types, readable by reflection with no assembly reference. An event-occurred notification, which events a hero knows, and an event's JSON. Unleaked secrets are never returned
- **AI chat mods**: currently integrated with Calradia Remembers only. It binds to that mod's types by reflection and checks its interface version; on a mismatch it disables itself and logs why. It hands over the rumors an NPC still remembers, in the version they heard

---

## Observability

Every new behaviour must show **why** in the log: the inputs, each multiplier, and **what was excluded and why**. The log is `Configs\VividWorld\log.txt`.
Set `debug.logTellerTurns` in `config.json` and every retelling is logged. Developer tools can also show each system's current state in game.

---

## Building

You need the .NET SDK 8.0 or newer, a local install of Bannerlord **v1.4.8**, and MCM in that same game folder.

```
dotnet build VividWorld.sln -c Release
dotnet test tests/VividWorld.Core.Tests
```

The build is expected to produce zero warnings. Pointing the build at your game folder and the rules for pull requests are in [`CONTRIBUTING.md`](CONTRIBUTING.md); what each folder holds is in [`docs/architecture.md`](docs/architecture.md).

---

## Installing (players)

1. Put the `VividWorld` folder into the game's `Modules\`
2. **Install Mod Configuration Menu (MCM) as well**, loaded before this mod
3. Tick **Vivid World** in the launcher, after Native, SandBoxCore, Sandbox and StoryMode
4. Harmony is bundled

Built against game **v1.4.8**. Settings are on the game's settings screen (MCM) or in `config.json`; both are the same file.

---

## Compatibility

- **Mods where commoners may not address nobles** (NaN, Lowborn and the like): when detected, the Auto rumor mode switches to Realistic, and at clan tier 0 you get no news
- **Calradia Remembers**: see "Interfaces for other mods" above
- **Affairs of Calradia**: its personal relations mode does the same thing; when detected, the work is not repeated
- **Mods that take over a lord's opening line**: an extra dialogue line, "You looked like you had something to say?", brings the subject back

---

## Languages

English, Traditional Chinese and [Russian](https://www.nexusmods.com/mountandblade2bannerlord/mods/13594) are included. More languages are welcome.

- Thanks to Lingaraja for the Russian localization!

The rules for adding a language (every language folder must carry exactly the English key set; how pronouns and gendered words are written) are in [`CONTRIBUTING.md`](CONTRIBUTING.md).

---

## Licence

MIT — see [`LICENSE`](LICENSE). Harmony is bundled under its own MIT licence.
