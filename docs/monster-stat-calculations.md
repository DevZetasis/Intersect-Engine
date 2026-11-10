# Individual monsters and Generation 4 core calculations (Milestone 5B)

This is a server-only calculation foundation, not a complete Generation 4 simulator or move executor. Trainer combat, editors, species learnsets and individual learned-move inventories are unchanged.

Milestone 6C now uses this foundation for snapshotted practice sessions and persistent individual move slots; see [monster-battles.md](monster-battles.md). The historical calculation subset below remains unchanged; complete move effects and advanced battle mechanics are still deferred.

## Persistent attributes

OwnedMonsters adds nullable NatureId and six scalar IV columns (IvHP, IvAttack, IvDefense, IvSpecialAttack, IvSpecialDefense, IvSpeed), plus the corresponding Ev columns. IVs are always 0�31. Each committed new grant draws six uniform IVs once, starts at zero EVs, and samples a valid positive-weight nature. The request UUID lookup precedes every draw. Moving, reading, nicknames, restarts and retries do not generate attributes. Initial IVs and nature UUID cannot be changed through ordinary persistence.

The additive player migrations 20261108040000_AddMonsterAttributes assign all pre-existing rows deterministic IV=0 and EV=0 and leave NatureId NULL. These are valid, conservative legacy values, not random rolls or retrospective assignment. Defaults are persisted columns, not load-time fallbacks. Identity, owner, species, level, experience, nickname, placement, request key, revision and creation timestamp remain unchanged. NatureId has a lookup index; structural checks enforce IV 0�31 and EV 0�255.

The internal MonsterOwnershipService.SetEVs seam replaces a six-value EV profile only after owner/revision validation. Default caps are 255 per stat and 510 total. No rewards, training items, public EV editing packet/command or progression loop is added. Metadata saves preserve already-initialized profiles even if configuration caps are later reduced; they do not truncate values. New EV writes and derived calculations validate active limits, so an above-cap old custom profile needs an explicit future adjustment before calculation under lower caps.

## Rules and randomness

Options.Monsters.Battle holds an immutable MonsterBattleRules value. Defaults: MaxLevel=100, MaxEvPerStat=255, MaxEvTotal=510, StabMultiplier=1.5, CriticalMultiplier=2, MinimumDamagePercent=85, MaximumDamagePercent=100. Supported custom ranges: MaxLevel 1�10000; per-stat EV cap 0�255; total cap 0�6�per-stat cap; positive multipliers up to 10; random percent bounds 1�100 in ascending order. Defaults remain accessible independently through MonsterBattleRules.Generation4. No configuration file is changed by this implementation.

IMonsterRandom.NextInt64(bound) must return a uniform value in [0,bound); range violations are rejected. SystemMonsterRandom uses the server's thread-safe Random.Shared. Injected sources make tests deterministic; this is not reproduction of the original game's PRNG stream. Weighted selection uses checked Int64 sums and UUID order. No eligible weight yields neutral/unassigned plus logging, preserving IVs and identity.

## Final stats

Using nonnegative integer arithmetic, q = floor(EV/4).

- HP = floor(((2�Base + IV + q)�Level)/100) + Level + 10.
- Other = floor((floor(((2�Base + IV + q)�Level)/100) + 5) � nature factor).

