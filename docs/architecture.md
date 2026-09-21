# How Vivid World is put together

This is a map of the codebase for someone who wants to change it. It describes what the
pieces are and why they are split the way they are, not every class.

For build instructions and the rules a pull request has to respect, see
[`CONTRIBUTING.md`](../CONTRIBUTING.md).

---

## The one idea everything else follows from

**An event and knowledge of that event are two different things.**

An event is a fact that settled in the world: these two lords quarrelled, at this place, on this
day. It is recorded once, in full, and never changes.

A rumour is one person's copy of that event — and copies are lossy. When a story is passed on,
the teller does not hand over the whole thing. Details drop out according to how fragile they
are, so the version that reaches the sixth person is genuinely thinner than the version the
witness held.

Nothing in the mod asks "is this important enough to tell the player?" The player is just another
node in the same network, hearing whatever reaches them through the same rules as everyone else.
If word never gets to anyone they talk to, they never find out. That is the intended behaviour,
not a gap to be patched with a notification.

---

## Two assemblies, and the wall between them

```
VividWorld.Core    netstandard2.0   the rules. No game types, ever.
VividWorld.Module  net472           the game. Harmony patches, dialogue, UI, campaign events.
```

`Core` refers to people as `string` ids and nothing more. It has no idea what a `Hero` is. That
is what lets the whole rumour system — propagation, forgetting, grudges, text composition — run
in a unit test in two seconds, with no game process anywhere.

`Module` is the only place that knows about Bannerlord. It reads the campaign, hands `Core`
plain values, and turns `Core`'s answers back into dialogue lines, log entries and screens.

If a change seems to need a `Hero` inside `Core`, the logic belongs in `Module` — or `Core` only
ever wanted one fact out of that hero, which can be passed as a value.

### Inside `Core`

| Folder | What lives there |
|---|---|
| `Events` | Event templates, facts, roles, the event record itself |
| `Rumors` | One person's copy of an event, the propagation engine, and which facts survive being retold |
| `Channels` | The six routes a story can travel along (below) |
| `Memory` | Forgetting, interest, re-hearing, and news going stale |
| `Situations` | Scene templates, conditions and branches — how events come about |
| `Grudges` | The grudge ledger, and how grudges fade |
| `Catalog` / `Ingest` | Loading and validating the JSON content |
| `Persistence` | Reading and writing campaign data, and the save snapshots |
| `Presentation` | Composing the text a teller actually says |
| `Config` | Every tunable number, with defaults and clamping |
| `Util` | The deterministic RNG and seed construction |

### Inside `Module`

| Folder | What lives there |
|---|---|
| `Campaign` | Campaign-event hooks, the propagation scheduler, content loading |
| `Dialogue` | Dialogue lines — both asking for news and being told it unprompted |
| `Presentation` / `UI` | The chronicle window and the snapshot manager |
| `Mcm` | The in-game settings page (Mod Configuration Menu) |
| `Debug` | Developer console commands and dumps, inert unless switched on |
| `Api` | A small surface other mods can read |

---

## How word travels

Six channels, in two groups that behave differently on purpose:

**In person** — `SameParty`, `SameArmy`, `SameSettlement`. You do not choose who you run into.
Liking someone does not make you share a camp with them, so relationship weighting here is
centred on 1.0 and barely moves.

**At a distance** — `SameClan`, `KinAbroad` (a daughter married into another house, a mother in
another clan), `Kingdom`. These are deliberate: you choose who to write to. Here the weighting
climbs from about 0.10 upward, because you write to people you care about.

Two curves of different shapes, because one linear formula cannot do both.

Each tick, a limited number of people get a turn to talk, and each picks **one** story from what
they remember — weighted by how big it was, how willing they are to tell it, and how fresh it is.
Seats are allotted per group (four in person, two at a distance) and one group can borrow the
other's unused seats. This is not "sort everything and take the top six": that would let the
in-person channels, which are far more numerous, crowd the distant ones out of existence.

People also forget. Interest decides how long a story stays with someone, and being reminded of
it by someone else resets that.

---

## Saves, and why nothing is written into them

The mod stores its campaign data in **its own files**, under the player's
`Mount and Blade II Bannerlord\Configs\VividWorld\` folder — not inside the `.sav`.

This is the entire reason uninstalling the mod cannot corrupt a save. Writing through the game's
own save system would put mod-defined types in the save file, and removing the mod would then
leave a save the game cannot fully load.

Reloading is handled with snapshots instead. Every save mints a token; saving takes a snapshot of
the campaign data folder alongside it; loading finds the matching snapshot and restores it. So
reloading an older save rolls the rumours back to where they were, instead of leaving the world
knowing things that — from that save's point of view — have not happened yet.

Snapshots are hard-linked where the filesystem allows and copied where it does not, and a restore
always copies, never links. Players manage them from inside the game (`F9` by default) rather than
by finding a folder.

---

## What the player actually sees

- **Ask someone for news.** Whether they will talk depends on their opinion of you and their
  character — generosity and honour open people up, calculation closes them. Getting nothing out
  of a stranger is the design working, not a bug to tune away.
- **Being told something unprompted**, by someone close enough to volunteer it.
- **The chronicle window** (`Ctrl+L` by default) — what you have heard, and from whom.
- **The snapshot manager** (`F9` by default).
- **A settings page**, if Mod Configuration Menu is installed. It and `config.json` are the same
  settings; `config.json` is the source of truth.

All of it is localised. Labels the player uses to *operate* something get translated; diagnostic
output the player would paste into a bug report stays in English on purpose, so it is readable by
someone who does not speak their language.
