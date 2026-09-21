# Vivid World

*A Life of Their Own*

A singleplayer mod for *Mount & Blade II: Bannerlord*. **It brings the people of Calradia to life.**
Things happen between them, word gets around and changes on the way. They remember, and they forget.
And you can be part of it.

*[繁體中文](README.md)*

> **This is a beta (v0.9.0).**
> The underlying machinery is complete and has seen live play; the content is still being expanded.
> See the Roadmap at the end of this document for what remains.

---

## Overview

**News generates itself.** The mod hooks five classes of the game's own events — death, captivity,
marriage, childbirth, release — and subdivides them into nine kinds of news: death is distinguished by
cause into murder, execution, death in battle and natural death; release is divided into being freed
and escaping.
Six authored scenes also occur of their own accord: a quarrel over seating at a feast,
counsel sought from a veteran, loose talk over the cups, a dispute over credit for a victory, a brawl
in the market, and a wager struck on a hunt.

**News travels by word of mouth, and frays in transit.** Every so often, lords each choose something
from memory and relate it to whoever is at hand. Every retelling loses
further detail — the day, the place, who else was present, one item at a time. By the fifth telling,
what reaches you is often no more than "someone did something, somewhere."

**Great news travels far; small news barely moves.** Each kind carries its own weight: word of a birth
stops after two tellings, whereas the murder of a ruler may travel six and cross half the map.

**People forget.** Those with no connection to the parties involved retain it only for a few days;
what is repeatedly raised is held longer; and once everyone who knew has forgotten, the account falls
silent.

**Hearing about someone changes how they are regarded.** Whoever receives the news shifts their
opinion of the parties by whatever that event is worth — in full for an eyewitness, reduced for one
who merely heard of it, and reduced again with each telling it passed through. **Your own conduct is
talked about on the same terms.**

**Conflict leaves a score behind.** Friction between lords is entered in the mod's own grudge ledger —
who holds what against whom, and over which incident. What that ledger governs is **when a private
quarrel escalates into a feud between two families**: let the score run deep enough, with a clan head
or their close kin involved, and the matter ceases to be between two people. A score fades with time,
but only so far — it is never wiped clean.

---

## Installation

**Game version:** built against **v1.4.8**. Other versions are untested.

1. Extract the archive and place the entire `VividWorld` folder under the game's `Modules\` directory
   (e.g. `...\Mount & Blade II Bannerlord\Modules\VividWorld\`).
2. **Install Mod Configuration Menu (MCM) as well**, loaded before this mod.
3. Enable **Vivid World** in the launcher's module list, ordered after Native, SandBoxCore, Sandbox
   and StoryMode.
4. Harmony need not be installed separately.

**Removal:** delete the module folder. Backing up beforehand is advisable.

---

## Encountering it in play

**Asking.** In conversation with any lord, the menu gains the line *"Any news on the road?"* He relates
what he knows, or answers *"Nothing worth repeating."* if he has nothing. **Getting nothing out of a
stranger is normal** — whether he will speak depends on his opinion of you together with his character
(generosity and honour incline him to share; a calculating nature keeps him silent). Below the
threshold, nothing is forthcoming. This is by design.

**Being told.** A lord will sometimes open by relating news he has heard, unprompted. Should another
installed mod take over the greeting entirely, the menu gains the fallback line *"You looked like you
were about to say something."*

**Reviewing what you have heard.** Press **Ctrl+L** for *What you have heard*, which lists every piece
of news that has reached you so far, marked with how many tellings removed it is and who told you.
Names of people and places in the text are clickable and lead to the game's encyclopedia.

---

## Save safety

The mod's data is **not written into the game's save files**. It is stored separately, under
`Documents\Mount and Blade II Bannerlord\Configs\VividWorld\`.

Two consequences follow:

- **Existing saves still load after the mod is removed.** They contain nothing belonging to this mod.
- **Moving a save to another machine leaves the rumour data behind.** Copy the folder above as well if
  you want it to accompany the save.

---

## Configuration

The configuration file is located at:

```
Documents\Mount and Blade II Bannerlord\Configs\VividWorld\config.json
```

It is created the first time the game is launched. The `_README.txt` beside it describes what each
file in that folder is for.

The settings most likely to be adjusted:

| Key | Default | Effect |
|---|---|---|
| `enabled` | `true` | Master switch for the mod |
| `presentation.chronicleHotkey` | `"Ctrl+L"` | Opens *What you have heard* |
| `presentation.snapshotManagerHotkey` | `"F9"` | Opens the snapshot list |
| `persistence.maxSnapshots` | `0` | Snapshot retention limit; `0` means no limit |
| `debug.logTellerTurns` | `false` | Writes every telling to `log.txt`. Useful when reporting a problem |

Hotkeys accept a modifier prefix — `"Ctrl+L"`, `"Alt+K"`, `"Shift+F9"` — or a bare key name such as
`"L"`.

**Updating the mod does not alter your existing settings.** Only keys new to the update are added;
values you have adjusted, and even keys the mod no longer recognises, are preserved exactly as they
are.

**There is also a Vivid World page in the game's settings screen** (provided by Mod Configuration
Menu): 24 entries in eight groups, changed in game and written straight back to the `config.json`
above. Settings not on the page are left untouched.

---

## Languages

English and **Traditional Chinese** only at present. Further translations are welcome.

---

## Compatibility

**Mods of the "commoners do not address nobles" kind** (NaN, Lowborn and similar) are detected, upon
which the mod enters a compatibility mode that restricts *asking* for news to clan tier 1 and above.
**At clan tier 0 you will get nothing out of anyone** — this is deliberate deference to those mods.

**Mods that take over the greeting:** some mods claim a lord's opening line in its entirety, leaving
this mod no opening. In that case the dialogue menu gains the line *"You looked like you were about to
say something."* so that the subject may still be raised.

This mod does not modify the game's own AI decision-making, nor does it overwrite any native file.

---

## Reporting problems

The log is located at:

```
Documents\Mount and Blade II Bannerlord\Configs\VividWorld\log.txt
```

Please attach it when reporting. If the problem concerns who related what to whom, first set
`debug.logTellerTurns` to `true` in `config.json`, restart, and reproduce the situation; the log will
be considerably more detailed.

---

## Roadmap

There is a good deal more to Calradia's dealings than the mod covers today. These are the directions
being worked on:

| Item | Where it is headed |
|---|---|
| **More authored scenes** | A steadily growing repertoire of occasions the lords play out among themselves — the feast, the campaign, the marketplace, and the business between families |
| **Embellishment** | News will not merely lose detail in transit; it will be exaggerated and coloured, until what arrives is a good deal more than what happened |
| **Setting the record straight** | When the version that reached you is at odds with the facts, you will be able to take it up with the party concerned and bring the misunderstanding into the open |
| **Quarrels with a reply** | A dispute will no longer be settled in a single stroke; a contest such as a duel will let each party answer the other |
| **Consequences that reach further** | Outcomes will touch not only standing but money, renown, influence — and, in time, life and limb |
| **A voice of your own** | Spread news yourself, and set one lord against another; make rumour an instrument in your hands |
| **More sources of news** | Defections, rebellions and the fall of besieged settlements will travel among the lords as well |
| **Hooking into AI dialogue mods** | What they know, and what they hold against one another, will be made available to mods that drive conversation with AI, so they can speak about it in their own words |

---

## Licence

MIT. See [`LICENSE`](LICENSE) for the full text.

This mod also ships [Harmony](https://github.com/pardeike/Harmony) (MIT as well), as the game does not
provide it. Its licence text is included in the release package as `0Harmony.LICENSE.txt`.
