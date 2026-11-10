# Persistent monster HP, PP and healing (Milestone 6E)

## Persistence and compatibility

OwnedMonsters has four additive player-database columns: CurrentHP (64-bit integer, default 0), MaximumHPAtLastUpdate (64-bit integer, default 0), ConditionInitialized (boolean, default false), and nullable MovePP (JSON text). MovePP contains exactly four entries once initialized: assigned move UUID, current PP, and last known maximum PP. Empty slots have null UUID and 0/0 PP. Existing move UUID columns remain authoritative. Validation rejects mismatched sidecars and out-of-bounds resources.

SQLite and MySQL migrations are both `20261110000000_AddMonsterCondition`. Only player databases change. The nullable JSON column avoids MySQL longtext default restrictions. Both target models/snapshots are updated. SQLite is exercised with real disposable databases, including upgrading a pre-condition monster; MySQL model/operations are inspected without connecting to a MySQL server. Existing development databases have not been migrated.

Legacy rows initialize on their first authorized ownership load using canonical species/stats/moves. Initialization and resources save together. It runs once, preserves every identity/attribute/placement field, and distinguishes legitimate fainted zero from legacy zero. Missing species/nature definitions defer initialization. Grant retries immediately return the original individual and cannot heal it.

Max HP increases preserve the previous HP deficit. Decreases clamp current HP. A fainted monster remains at zero in either case. Same-slot/same-move PP is preserved, clamped on maximum reductions, and never refilled by maximum increases. A replacement move starts full. Missing move definitions have no usable PP. Definition edits are reconciled after active battle reservations end; live combat keeps its existing immutable definition/stat snapshot.

## Turn commits and cleanup

Battle start loads owned party HP and four PP values, validates ownership/placement, and refuses fainted monsters. The generated practice opponent remains temporary. A resolved turn saves owned HP/PP with the existing optimistic Revision concurrency token before publishing success or animations. Duplicate/stale actions cannot apply another turn. The content coordinator and owner gate serialize reservations, healing, placement and turn writes in one lock order.

On a failed write the session restores its last confirmed resource checkpoint, cancels and releases its reservation. A unique new revision token allows a fresh context to verify an ambiguous exception after commit: a proven commit is retained and never reapplied. If the database cannot confirm the result, cancellation states that the displayed checkpoint is only the last confirmed state and asks for a roster refresh. A publication failure after a committed write never undoes the database update.

Victory, defeat, draw, forfeit, disconnect, operator cancellation and graceful shutdown do not heal. Every previously committed turn survives. Unsubmitted actions have no effect. Fainted actors cannot restart until HP is healed. No experience/EV rewards, switching, items, status conditions, capture or wild encounters are added. Future additional owned combatants can reuse the same resource/revision boundary; this milestone remains 1v1.

## Healing and editor integration

MonsterHealingService routes through MonsterOwnershipService: one owned monster, party only, or explicitly all owned including storage; HP, PP or both. Owner filtering is mandatory. A collection heal checks all reservations before making changes and saves the whole selection atomically. Healing changes only condition and revision, never IVs/EVs/nature/level/experience/species/moves/placement. Reserved selections are rejected, with no partial heal.

In the existing Event Editor, Player Control now includes **Heal Player Monsters**. Select Party Only or Party and Storage and Restore HP, Restore PP, or Restore Both. Defaults are Party Only/Both. Options are staged until OK; Cancel leaves the command unchanged. The command uses existing typed event serialization, localization, command printing and server dispatch. It takes the actual triggering Player, never a client-supplied owner UUID. A failed event heal logs a warning, sends a failure notice and stops that event command stack so a scripted success message does not follow a failed heal. There is no separate healing editor or healing packet.

Server-console commands:

```text
inspectmonster <individual-monster-UUID>
healmonster <individual-monster-UUID> <hp|pp|all>
startmonsterbattle <online-character-name-or-UUID> <owned-party-monster-UUID> <opponent-species-UUID> <level>
cancelmonsterbattle <battle-UUID>
grantmonster "CharacterName" <species-UUID> <stable-request-UUID>
```

