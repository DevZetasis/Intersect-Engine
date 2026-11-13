# Milestone 6H-B — Custom monster status conditions

Open **Game Editors → Monster Status Editor** to create a UUID-backed condition. Each definition has a name, description, folder, exclusive group, persistence and switching rules, duration, recovery chance, action-block chance and optional custom blocked-action message. Five independent stat percentages control how much of each effective stat is retained. Periodic damage accepts whole HP or a percentage with up to four decimal places. Applied, activation and recovery animations use existing Intersect animation UUIDs. Save, Cancel, Copy, Paste, Undo and Delete use the normal content editor flow.

The **Monster Move Editor → Move effects** section adds Apply Status and Cure Status. Apply selects a condition, Self or Opponent, and a chance from 1–100%. The move first uses the established accuracy rules. The Major slot rejects another application while occupied, including reapplication of the same condition. Cure targets Self and can select a particular definition or Any Major status. Moves still support four ordered effects. Effects attached to damaging moves require a landed hit that deals damage, and a fainted target cannot receive a new effect.

## Battle rules and data

- Priority and effective Speed determine the turn order before either move. Condition stat percentages apply after temporary stages. The existing critical-hit exceptions apply to stages. Condition penalties continue to apply on critical hits. Stats floor at each step and remain at least one.
- A living monster attempting a move first rolls recovery, then action prevention. Recovery lets it continue immediately. A blocked action spends no PP. An executed move spends PP even if its condition application misses, fails the chance roll, or finds the slot occupied.
- Both active, living monsters process their end-of-turn effects after ordinary actions and voluntary switches. The newly active monster can tick on that turn. Benched monsters and forced replacements do not tick. A condition applied during a turn counts that turn if the recipient is active and living at the end. Percent damage floors with a minimum of one HP. HP clamps at zero. Both sides' residual effects finish before outcome or replacement selection.
- Fixed duration uses Turns / minimum. Random duration rolls inclusively between the two configured bounds. A counted turn applies its damage before decrementing and can expire afterward. Fainted monsters stop ticking. Switching can remove a condition independently of whether it persists after battle.
- The server snapshots referenced status definitions when a battle starts. Edits apply in subsequent battles. A stored countdown is retained across sessions. A formerly indefinite condition changed to finite rolls a countdown on its next applicable turn. An unavailable, invalid, or no-longer-persistent definition is cleared on owned roster reconciliation outside active battle. Deletion is protected while a move, an owned monster or an active battle references the definition. Status animation references also protect those animations from deletion.
- Persistent UUID and remaining turns share the HP/PP transaction and rollback checkpoint. Battle-only conditions live in session memory. The existing turn/revision and owner checks prevent duplicate action requests. Next and Skip operate on client playback only.
- The client shows the condition by HP, interpolates authoritative residual damage, and plays the applied, activation and recovery animations through the existing Gwen battle adapter. The owned roster shows the saved condition in the selected monster's details.
- The healer event has **Cure status** and **Restore HP and PP and cure status** choices. Existing HP/PP event choices retain their meaning. The prepared healer uses the combined choice. Console commands include `healmonster <individual-UUID> status` and `healmonster <individual-UUID> all`. HP-only and PP-only still work. Healing remains restricted while a monster is reserved in battle.

SQLite and MySQL migrations add `MonsterStatuses` to game data and nullable `StatusId` and `StatusTurns` to `OwnedMonsters`. Existing rows start unaffected. The network marker advances to `monster-battles-v7`. Status content is game object type 23. Actor status name uses key 9, presentation status name uses key 13 and roster status name uses key 10. Previously assigned keys are unchanged.

## Prepared Windows world

The runtime at `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6hb-live-55969` uses new SQLite files and a copy of the practice assets. The previous milestone worlds keep their own databases. Start the disposable server and wait for UDP 55969.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6hb-live-55969/server'
& '.\Intersect Server.exe' --no-upnp --no-port-check --migrate-automatically
```

Open the prepared client in a second PowerShell window.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6hb-live-55969/client'
& '.\Intersect Client.exe'
```

