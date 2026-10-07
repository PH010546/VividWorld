# Vivid World

*A Life of Their Own*

A singleplayer mod for *Mount & Blade II: Bannerlord*. **It brings the people of Calradia to life.**
Things happen between them, word gets around and changes on the way. They remember, and they forget.
And you can be part of it.

*[繁體中文](README.md)*

> **This mod is currently in beta.**
> The underlying machinery is complete and has seen live play; the content is still being expanded.
> See the Roadmap at the end of this document for what remains.

---

## Overview

**News generates itself.** The mod hooks the game's own major events — death, captivity, release,
marriage, childbirth — and tells them apart by cause and circumstance into different kinds of news.
The lords also play out scenes of their own accord, such as a quarrel over seating at a feast or loose talk over the cups.

**News travels by word of mouth, and frays in transit.** Every so often, lords each choose something
from memory and relate it to whoever is at hand. They say whether they saw it themselves or who told them;
those involved speak of their own affairs in their own voice. Every retelling loses
further detail — the day, the place, who else was present, one item at a time. The further it travels,
the more likely what reaches you is no more than "someone did something, somewhere."

**Great news travels far; small news barely moves.** Each kind carries its own weight: word of a birth
does not get far, whereas the murder of a ruler may cross half the map.

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

**Not everything said is true.** Lords will say things behind others' backs that never happened — smearing someone they bear a grudge against,
running down a rival for position, making up good deeds for their own family. Whoever hears it decides whether to believe it:
someone close to the person talked about, someone who distrusts the teller, or someone who finds it out of character will not change their view because of it.
The person talked about may deny it publicly, and someone who was there and knows the truth may set it straight; those who heard it will think again.

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

**Asking.** In conversation with any lord or wanderer, the menu gains the line *"Any news on the road?"* Someone who
barely knows you relates only the big news everyone is talking about; someone close to you starts with what touches
him. When he has nothing for you, he tells you why. **Getting nothing out of a
stranger is normal** — whether he will speak depends on his opinion of you together with his character. Below the
threshold, nothing is forthcoming. This is by design.

**Being told.** A lord or wanderer will sometimes open by relating news, unprompted. What he brings up is his own
business and that of the people he cares about; whether he does depends, again, on his opinion of you together with
his character. Having told it, he adds a line of his own — a friend, an enemy and a bystander tell the same event
differently. Each person will only
share so much with you in a day; once he has said enough, he will tell you to come back another time. Should another
installed mod take over the greeting entirely, the menu gains the fallback line *"You looked like you
were about to say something."*

**Probing.** In conversation, choose *"There's something I want to ask you."* and pick an item from *What you have heard* to ask about. They answer from what they know — never heard of it, the part they know, or, if they have heard both versions, which one they believe; ask about their own affairs and they may deny it. Each person will only let you ask so much in a day.

**Rumor mode.** The Vivid World page of the game's settings offers
Auto, Casual and Realistic; a change takes effect immediately. Casual: anyone whose character does not shy from talk
may bring up his own affairs even to a new acquaintance, and even a commoner may ask. Realistic: he must be talkative
by nature, or think somewhat well of you, before he brings them up. Auto (the default): Realistic when a mod such as NaN or Lowborn is detected,
Casual otherwise.

**Personal relations.** Everyone keeps their own opinion of you: helping or crossing someone only affects that person, not their whole clan (in the base game, a clan shares its leader's relation with you). Who is willing to talk to you, and what they think of you when news involves you, also follow that person's own opinion.
The first time you load an existing campaign, each person gets the value shown on screen at that moment, so the numbers you see do not change. It can be turned off on the Vivid World page of the game's settings to go back to the original rule.

**Reviewing what you have heard.** Press **Ctrl+L** for *What you have heard*, which gathers each matter
that has reached you — follow-ups and differing accounts together, marked *Conflicting accounts* when they disagree — with each person's own words kept as a separate entry and how many tellings removed it is.
Names of people and places in the text are clickable and lead to the game's encyclopedia.

---

## Save safety

The rumor data is **not written into the game's save files**. It is stored separately, under
`Documents\Mount and Blade II Bannerlord\Configs\VividWorld\`.

Two consequences follow:

- **Existing saves still load after the mod is removed.** The personal relations recorded by "Personal relations" stay in the save, but the base game does not read them, and the screen goes back to the value a clan shares from its leader.
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
Menu): changed in game and written straight back to the `config.json`
above. Settings not on the page are left untouched.

---

## Languages

English and **Traditional Chinese** only at present. Further translations are welcome.

---

## Compatibility

**Mods of the "commoners do not address nobles" kind** (NaN, Lowborn and similar) are detected, upon
which a rumor mode of Auto switches to Realistic, restricting *asking* for news to clan tier 1 and above.
**At clan tier 0 you will get nothing out of anyone** — this is deliberate deference to those mods.
To let a commoner ask anyway, set the rumor mode to Casual.

**Calradia Remembers**: this mod is currently integrated with Calradia Remembers only. With it installed, when you chat
with a lord or wanderer through AI, he knows the rumors he has heard in this mod and still remembers — in the version he
heard, and he can say who told him; what he has forgotten or what is long out of date does not come up. Other AI chat mods
do not receive these rumors yet; without Calradia Remembers, nothing in this mod is affected.
The language used for this is set in the "AI Integration" group in MCM (English by default).

**Mods that take over the greeting:** some mods claim a lord's opening line in its entirety, leaving
this mod no opening. In that case the dialogue menu gains the line *"You looked like you were about to
say something."* so that the subject may still be raised.

**Affairs of Calradia**: with its personal relations mode on, it does the same thing as "Personal relations", and the two do not conflict; this mod detects that it is already being done and does not redistribute relations.

Apart from "Personal relations" changing how the game computes someone's relation with you, this mod does not modify the game's own AI decision-making, nor does it overwrite any native file.

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
| **Setting the record straight** | When the version that reached you is at odds with the facts, you will be able to take it up with the party concerned and bring the misunderstanding into the open |
| **Quarrels with a reply** | A dispute will no longer be settled in a single stroke; a contest such as a duel will let each party answer the other |
| **Consequences that reach further** | Outcomes will touch not only standing but money, renown, influence — and, in time, life and limb |
| **A voice of your own** | Spread news yourself, and set one lord against another; make rumour an instrument in your hands |
| **More sources of news** | Defections, rebellions and the fall of besieged settlements will travel among the lords as well |
| **Deeper ties with AI dialogue mods** | Beyond the rumors they have heard, making what they hold against one another available to mods that drive conversation with AI |

---

## Licence

MIT. See [`LICENSE`](LICENSE) for the full text.

This mod also ships [Harmony](https://github.com/pardeike/Harmony) (MIT as well), as the game does not
provide it. Its licence text is included in the release package as `0Harmony.LICENSE.txt`.