inspectmonster remains read-only and adds stored current/max HP, fainted status and assigned UUID/name/current/max PP. Pending legacy initialization is reported rather than silently healed. healmonster is registered only with server console commands, not public chat. Keep console access restricted to trusted operators as with other administrative commands.

## Client and protocol

The existing owner-scoped roster details append MessagePack keys 6..9: initialized flag, current HP, maximum HP and move PP entries. Existing keys/enums remain unchanged; the new event enum is appended. The fork protocol marker advances from battle v2 to v3 at the same byte length, retaining RSA hail capacity. Deploy matching server/client/editor together; older binaries are intentionally rejected. Newer database rows should not be run with old server binaries.

Monster Details adds read-only current/max HP, a bar, fainted status and assigned move PP. Stats, IVs/EVs, Nature, party/storage operations and scroll layout remain. The battle scene/playback state machine is unchanged: its initial snapshot now contains actual persisted HP/PP and it continues sequential damage/faint/result playback. No roster healing controls exist.

## Matching builds

Debug builds (net8.0), zero errors. Final incremental build warning totals: server 284, client 276, editor 277. These include existing warnings in shared dependencies; initial full builds had server 359, client 1271 and editor 839 warnings. Logs are `%TEMP%/monster-m6e-{server,client,editor}-final-build.log`. MySQL has no live database integration run.

```text
C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6e-build/server/Intersect Server.exe
C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6e-build/client/Intersect Client.exe
C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6e-build/editor/Intersect Editor.exe
```

## Disposable live world and manual checklist

A fresh world is prepared at `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6e-live-42099633-ae0c-466a-9ce4-5a4afcf38158`, listening on localhost:55965. Only copied test assets and new configurations are used; no previous player/game databases are copied. Use the live client executable, which has resources beside it. The raw build outputs need resources before interactive use.

Start its server in PowerShell:

```powershell
Set-Location 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6e-live-42099633-ae0c-466a-9ce4-5a4afcf38158/server'
& '.\Intersect Server.exe' --no-upnp --no-port-check --migrate-automatically
```

This automatic-migration flag is intended ONLY for the new disposable world. Do not run it against an existing development database without separately approving that upgrade. Start `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6e-live-42099633-ae0c-466a-9ce4-5a4afcf38158/client/Intersect Client.exe` with its client folder as working directory. Build outputs and the live runtime have matching binaries.

1. Use New Account and normal default-class character creation, then enter the map. Do not seed accounts, characters or slot rows with SQL.
2. Use the seeded test species/moves, or create species and a damaging move with Maximum PP in the matching editor. Add the move to the level-1 learnset and save.
3. Grant with a new request UUID; record the individual UUID. Inspect: full HP/PP, correct attributes and party position. Retry the same request UUID and confirm one individual remains.
4. Start a practice battle. Verify starting HP/PP and HUD. Use several damaging moves and take damage; finish or Forfeit, then Return to game. Inspect persisted remaining resources and roster readouts.
5. Start another battle: it must begin at the previous resources. Play a turn, disconnect during playback and reconnect. Inspect, start another battle and confirm committed damage/PP stayed spent and the old reservation cleared.
6. Exit ONLY this disposable server gracefully, restart with the command above, reconnect and inspect again. Preserve its database/configuration files.
7. Store and retrieve the monster in Monsters; HP/PP must not refill. Retry grant again; HP/PP and count remain unchanged.
8. Test `healmonster UUID hp`: only HP restores. Spend HP/PP again; test `pp`: only PP restores. Test `all`: both restore. While a battle reserves it, healing must be refused.
9. In the existing Map/Event Editor, create an action-button NPC/event at a reachable tile. Add Heal Player Monsters (Party Only/Both), followed by Show Text. Save, interact using the normal client; inspect party resources. Repeat with HP-only and PP-only. Put another damaged monster in storage, verify Party Only leaves it alone and Party and Storage restores it. Cancel an unsaved command edit and confirm its options remain unchanged.
10. Battle a high-level opponent until fainted. Verify zero HP, fainted roster status and refusal to start another battle. Heal HP, then verify eligibility returns. PP-only healing must not revive it.
11. Verify win/loss/draw/exhaustion, Forfeit and Return to game; front/back art, HP animations, PP labels and sequential moves must remain functional. Check 800x600, 1280x720 and larger windows for clipping.

