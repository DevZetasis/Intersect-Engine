# Milestone 6H-A — Stat changes and healing moves

## Battle behavior

Each active monster has five temporary stages, one each for Attack, Defense, Special Attack, Special Defense and Speed. The existing Gen 4 calculator uses the ratio (2 + stage) / 2 for nonnegative stages and 2 / (2 + |stage|) for negative stages. It floors effective stats and keeps them at least one. Physical and special damage use their respective stat pairs. The existing critical-hit exceptions still apply. Priority takes precedence over Speed. Effective Speed is read when the turn order is formed, so a mid-turn Speed change affects later turns. Attack and Defense changes apply to damage calculated after the effect, including later actions in the same turn.

Stages clamp to −6…+6. The message describes the applied delta, including a cap message if the value stayed the same. Switching clears the outgoing monster's stages and end-of-battle cleanup clears the session. The rollback checkpoint restores stage state if the turn cannot be committed. The permanent monster record stores the same HP and PP as before. Its IVs, EVs, Nature, level and underlying stats are unchanged by battle stages.

The existing Monster Move Editor has a **Move effects** section. A move can contain up to four effects, executed in list order.

- **Modify stat stage** takes a self or opponent recipient, a supported stat, and a signed delta from −6 to +6 excluding zero.
- **Restore HP** takes a self recipient and either a positive fixed amount or a percentage from 1 to 100. Percentages floor the amount and restore at least one HP if the recipient is missing HP. The final amount caps at missing HP. Full HP produces a failure message. A fainted monster cannot act or recover through its move.

A configured Status move applies its effects without direct damage. An attempt spends PP even when it misses, is at full HP or reaches a stage cap. Opponent effects use the move's accuracy and Always Hits settings. Self-only Status moves need no opponent accuracy check. A damaging move's additional effects run after it deals positive damage. An opponent effect cannot apply to a fainted target. Legacy Status moves without effects remain unavailable, and effect-free damaging moves retain their existing behavior.

The server validates authored effects, freezes the move definition for the session and resolves it through the existing battle action. The existing battle ID, turn and revision checks reject repeated or stale actions. HP and PP commit before the authoritative result is published. The client only plays the resolved events and can advance or skip them without sending another action. The authored move animation plays before the effect messages. Healing interpolates the HP bar upward without the damage tint or recoil. The battle command panel and actor tooltips show nonzero stages.

## Data and compatibility

Migration `20261110000200_AddMonsterMoveEffects` adds nullable `MonsterMoves.Effects` text (SQLite TEXT, MySQL longtext) to the game database. It stores the ordered list as JSON. Existing rows and old content JSON load as empty lists. Player databases and animation records keep their existing schema. The move's normal UUID, animation reference, Effect position, copy/paste, undo and save flow remain in use.

Battle presentation adds a stat identifier and final stage to the event payload, a five-value stage list to actor snapshots, and distinct event kinds for stage changes, healing and failures. The new event fields use MessagePack keys 11 and 12. Actor snapshots use key 8 for stages. Previously assigned keys remain in place. The protocol marker is `monster-battles-v6`, requiring the matching server, client and editor.

## Matching executable paths

- Server — `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ha-build/server/Intersect Server.exe`
- Client — `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ha-build/client/Intersect Client.exe`
- Editor — `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ha-build/editor/Intersect Editor.exe`

## Disposable Windows test

The prepared world at `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ha-live-55968` uses its own SQLite files and a copy of the practice assets. Its seeded content includes the previous practice species and a healer at tile (1,0). It has no seeded accounts or characters.

Start the prepared server and wait for UDP port 55968.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ha-live-55968/server'
& '.\Intersect Server.exe' --no-upnp --no-port-check --migrate-automatically
```

Start the prepared client in a separate window. It connects to localhost:55968.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ha-live-55968/client'
& '.\Intersect Client.exe'
```