Nature factors come from the referenced custom definition, including decimal 0�50% magnitudes. Decimal percentages are converted to exact rational numerator/denominator values; floor is applied after the nature step. HP ignores nature entirely. Intermediate arithmetic uses BigInteger; results are checked Int64 values. Invalid inputs, zero defenses and result overflow fail clearly rather than wrapping. No named-species exceptions are introduced. These formulas and the 95-base-HP / 135-base-Attack test examples were checked against [Smogon's DP stats research](https://www.smogon.com/dp/articles/stats).

## Supported damage subset and phase order

For Physical use calculated Attack/Defense; for Special use SpecialAttack/SpecialDefense. Standard stat-stage ratios use (2+s)/2 or 2/(2-s), floored and clamped to at least one. Critical hits ignore negative attacking stages and positive defending stages, while retaining advantageous stages. Status moves return zero ordinary damage.

The core is floor(floor((floor(2�Level/5)+2)�Power�Attack/50)/Defense), followed by ordered BeforeAddTwo factors, +2, critical multiplier, ordered AfterCritical factors, an inclusive uniform damage percent, STAB, each defending type and ordered AfterTypes factors. Each multiplication/division floors at that phase. Immunity returns zero; other damaging hits have minimum one. Source: [DPP damage research and its worked 114�134 example](https://www.smogon.com/dp/articles/damage_formula), independently cross-checked with [the maintained DPP calculator](https://github.com/smogon/damage-calc/blob/master/calc/src/mechanics/gen4.ts).

Custom type systems apply primary then secondary UUID order from the species input, flooring each separately. This preserves ordered phase semantics, but does not introduce the original games' hard-coded elemental-type precedence list. A resistance then weakness can differ by one from the reverse order when intermediate damage is odd. This custom ordering is explicit and covered by tests. The existing pure type-effectiveness product evaluator remains available separately.

BeforeAddTwo, AfterCritical and AfterTypes are ordered, positive decimal-factor extension points. They do not implement burn, screens, weather, items, abilities or spread logic. Future mechanics must supply factors at the correct phase, not collapse everything into a modern single multiplier. No battle state/status detection exists here.

## Accuracy, critical hits and action ordering

Accuracy and evasion inputs are each validated in [-6,+6]. The Platinum implementation combines accuracy minus evasion, clamps that result to [-6,+6], applies the 3-based ratio to the move's integer percentage, then checks a uniform 0�99 draw against the floored threshold. AlwaysHits bypasses the random draw. This follows [the Platinum accuracy implementation](https://github.com/pret/pokeplatinum/blob/main/src/battle/battle_controller_player.c); an older Smogon stats article and some simulator code present a different separate-stage formula, so they are not used for this step.

Critical stages use denominators 16,8,4,3,2, with stage >=4 capped at one half. The stage API rejects negative stages. Default critical damage is 2�. Probabilities and stage interaction were checked against [Platinum battle mechanics](https://github.com/pret/pokeplatinum/blob/main/src/battle/battle_lib.c). CriticalHit and damage calculation are separate explicit operations, not an automatic move executor.

OrderActions sorts any number of distinct actor UUIDs by priority descending, then positive effective Speed descending. Equal groups use an unbiased Fisher�Yates shuffle with the injected random source; no random sort comparator is used. The API does not assume only two actors, but does not create sessions, turn queues or client actions.

## Supported versus deferred

Supported: explicit final stats, custom nature percentages, IV/EV validation, weighted nature generation, fixed-power Physical/Special core damage, status-zero direct damage, selected attack/defense stages and critical exceptions, STAB, ordered custom dual types and immunity, damage variance, accuracy/evasion stages, critical probability stages, priority/Speed/tie ordering and named numeric phase hooks.

Deferred: full move execution, PP, learned move inventory, switching, effects/statuses, burn rules, screens, weather, held items, abilities (including critical/accuracy bonuses), double-battle spread detection, variable/fixed-damage move exceptions, invulnerability/OHKO accuracy exceptions, Trick Room, paralysis, field/turn state, battle snapshots, rewards, encounters and capture. Custom rule overrides and arbitrary custom type order are not claimed to be original-game compatibility.

Future server-authoritative sessions should resolve and snapshot participant stats and applicable nature/modifier values at initialization, retaining immutable values for the active battle. This milestone recalculates on demand using current definitions but does not implement those snapshots. Client-supplied final stats, rules, stages, effects or RNG results must never be trusted when future battle submission packets are introduced.

## Console inspection

`inspectmonster <individual-monster-UUID>` is a read-only server-console command. It queries persistent data directly (including offline monsters), prints species/level/nature, IVs, EVs and current calculated stats, and does not normalize placement or mutate revisions. It prints neutral/unassigned for null NatureId and a clear calculation error for missing definitions or incompatible rules. No account information or owner credentials are printed.

## Disposable live verification (manual)

No live server/client session or real database migration was performed for this milestone. Use matching binaries from the `m5b-build/server`, `m5b-build/client`, and `m5b-build/editor` directories under `C:\Users\calip\.codex\visualizations\2026\11\08\01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0`. Keep complete build folders, including DLLs and native runtime dependencies, together.

Create a fresh scratch server directory and a fresh SQLite configuration. The following PowerShell commands create new files only in a newly named temporary directory; they do not launch against the repository's resources/configuration:

```powershell
$build = 'C:\Users\calip\.codex\visualizations\2026\11\08\01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0\m5b-build'
$scratch = Join-Path $env:TEMP ('Intersect-M5B-Live-' + [guid]::NewGuid())
New-Item -ItemType Directory -Path "$scratch\server\resources" -Force | Out-Null
Copy-Item -Path "$build\server\*" -Destination "$scratch\server" -Recurse
@'
{"ServerPort":55400,"UPnP":false,"GameDatabase":{"Type":"SQLite"},"PlayerDatabase":{"Type":"SQLite"},"LoggingDatabase":{"Type":"SQLite"}}
'@ | Set-Content -LiteralPath "$scratch\server\resources\config.json" -Encoding UTF8
Push-Location "$scratch\server"
try { & '.\Intersect Server.exe' } finally { Pop-Location }
```

Use an unused port instead of 55400 if necessary. Follow the usual first-run prompts to initialize this **new** server and an administrator account. Do not import existing configuration files or database connection strings. Any automatic migrations apply only to this scratch directory's new SQLite databases. Server API settings, if enabled separately in your runtime, also need an unused port. Use the console's normal shutdown command before restarting this scratch server; leave other running servers untouched.

Prepare separate scratch client/editor runtime folders with the normal asset-only resources required by your game (textures, fonts, sounds, GUI layouts and editor assets); overlay the complete matching client/editor build folders. Connect both to localhost port 55400 using their normal connection settings in these scratch copies. Do not point either at your existing server. Asset packaging and interactive connection/login remain manual checks, not automated test results.

1. Create a disposable character named `TrainerOne`. In the editor create a species and record its UUID. Use base stats 45 in all six fields to make manual arithmetic easy.
2. Create a neutral nature with positive weight and make it the only eligible nature. Create other zero-weight natures as desired. Save the definitions. New grants must select the eligible nature; zero-weight definitions must not be selected.
3. On the **server console**, substitute the actual species UUID below. The request UUIDs are examples and may be reused exactly within this fresh test world:

   ```text
   grantmonster "TrainerOne" <species-UUID> 21aa38c0-169e-4bc7-b779-e3c14b6600d1
   inspectmonster <individual-UUID-returned-by-grant>
   grantmonster "TrainerOne" <species-UUID> 21aa38c0-169e-4bc7-b779-e3c14b6600d1
   inspectmonster <same-individual-UUID>
   grantmonster "TrainerOne" <species-UUID> 21aa38c0-169e-4bc7-b779-e3c14b6600d2
   ```

4. Each new request creates a different individual UUID with six IVs in 0..31 and zero EVs. A repeated request returns the first UUID and preserves its attributes and placement. Randomly generated individuals need not differ in every stat, and identical derived stats at level 1 are common because of flooring.
5. With base stats 45, level 1, zero EVs and neutral nature, IV 0 gives HP 11 and other stats 5; IV 31 gives HP 12 and other stats 6. Different IVs are still visible in inspection. At level 100, the same profiles would give HP 200 versus 231 and other stats 95 versus 126. The latter level-100 profiles are deterministic calculator examples, not promises about the default level-1 grant or a new level-editing command.
6. Use the existing roster Store/Retrieve controls, inspect again, log out, shut down and restart **this scratch server**, and inspect again. UUID, IVs, EVs and NatureId must remain unchanged. Verify existing species/type/move/nature editing and roster behavior.
7. Log the character out and try deleting its assigned nature in the editor. Deletion must be refused with an owned-monster reference message. Edit that nature's Attack increase instead; inspect again to check the current definition is used. Small level-1 values may round to the same result; a 50% increase on stat 5 produces 7.
8. Set every nature's weight to zero, save, grant with another new request UUID and inspect. It must succeed with neutral/unassigned nature, valid IVs, zero EVs, and one warning for the new grant. Retry it: no new individual or reroll should occur. An empty catalog can be tested in another fresh scratch world before creating any natures. Referenced natures cannot be deleted just to make the catalog empty.

EV mutation, high-level stat examples, damage, accuracy, criticals, offline reference checks, migration and controlled RNG boundaries are covered by automated tests. No EV editor, battle UI, level setter or client-side attribute editing command was added.

## Build and automated verification

All three Debug builds completed with zero errors: server (84 warnings), client (1265 warnings), editor (1116 warnings). Warnings remain; this milestone does not claim warning-free builds.

Cached NUnit assertion harnesses outside the repository compiled and executed the actual repository test sources: 112 server cases, 13 editor checks and 8 client checks passed, with zero failures. Server coverage includes the two new calculation/individual fixtures and existing nature/move/type/species/ownership/command/player regression fixtures. A separate focused run against the final rebuilt server binaries additionally passed the synchronous/asynchronous initialized-attribute protection check. This is targeted regression verification, not a run of every solution test project through `dotnet test`.

SQLite schema upgrades/downgrades, persistence/reload and legacy-row preservation ran only in GUID-named disposable temporary databases. MySQL migration target-model agreement and generated additive SQL were checked without connecting to a MySQL server; a real MySQL migration remains a manual integration check. Client/editor checks exercise content handling and UI controls/layout in their test harnesses, not a connected multiplayer play session. No real game/player database was opened or migrated and no running application was stopped.

Logs for this run are `%TEMP%\monster-m5b-server-build.log`, `%TEMP%\monster-m5b-client-build.log`, `%TEMP%\monster-m5b-editor-build.log`, `%TEMP%\monster-m5b-server-tests-final.log`, `%TEMP%\monster-m5b-client-tests.log`, `%TEMP%\monster-m5b-editor-tests.log`, and `%TEMP%\monster-m5b-async-smoke.log`.

## Milestone file manifest

Paths are relative to the repository root. Generated migration designers and snapshots follow the existing provider layout.

Modified existing files (16):

```text
docs/monster-natures.md
Framework/Intersect.Framework.Core/Config/MonsterOptions.cs
Intersect.Server.Core/Database/DbInterface.cs
Intersect.Server.Core/Database/GameData/GameContext.Monsters.cs
Intersect.Server.Core/Database/PlayerData/PlayerContext.cs
Intersect.Server.Core/Database/PlayerData/Players/MonsterInstance.cs
Intersect.Server.Core/Intersect.Server.Core.csproj
Intersect.Server.Core/Localization/CommandsNamespace.Monsters.cs
Intersect.Server.Core/Migrations/Sqlite/Player/SqlitePlayerContextModelSnapshot.cs
Intersect.Server.Core/Monsters/MonsterOwnershipService.cs
Intersect.Server.Core/Monsters/MonsterRoster.cs
Intersect.Server/Core/Commands/GrantMonsterCommand.cs
Intersect.Server/Core/ConsoleService.ConsoleThread.cs
Intersect.Server/Migrations/MySql/Player/MySqlPlayerContextModelSnapshot.cs
Intersect.Server/Networking/NetworkedPacketHandler.cs
Intersect.Tests.Server/GameObjects/MonsterOwnershipTests.cs
```

Added files (17):

```text
docs/monster-stat-calculations.md
Framework/Intersect.Framework.Core/Config/MonsterBattleRules.cs
Framework/Intersect.Framework.Core/GameObjects/Monsters/MonsterStatValues.cs
Intersect.Server.Core/Database/PlayerData/PlayerContext.Monsters.cs
Intersect.Server.Core/Migrations/Sqlite/Player/20261108040000_AddMonsterAttributes.cs
Intersect.Server.Core/Migrations/Sqlite/Player/20261108040000_AddMonsterAttributes.Designer.cs
Intersect.Server.Core/Monsters/Calculations/MonsterAttributeGeneration.cs
Intersect.Server.Core/Monsters/Calculations/MonsterBattleCalculator.cs
Intersect.Server.Core/Monsters/Calculations/MonsterRandom.cs
Intersect.Server.Core/Monsters/Calculations/MonsterStatCalculator.cs
Intersect.Server.Core/Monsters/MonsterDiagnostics.cs
Intersect.Server.Core/Monsters/MonsterNatureReferences.cs
Intersect.Server/Core/Commands/InspectMonsterCommand.cs
Intersect.Server/Migrations/MySql/Player/20261108040000_AddMonsterAttributes.cs
Intersect.Server/Migrations/MySql/Player/20261108040000_AddMonsterAttributes.Designer.cs
Intersect.Tests.Server/GameObjects/MonsterCalculationTests.cs
Intersect.Tests.Server/GameObjects/MonsterIndividualTests.cs
```