Live results and automated results are recorded separately below. A manual checklist is not a claim that these steps have all been performed.

## Automated results

Condition fixture: 23/23 passed on disposable SQLite, covering initialization, legacy migration, bounds, max changes, replacement moves, grant idempotency, transfers/reload, partial/party/storage healing, authorization/reservations, all battle cleanup outcomes, per-turn publication ordering, duplicate/stale actions, revision conflicts, precommit failure, ambiguous postcommit failure, publication failure, transactional multi-row rollback, event serialization/trigger identity, roster and diagnostics.

Client: 22 checks passed, including six real Gwen roster viewport sizes, new condition/fainted/PP assertions and existing battle presentation/playback regressions. Editor: 14 checks passed, including healing option staging/save/cancel and existing species/type/move/nature editors. Broader server suite: **162 passed, 0 failed** (includes the 23 condition tests). These are focused executable harnesses compiling the repository's NUnit fixtures against the matching outputs using cached NUnit binaries; the network-isolated environment does not restore a separate test runner package.

## Live results

The fresh server booted on localhost:55965 with zero accounts and zero characters; the user created M6ETrainer through the normal client and entered the map. A console grant put Mossling in party slot 1 at 12/12 HP and 20/20 PP for each move. One resolved Leaf Tackle turn left Mossling at 11/12 HP and 19/20 Leaf Tackle PP in both the client and server. Those values survived forfeiting. The in-game Monster Healer then restored 12/12 HP and 20/20 PP, confirmed by the user and server inspection. Another resolved turn returned the values to 11/12 and 19/20. The disposable server shut down while that battle was active and reloaded both values on restart. The user reconnected and confirmed the same values in Monsters. A repeated grant returned the same individual in party slot 1 with both values intact. Console HP-only healing restored 12/12 HP and left Leaf Tackle at 19/20. Console PP-only healing then restored 20/20 PP. The checklist includes additional interactive cases covered by the automated suite, including fainted battle rejection and party/storage transfer. Seeded content only: Mossling species `1fc5e7b8-87e2-4724-948b-ffed6620dd1f`, faster gentle opponent `7306b834-caa1-4ae6-b0ce-4a29e31e7d7f`. An action-button Monster Healer event at tile (1,0), beside default spawn (0,0), uses the real Heal Player Monsters command (Party/Both) followed by Show Text. Face the NPC and use the configured interaction key after taking damage/spending PP. No player rows were seeded or repaired. The existing 6D live confirmations remain historical presentation evidence, not proof of new persistent-condition behavior.

## Limitations

Only the existing single-active-owned-monster practice flow is wired to persistence. No automatic battle-end healing is provided. New healing-event dispatch is implemented; manual in-game event testing remains on the checklist until confirmed. Abrupt failure during a database outage can prevent an immediate roster refresh, but cannot authorize replay of a committed turn. Back up real databases before an independently approved deployment/migration. No commit or push was performed.

## File manifest

