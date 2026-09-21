# Contributing to Vivid World

Thanks for taking an interest. This file covers how to build the mod, the rules the
codebase holds itself to, and what a useful pull request looks like.

For how the mod is put together, read [`docs/architecture.md`](docs/architecture.md) first.

---

## Building

**You need**

- The .NET SDK (8.0 or newer). The solution targets older frameworks, but the SDK builds them.
- A local install of Mount & Blade II: Bannerlord **v1.4.8**. The Module project references
  the game's own assemblies straight from that install; they are never copied into our output
  and are not redistributed here.
- [Mod Configuration Menu (MCM v5)](https://www.nexusmods.com/mountandblade2bannerlord/mods/612)
  installed in that same game folder. The in-game settings page compiles against it.

**Point the build at your game folder**

Copy `Directory.Build.props.user.example` to `Directory.Build.props.user` and edit the path.
That file is git-ignored, so your local path never lands in a commit:

```xml
<Project>
  <PropertyGroup>
    <GameFolder>D:\Steam\steamapps\common\Mount &amp; Blade II Bannerlord</GameFolder>
  </PropertyGroup>
</Project>
```

**Build and test**

```
dotnet build VividWorld.sln -c Release
dotnet test tests/VividWorld.Core.Tests
```

Both must be clean before you open a pull request. The build is expected to produce
**zero warnings** — if yours does, that is a finding, not noise to step over.

**Install a dev build into the game**

`tools/deploy.ps1` builds Release and installs it under the id `VividWorld.Dev`, so it sits
beside a released copy instead of overwriting it.

**Build a release zip**

`tools/package.ps1` builds Release and produces `dist/VividWorld-<version>.zip`, laid out so a
player can unzip it straight into the game's `Modules\` folder. It checks its own output and
refuses to produce a package that is missing a file or carrying one that should not ship.

---

## The three rules the codebase will not bend on

These are not style preferences. A pull request that breaks one of them cannot be merged as-is,
and in most cases a test will catch it before a human does.

### 1. `VividWorld.Core` never references the game

`VividWorld.Core` targets `netstandard2.0` and is not allowed to reference `TaleWorlds.*` or
`0Harmony`. It knows heroes, settlements and clans only as `string` ids. Everything that needs
a real `Hero` lives in `VividWorld.Module`.

This is what makes the rumour engine testable at all: the 859 tests run without the game.
If you find yourself wanting a `Hero` inside Core, the logic belongs in Module, or Core needs
the one fact it actually wanted passed in as a value.

### 2. Core does not roll its own dice

`System.Random`, `Guid.NewGuid()`, `DateTime.Now` and the game's `MBRandom` are all banned in
Core. Every random choice goes through `IDeterministicRng` seeded from the campaign, the game
day and what is being decided.

The reason is save/load: a player who reloads must get the same world back. A raw `Random` call
re-rolls on every load, and the bug that produces is nearly impossible to reproduce on purpose.

### 3. No tunable numbers in the code

Anything a player or a designer might reasonably want to change belongs in `config.json`, not
in a literal. Add the key to the matching class under `src/VividWorld.Core/Config/`, give it a
default and clamp it in `Normalize()`, then use it.

Existing config files are only ever added to — a missing key is filled in with its default, an
existing value is left alone, and unknown keys are preserved so that a newer version's settings
survive a downgrade.

---

## Adding content without writing code

Most of what happens in a campaign is data, not C#. Three files under `module/ModuleData/`:

| File | What it holds |
|---|---|
| `vividworld_events.json` | Event templates — the things that happen |
| `vividworld_situations.json` | Situations — how an event comes about, and how it branches |
| `vividworld_situation_events.json` | The event templates a situation's branches emit |

**An event template** declares who takes part (`roles`), how big a deal it is (`dramaWeight`,
1–5, which decides how far word travels), whether it starts public or secret (`origin`), what it
does to opinions (`opinion`), and the individual `facts` that make up the story. Each fact carries
a `fragility` — how easily it gets lost as the story is passed along. That loss is the point of
the system, not a defect in it.

**A situation** declares the roles it needs, `conditions` that must hold for it to happen at all,
and `branches` for how it can turn out. The condition evaluators are a fixed set (same settlement,
same kingdom, different clan, clan tier comparison, clan-leader check, cooldown, and so on); if
your idea needs a new one, that part is code.

**Every line of text needs a string key.** Facts reference `textId`, and the key has to exist in
`module/ModuleData/Languages/std_module_strings_xml.xml`. A missing key falls back to English
*per key*, so a half-translated language reads as a Chinese sentence with English punctuation in
the middle of it. Because of that, every folder under `Languages/` must carry exactly the same
key set as the English table. Three tests enforce this, and they will fail your build rather than
let a half-filled language through.

**One content rule worth knowing before you write a scene:** a scene may not claim anything about
game state the mod cannot actually change. No money changing hands, no marriages, no one thrown in
a cell — the mod does not move any of those, and a scene that says otherwise is lying to the
player. Scenes work with what the mod really does touch: who resents whom, and who heard about it.

---

## Pull requests

- **One thing per pull request.** A behaviour change and a refactor in the same diff are hard to
  review and harder to revert.
- **Say how you know it works.** Which test covers it; or, if it needs a real campaign, what you
  did in-game and what you saw. "Should work" is not an answer — if you could not verify it, say
  so plainly and it will be treated as unverified.
- **New behaviour comes with a way to see it.** Logs should show *why* something happened: the
  inputs, each multiplier, and — this is the part people skip — **what was excluded and why**. A
  log line that only prints the winner cannot be debugged.
- **Don't guess at the game's API.** The comments in this codebase cite specific decompiled
  signatures for a reason: assuming what a `TaleWorlds` method does, based on its name, has been
  wrong often enough to be a rule. Check the real thing.
- **Translations are welcome**, but only complete ones — see the key-set rule above.

### Notation you will see in the comments

Comments cite an internal engineering spec (`§4`, `§6.7.4`) and an internal verification ledger
(`D-73`, `S-18`, `L-45`). Those two documents are part of the project's private working notes and
are not published here. You do not need them to contribute — the citation is there so a maintainer
can trace where a decision came from. `D` means it was confirmed by decompiling the game, `S` that
it was read out of shipped source, `L` that it was observed in a real campaign.

---

## Licence

MIT — see [`LICENSE`](LICENSE). By opening a pull request you agree your contribution ships
under it. Harmony is bundled under its own MIT licence (`lib/0Harmony.LICENSE.txt`).