Create a disposable account named **m6hbtester** and a default-class character named **M6HBTrainer**. Enter the map normally. For editor access, run `poweracc m6hbtester 2` in the disposable server console and log out of the client before connecting with the editor.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6hb-live-55969/client'
& 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6hb-build/editor/Intersect Editor.exe'
```

The raw matching builds are `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6hb-build/server/Intersect Server.exe`, `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6hb-build/client/Intersect Client.exe`, and `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6hb-build/editor/Intersect Editor.exe`.

Record the individual UUIDs returned by the three grants as A, B and C.

```text
grantmonster "M6HBTrainer" 6b000000-0000-4000-8000-000000000201 6b000000-0000-4000-8000-000000000211
grantmonster "M6HBTrainer" 6b000000-0000-4000-8000-000000000202 6b000000-0000-4000-8000-000000000212
grantmonster "M6HBTrainer" 6b000000-0000-4000-8000-000000000203 6b000000-0000-4000-8000-000000000213
startmonsterbattle M6HBTrainer <A> 6b000000-0000-4000-8000-000000000204 1
```

| Definition | UUID suffix after `6b000000-0000-4000-8000-` | Test behavior |
|---|---|---|
| Condition Mossling | 000000000201 | Apply Poison, Apply Sick, Clear Condition, Sparring Strike |
| Penalty Pebbling | 000000000202 | Apply Burn, Apply Paralysis, Clear Condition, Sparring Strike |
| Recovery Emberling | 000000000203 | Apply Sleep, Apply Freeze, Clear Condition, Sparring Strike |
| Condition Practice Rival | 000000000204 | Weak direct-damage move |
| Poison / Apply Poison | 000000000001 / 000000000101 | Persistent, 12.5% HP per turn |
| Sick / Apply Sick | 000000000002 / 000000000102 | Battle-only, same 12.5% HP |
| Burn / Apply Burn | 000000000003 / 000000000103 | Persistent, 12.5% HP and 50% Attack |
| Paralysis / Apply Paralysis | 000000000004 / 000000000104 | Persistent, 25% Speed and 25% block chance |
| Sleep / Apply Sleep | 000000000005 / 000000000105 | Persistent, 2–4 active turns, blocks moves |
| Freeze / Apply Freeze | 000000000006 / 000000000106 | Persistent, 20% recovery before a blocked move |
| Clear Condition | 000000000107 | Cure own Major condition |

The first four application moves target Self so you can verify owned persistence and penalties. Sleep and Freeze target the practice opponent so it can demonstrate recovery without waiting for the player to regain control.

1. With A, use **Apply Poison**. Confirm the animation, HP label and end-of-turn damage. Use **Apply Sick** while poisoned and confirm the exclusive-slot message. Forfeit and inspect A with `inspectmonster <A>`. Restart only the disposable server using the launch command and reconnect. Inspect A again and confirm Poison and HP/PP were saved. Move it to storage and back to confirm the status follows the monster. Cure it using `healmonster <A> status`, or interact with the healer beside spawn.
2. Start with A again and use **Apply Sick**. Compare its periodic damage with Poison. Switch to B and back to A. Confirm Sick stays on A while benched and resumes ticking when A returns. Forfeit and start another battle. Sick should be gone, while the HP lost during the previous battle remains spent.
3. Start with B and compare noncritical Sparring Strike damage before and after applying Burn. Use Clear Condition to remove it, then apply Paralysis. Watch for changed move order and blocked actions that leave PP unchanged. Use the healer between samples.
4. Start with C. Apply Sleep to the opponent and confirm it skips moves and eventually recovers. Try Freeze in a fresh battle. For a deterministic editor check, change Freeze recovery to 100%, save and start a new battle. The opponent should recover before blocking its next action.
5. In the editor, copy Poison's settings into a new UUID, rename it, and change only Persist outside battle. Save, reopen and assign it to a move. Check that a referenced status cannot be deleted. Try selecting an activation and recovery animation. The prepared definitions already have an applied animation.
6. During status damage, use Next or Skip and compare displayed HP against `inspectmonster`. Forfeit or disconnect mid-battle and reconnect. Verify that the persisted results reflect the resolved turn once. Check the battle and party labels at 1280×720 and a smaller supported window.

## Verification and limits

The final Debug builds succeeded with zero errors. Build logs reported 359 server warnings, 276 client warnings and 1,114 editor warnings, including dependency audit, nullable and analyzer diagnostics. Shared handshake and packet assembly hashes matched across all three builds. An overlapping build briefly locked the server compiler output. The sequential build succeeded.

The final focused battle run passed 37 checks. The client passed 29 checks, including condition animations and HP updates across six Gwen viewport sizes. The editor passed 15 checks, including draft editing, copy/paste, and undo. The disposable SQLite game and player schemas booted successfully on port 55969, and the seeded definitions and healer loaded. SQLite migration, reference protection, and offline MySQL game and player model checks passed. The broad server regression run passed 184 checks with zero failures. The focused rerun used the final build and included the added random-duration endpoint test. A standalone schema runner initially omitted fixture setup, and the full server runner passed that same schema test with setup.

Only the Major group is executable in this milestone. Volatile conditions, items, wild encounters, trainer AI, PvP and double battles are outside this work. Interactive Windows verification remains on the checklist.

## Changed files

- Modified `Framework/Intersect.Framework.Core/Descriptors/GameObjectType.cs`
- Modified `Framework/Intersect.Framework.Core/GameObjects/Monsters/MonsterHealing.cs`
- Modified `Framework/Intersect.Framework.Core/GameObjects/Monsters/MonsterMoveEffect.cs`
- Modified `Framework/Intersect.Framework.Core/GameObjects/Monsters/OwnedMonsterDetails.cs`
- Modified `Framework/Intersect.Framework.Core/Network/Packets/Server/MonsterBattlePresentationEvent.cs`
- Modified `Framework/Intersect.Framework.Core/Network/Packets/Server/MonsterBattleStatePacket.cs`
- Modified `Intersect (Core)/SharedConstants.cs`
- Modified `Intersect.Client.Core/Interface/Game/Monsters/MonsterBattleAnimation.cs`
- Modified `Intersect.Client.Core/Interface/Game/Monsters/MonsterBattlePlayback.cs`
- Modified `Intersect.Client.Core/Interface/Game/Monsters/MonsterBattleWindow.cs`
- Modified `Intersect.Client.Core/Interface/Game/Monsters/MonsterRosterWindow.cs`
- Modified `Intersect.Editor/Forms/Editors/Events/CommandPrinter.MonsterHealing.cs`
- Modified `Intersect.Editor/Forms/Editors/Events/Event Commands/EventCommand_HealPlayerMonsters.cs`
- Modified `Intersect.Editor/Forms/Editors/MonsterMoveEffectsControl.cs`
- Modified `Intersect.Editor/Forms/Editors/frmMonsterMove.cs`
- Modified `Intersect.Editor/Forms/frmMain.cs`
- Modified `Intersect.Editor/Localization/Strings.MonsterHealing.cs`
- Modified `Intersect.Editor/Networking/PacketHandler.cs`
- Modified `Intersect.Server.Core/Database/DbInterface.cs`
- Modified `Intersect.Server.Core/Database/GameData/GameContext.Monsters.cs`
- Modified `Intersect.Server.Core/Database/GameData/GameContext.cs`
- Modified `Intersect.Server.Core/Database/GameData/IGameContext.cs`
- Modified `Intersect.Server.Core/Database/PlayerData/Players/MonsterInstance.Condition.cs`
- Modified `Intersect.Server.Core/Localization/CommandsNamespace.Monsters.cs`
- Modified `Intersect.Server.Core/Migrations/Sqlite/Game/SqliteGameContextModelSnapshot.cs`
- Modified `Intersect.Server.Core/Migrations/Sqlite/Player/SqlitePlayerContextModelSnapshot.cs`
- Modified `Intersect.Server.Core/Monsters/Battles/MonsterBattleCombatant.cs`
- Modified `Intersect.Server.Core/Monsters/Battles/MonsterBattleRegistry.cs`
- Modified `Intersect.Server.Core/Monsters/Battles/MonsterBattleSession.cs`
- Modified `Intersect.Server.Core/Monsters/Battles/MonsterPracticeBattles.cs`
- Modified `Intersect.Server.Core/Monsters/Calculations/MonsterBattleCalculator.cs`
- Modified `Intersect.Server.Core/Monsters/MonsterDiagnostics.cs`
- Modified `Intersect.Server.Core/Monsters/MonsterOwnershipService.Condition.cs`
- Modified `Intersect.Server.Core/Monsters/MonsterRosterDetails.cs`
- Modified `Intersect.Server.Core/Networking/PacketSender.cs`
- Modified `Intersect.Server/Core/Commands/HealMonsterCommand.cs`
- Modified `Intersect.Server/Migrations/MySql/Game/MySqlGameContextModelSnapshot.cs`
- Modified `Intersect.Server/Migrations/MySql/Player/MySqlPlayerContextModelSnapshot.cs`
- Modified `Intersect.Server/Networking/NetworkedPacketHandler.cs`
- Modified `Intersect.Tests.Client/Monsters/MonsterBattlePresentationTests.cs`
- Modified `Intersect.Tests.Editor/MonsterMoveEditorTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterBattleTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterConditionTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterIndividualTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterMoveTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterNatureTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterOwnershipTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterSpeciesTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterTypeTests.cs`
- Added `Framework/Intersect.Framework.Core/GameObjects/Monsters/MonsterStatusDescriptor.cs`
- Added `Intersect.Editor/Forms/Editors/frmMonsterStatus.cs`
- Added `Intersect.Server.Core/Migrations/Sqlite/Game/20261112000000_AddMonsterStatuses.Designer.cs`
- Added `Intersect.Server.Core/Migrations/Sqlite/Game/20261112000000_AddMonsterStatuses.cs`
- Added `Intersect.Server.Core/Migrations/Sqlite/Player/20261112000000_AddOwnedMonsterStatus.Designer.cs`
- Added `Intersect.Server.Core/Migrations/Sqlite/Player/20261112000000_AddOwnedMonsterStatus.cs`
- Added `Intersect.Server.Core/Monsters/Battles/MonsterBattleSession.Status.cs`
- Added `Intersect.Server/Migrations/MySql/Game/20261112000000_AddMonsterStatuses.Designer.cs`
- Added `Intersect.Server/Migrations/MySql/Game/20261112000000_AddMonsterStatuses.cs`
- Added `Intersect.Server/Migrations/MySql/Player/20261112000000_AddOwnedMonsterStatus.Designer.cs`
- Added `Intersect.Server/Migrations/MySql/Player/20261112000000_AddOwnedMonsterStatus.cs`
- Added `docs/monster-status-conditions.md`