Create a disposable account named **m6hatester** and a default-class character named **M6HATrainer**. Enter the map normally. If editor permissions are needed, use `poweracc m6hatester 2` in the disposable server console. Log out of the client before logging into the editor.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ha-live-55968/client'
& 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ha-build/editor/Intersect Editor.exe'
```

The prepared definitions are editable in the existing Monster Move Editor.

| Definition | UUID | Setup |
|---|---|---|
| Stage Mossling | `6a000000-0000-4000-8000-000000000201` | Power Up, Iron Guard, Quick Focus, Sparring Strike |
| Recovery Pebbling | `6a000000-0000-4000-8000-000000000202` | Weaken, Recover, Sparring Strike, Sparring Burst |
| Effects Practice Rival | `6a000000-0000-4000-8000-000000000203` | Faster opponent with Sparring Strike |
| Power Up | `6a000000-0000-4000-8000-000000000101` | Self Attack +2 |
| Iron Guard | `6a000000-0000-4000-8000-000000000102` | Self Defense +2 |
| Weaken | `6a000000-0000-4000-8000-000000000103` | Opponent Attack −1 |
| Quick Focus | `6a000000-0000-4000-8000-000000000104` | Self Speed +2 |
| Recover | `6a000000-0000-4000-8000-000000000105` | Self restores 50% of maximum HP |
| Sparring Strike | `6a000000-0000-4000-8000-000000000106` | Physical, power 60, no added effect |
| Sparring Burst | `6a000000-0000-4000-8000-000000000107` | Special, power 60, no added effect |
| 6H-A Practice Effect | `6a000000-0000-4000-8000-000000000301` | Existing Animation Editor descriptor using Slashing.png |

Grant A and B while the character is online. Substitute the returned individual monster UUIDs for `<A>` and `<B>`. Species UUIDs and individual UUIDs serve different purposes in these commands.

```text
grantmonster "M6HATrainer" 6a000000-0000-4000-8000-000000000201 6a000000-0000-4000-8000-000000000211
grantmonster "M6HATrainer" 6a000000-0000-4000-8000-000000000202 6a000000-0000-4000-8000-000000000212
startmonsterbattle M6HATrainer <A> 6a000000-0000-4000-8000-000000000203 1
```

1. Use Sparring Strike at neutral stages. Record the move order and both HP losses. Use **Power Up** and then Sparring Strike again. Compare noncritical attacks. The command panel should show Atk +2, and the boosted strike should deal more damage. Power Up itself deals no damage.
2. Forfeit and heal with `healmonster <A> all` or the NPC beside spawn. Start the same battle and compare the rival's normal strikes before and after **Iron Guard**. Check for Def +2 and reduced damage after the guard is applied. Critical hits retain their existing defense-stage exception.
3. In another fresh battle with A, use **Quick Focus**. The rival acts first on the setup turn. On the next turn Sparring Strike should go first, since the two moves have equal priority. Use Monsters to switch to B and then back to A. The Speed stage should reset and the rival should lead again. HP and spent PP should remain associated with the correct monster.
4. Start with B by using `startmonsterbattle M6HATrainer <B> 6a000000-0000-4000-8000-000000000203 1`. Use **Weaken**, then compare subsequent rival hits with its neutral hits. The rival's command-panel summary should show Atk −1. Use Sparring Burst to check the ordinary special-damage path.
5. Let B lose more than half its maximum HP, then use **Recover**. Repeat when B is closer to full HP to check clamping. Check that the authored effect plays on B, the bar rises, the amount caps at maximum HP and one Recover PP is spent. Using Recover at full HP can be observed after raising its priority above the rival's in the editor and starting a fresh battle with full HP.
6. Inspect `<A>` and `<B>` with `inspectmonster` after a resolved turn. Skip playback and confirm neither HP nor PP changes again. Forfeit or disconnect, reconnect and inspect the same IDs. In a fresh battle the stages should be neutral and the committed HP and PP should match the roster.
7. In the editor, open Recover and edit its amount or select Fixed HP. Save, reopen the move and verify its effect and animation reference. Start a new battle to try the change. For a miss check, clear Always Hits on Weaken and set Accuracy to 1 before starting a new battle. A miss should spend PP and leave the rival's stage unchanged. Return the sample settings to their original values if you want to repeat the earlier comparisons.

## Verification and limits

The matching Debug server, client and editor builds succeeded with zero errors and matched handshake/packet assembly hashes. The final build logs showed 359 server warnings, 1,272 client warnings and 839 editor warnings, including dependency audit, nullable and platform analyzer diagnostics.

The broad server run passed 177 checks. The final focused battle run passed 32 checks, including the additional priority guard and damaging-effect regression. The client passed 28 checks, including upward HP playback, stages applied in event order, Next and Skip, authored effects and six Gwen viewport sizes. The editor passed 14 checks, including editing, copying, removing and undoing the new effect entries. The server suite exercised SQLite migration and persisted HP/PP, and checked MySQL schema and generated SQL offline.

The seeded disposable server started on UDP 55968 and shut down cleanly. The SQLite migration and all five example effect definitions were verified in that database.

Interactive Windows play remains for the checklist above. The implementation covers temporary stat stages and move-based HP recovery. Persistent conditions, accuracy/evasion stages, and other effect categories remain deferred.

## Source changes

- Modified `Framework/Intersect.Framework.Core/GameObjects/Monsters/MonsterMoveDescriptor.cs`
- Modified `Framework/Intersect.Framework.Core/Network/Packets/Server/MonsterBattlePresentationEvent.cs`
- Modified `Framework/Intersect.Framework.Core/Network/Packets/Server/MonsterBattleStatePacket.cs`
- Modified `Intersect (Core)/SharedConstants.cs`
- Modified `Intersect.Client.Core/Interface/Game/Monsters/MonsterBattlePlayback.cs`
- Modified `Intersect.Client.Core/Interface/Game/Monsters/MonsterBattleWindow.cs`
- Modified `Intersect.Editor/Forms/Editors/frmMonsterMove.cs`
- Modified `Intersect.Editor/Localization/Strings.MonsterMoves.cs`
- Modified `Intersect.Server.Core/Database/GameData/GameContext.cs`
- Modified `Intersect.Server.Core/Migrations/Sqlite/Game/SqliteGameContextModelSnapshot.cs`
- Modified `Intersect.Server.Core/Monsters/Battles/MonsterBattleCombatant.cs`
- Modified `Intersect.Server.Core/Monsters/Battles/MonsterBattleSession.cs`
- Modified `Intersect.Server/Migrations/MySql/Game/MySqlGameContextModelSnapshot.cs`
- Modified `Intersect.Tests.Client/Monsters/MonsterBattlePresentationTests.cs`
- Modified `Intersect.Tests.Editor/MonsterMoveEditorTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterBattleTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterConditionTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterMoveTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterNatureTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterOwnershipTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterSpeciesTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterTypeTests.cs`
- Added `Framework/Intersect.Framework.Core/GameObjects/Monsters/MonsterMoveEffect.cs`
- Added `Intersect.Editor/Forms/Editors/MonsterMoveEffectsControl.cs`
- Added `Intersect.Server.Core/Migrations/Sqlite/Game/20261110000200_AddMonsterMoveEffects.Designer.cs`
- Added `Intersect.Server.Core/Migrations/Sqlite/Game/20261110000200_AddMonsterMoveEffects.cs`
- Added `Intersect.Server/Migrations/MySql/Game/20261110000200_AddMonsterMoveEffects.Designer.cs`
- Added `Intersect.Server/Migrations/MySql/Game/20261110000200_AddMonsterMoveEffects.cs`
- Added `docs/monster-move-effects.md`