- Added: `Framework/Intersect.Framework.Core/GameObjects/Events/Commands/HealPlayerMonstersCommand.cs`
- Added: `Framework/Intersect.Framework.Core/GameObjects/Monsters/MonsterHealing.cs`
- Added: `Intersect.Editor/Forms/Editors/Events/CommandPrinter.MonsterHealing.cs`
- Added: `Intersect.Editor/Forms/Editors/Events/Event Commands/EventCommand_HealPlayerMonsters.cs`
- Added: `Intersect.Editor/Localization/Strings.MonsterHealing.cs`
- Added: `Intersect.Server.Core/Database/PlayerData/Players/MonsterInstance.Condition.cs`
- Added: `Intersect.Server.Core/Entities/Events/CommandProcessing.MonsterHealing.cs`
- Added: `Intersect.Server.Core/Localization/Strings.MonsterHealing.cs`
- Added: `Intersect.Server.Core/Migrations/Sqlite/Player/20261110000000_AddMonsterCondition.Designer.cs`
- Added: `Intersect.Server.Core/Migrations/Sqlite/Player/20261110000000_AddMonsterCondition.cs`
- Added: `Intersect.Server.Core/Monsters/MonsterHealingEvents.cs`
- Added: `Intersect.Server.Core/Monsters/MonsterOwnershipService.Condition.cs`
- Added: `Intersect.Server/Core/Commands/HealMonsterCommand.cs`
- Added: `Intersect.Server/Migrations/MySql/Player/20261110000000_AddMonsterCondition.Designer.cs`
- Added: `Intersect.Server/Migrations/MySql/Player/20261110000000_AddMonsterCondition.cs`
- Added: `Intersect.Tests.Editor/MonsterHealingEventEditorTests.cs`
- Added: `Intersect.Tests.Server/GameObjects/MonsterConditionTests.cs`
- Added: `docs/monster-condition.md`
- Modified: `Framework/Intersect.Framework.Core/GameObjects/Events/EventCommandType.cs`
- Modified: `Framework/Intersect.Framework.Core/GameObjects/Monsters/OwnedMonsterDetails.cs`
- Modified: `Intersect (Core)/SharedConstants.cs`
- Modified: `Intersect.Client.Core/Interface/Game/Monsters/MonsterRosterWindow.cs`
- Modified: `Intersect.Editor/Forms/Editors/Events/frmEvent.Designer.cs`
- Modified: `Intersect.Editor/Forms/Editors/Events/frmEvent.cs`
- Modified: `Intersect.Editor/Localization/Strings.cs`
- Modified: `Intersect.Server.Core/Database/PlayerData/PlayerContext.Monsters.cs`
- Modified: `Intersect.Server.Core/Database/PlayerData/PlayerContext.cs`
- Modified: `Intersect.Server.Core/Database/PlayerData/Players/MonsterInstance.cs`
- Modified: `Intersect.Server.Core/Localization/CommandsNamespace.Monsters.cs`
- Modified: `Intersect.Server.Core/Migrations/Sqlite/Player/SqlitePlayerContextModelSnapshot.cs`
- Modified: `Intersect.Server.Core/Monsters/Battles/MonsterBattleCombatant.cs`
- Modified: `Intersect.Server.Core/Monsters/Battles/MonsterBattleRegistry.cs`
- Modified: `Intersect.Server.Core/Monsters/Battles/MonsterBattleSession.cs`
- Modified: `Intersect.Server.Core/Monsters/Battles/MonsterPracticeBattles.cs`
- Modified: `Intersect.Server.Core/Monsters/MonsterDiagnostics.cs`
- Modified: `Intersect.Server.Core/Monsters/MonsterOwnershipService.cs`
- Modified: `Intersect.Server.Core/Monsters/MonsterRosterDetails.cs`
- Modified: `Intersect.Server/Core/Commands/StartMonsterBattleCommand.cs`
- Modified: `Intersect.Server/Core/ConsoleService.ConsoleThread.cs`
- Modified: `Intersect.Server/Migrations/MySql/Player/MySqlPlayerContextModelSnapshot.cs`
- Modified: `Intersect.Tests.Client/Monsters/MonsterRosterLayoutTests.cs`
- Modified: `Intersect.Tests.Server/GameObjects/MonsterBattleTests.cs`
- Modified: `Intersect.Tests.Server/GameObjects/MonsterIndividualTests.cs`
- Modified: `Intersect.Tests.Server/GameObjects/MonsterOwnershipTests.cs`
- Modified: `docs/monster-battle-presentation.md`
- Modified: `docs/monster-battles.md`
