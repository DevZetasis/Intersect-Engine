# First practice monster battles (Milestone 6C)

Milestone 6E supersedes the temporary owned HP/PP policy described here. See [persistent monster condition](monster-condition.md) for persistence, healing and matching-build instructions.

> Milestone 6D now provides the battle scene, front/back artwork, menus and ordered event playback. See [the presentation report](monster-battle-presentation.md) for current builds, compatibility, tests and live-test instructions. The Milestone 6C mechanics described below remain in place.

## Architectural decisions

Species learnsets are templates; they are not individual move inventories. OwnedMonsters therefore receives four nullable move UUID columns and a MovesInitialized marker. New grants assign the four most recently eligible distinct moves (level descending, UUID ascending to select ties, then level ascending/UUID ascending for slot order). Existing rows initialize once from the current learnset on first roster/battle access. An initialized empty inventory is intentional and is never rerolled. Persistent references protect moves assigned to offline owners.

Move MaximumPP defaults to 20 and accepts 1..999. Remaining PP and HP belong only to a battle. Every session copies its species-derived stats, moves, types, PP limits and rules at creation; editor changes cannot alter an active turn. Current PP starts full on every practice battle. There is no persistent HP/PP, healing or reward writeback.

The turn resolver is independent of map entities and original real-time combat. It uses the existing stat, hit, critical, ordering and damage services. Sides and action providers are explicit extension points; this milestone supports exactly one active monster on each of two sides. The practice provider sees its own legal choices and public opponent state, never the player's pending decision. Only server operators can start/cancel practice sessions.

Only Physical/Special moves targeting OneOpponent execute. Other targets and Status effects are displayed as unsupported and disabled. Start requires a usable damaging move on both sides. If either side exhausts executable PP without a faint, the battle ends as a draw; a 200-turn limit also handles mutual immunity or repeated misses. This deliberately avoids inventing partial Struggle mechanics.

Submissions identify battle, turn, revision and own move slot, not damage/HP/actor/target. Server-side serialized resolution prevents duplicate turns. Disconnect forfeits immediately; shutdown/operator cancellation clears sessions. Reconnection does not restore a practice battle. Roster moves are blocked while the player has an active practice session. Later authorized attribute changes must honor session snapshots rather than change active battle values.

## Persistence and compatibility

Both SQLite and MySQL receive additive migrations:

- Player: `20261109000000_AddMonsterMoveSlots` adds nullable `Move1Id` through `Move4Id`, four reference-lookup indexes, and `MovesInitialized` (default false) to OwnedMonsters.
- Game: `20261109000100_AddMonsterMovePP` adds integer MaximumPP to MonsterMoves with database default 20. Existing move JSON without this property also loads with 20, including when reusing an editor draft.

Initial move selection first restricts entries to the current level and existing move UUIDs, groups repeated UUIDs at their most recent eligible level, selects four by descending level/ascending UUID, then orders the chosen slots by ascending level/ascending UUID. Empty slots are null. Validation rejects duplicate UUIDs, empty UUIDs and inventories of the wrong size. No client-facing assignment/relearning API is introduced.

Existing monsters initialize once when the roster/battle service first accesses them, using current content at that time. The marker persists even for an empty inventory. Adding a new learnset later does not overwrite that saved empty choice: for initial testing, create the learnset **before granting a new individual**. Compatibility initialization preserves the original monster UUID, owner, IVs, EVs, NatureId, level, experience, nickname, placement and placement revision. Normal metadata saves and idempotent grant retries do not reroll moves. `inspectmonster` remains read-only and reports either saved move UUIDs or pending initialization.

Move-reference checks query persistent player data across all four slots, including offline owners. The server's content context, core deletion method and editor deletion handler enforce protection in addition to existing species-learnset references. There is no cross-database SQL foreign key; direct manual SQL edits can bypass application guards. Missing assigned content prevents battle creation rather than inventing a replacement move.

The existing fork compatibility marker appends `|monster-battles-v1`. Existing marker segments, enum values and MessagePack keys are retained. Older clients/editors are intentionally rejected: they do not know the new battle packet types or PP setting and could lose configuration. Use matching builds for all three applications.

## Session and provider architecture

`MonsterBattleCombatant` contains frozen calculated stats/type IDs, copied move/type definitions and independent in-memory HP/PP. Sessions deep-copy combatants again so caller-owned objects cannot alias another session. `MonsterBattleSide` represents controller identity and active slots. This first resolver deliberately validates one active monster per side; future 2v2 needs additional action/target collection, not another damage engine. PvP needs a second human action source and synchronization; it is not already implemented.

`IMonsterBattleDecisionProvider` receives legal own slots, own public state and public opponent state. `RandomPracticeDecisionProvider` chooses uniformly among executable nonempty slots with remaining PP. Its context does not include the human's selected action. Different future providers can supply legal decisions to the same resolver. Opponents have zero EVs, server-generated IVs and a weighted custom nature, and use the same formulas as the player. The console explicitly sets their level; there are no extra hidden stat multipliers.

`MonsterBattleRegistry` serializes lifecycle, reservations and actions under its coordinator lock; each session also protects its state. One active practice session per character is allowed. Starting requires an online, in-game, alive character outside existing shop/bank/trade/crafting/request interactions, the character's own party monster, valid definitions and executable moves on both sides. Publication failure, provider/calculation failure, forfeit, disconnect, operator cancellation and shutdown release reservations. Practice HP, PP, outcomes and rewards are never saved to the player database.

The prototype coordinator serializes all practice turns. Future high-volume hosting can move to per-session coordination with a separate reservation map while preserving ordering and publication guarantees. Original Intersect map movement/combat systems are not disabled; this milestone does not implement a full overworld battle lock or safe arena. Use a safe empty map for tests.

## Turn resolution

1. Validate authenticated ownership, battle UUID, turn, revision and selected slot/remaining PP. A forfeit ends the owner's session; clients cannot select an opponent actor or supply HP, damage, RNG results or order.
2. Ask the practice provider for one legal slot without revealing the player's choice.
3. Call the existing priority/Speed ordering calculator, including unbiased random ties.
4. Skip a fainted actor. An executed move attempt spends one PP even on miss or immunity. An actor fainted before its action spends none.
5. Call the existing accuracy, critical and Physical/Special damage functions. Clamp applied loss to current HP and emit authoritative messages for moves, misses, criticals, effectiveness and fainting.
6. End on victory/defeat, otherwise draw on exhaustion or the 200-turn safety limit. If still active, advance turn and revision. Publish a private snapshot only after resolution.

Invalid/stale submissions do not advance a turn. A matching owner's rejected action receives that owner's current snapshot, without exposing another session. The client disables repeated action submission while awaiting the next state. Server checks remain authoritative regardless of UI disabling.

Supported calculations are the Milestone 5B subset: fixed-power Physical/Special, priority and Speed, 0-stage accuracy checks/AlwaysHits, base critical chance and 2x critical multiplier, STAB, configured damage variance, custom ordered dual-type relationships/immunity and integer flooring. Status execution, other targets, animations, switching, abilities, held items, weather, burn/screens/spread modifiers, rewards, capture and evolution remain deferred. No complete Generation 4 compatibility is claimed.

## Networking and client interface

Two new packets are sufficient:

- Client `MonsterBattleActionPacket`: battle UUID, turn, revision, own move slot, forfeit flag.
- Server `MonsterBattleStatePacket`: recipient player UUID, battle UUID, turn/revision/outcome, two public actor views, four own move views, recent messages and monotonic publication sequence. The same packet covers start, turn result, rejection and end.

The action handler rejects editors and clients without an in-game character. The registry locates sessions through the authenticated character before checking battle ID. State goes only to that character, not maps/observers. Opponent IVs, EVs, nature, persistent ownership and private move inventory are not transmitted. Client handling rejects foreign-character and older-sequence snapshots; new character state starts empty after reconnect. Transport teardown and server forfeiture prevent restoration of an interrupted battle.

`MonsterBattleWindow` uses existing Gwen windows, scrolling, labels, image panels, progress bars, buttons and list boxes. It opens on a server snapshot and shows both names/sprites/levels/current-max HP, four moves with type/category/PP, disabled unavailable moves, an authoritative log, forfeit and return controls. Closing an ended result returns to normal gameplay without reopening on every frame. Hidden/disconnected state clears the view. The existing Monsters details window, HUD correction and item/spell hotbar remain intact.

## Automated verification

All three matching Debug builds completed with zero compilation errors. Final review build logs contain 113 server warnings, 462 client warnings and 1114 editor warnings; warnings remain, and clean rebuild counts can differ.

Targeted cached NUnit harnesses compiled and executed the actual repository test sources:

- 130 server cases passed, zero failures in the final review rerun. Coverage includes normal character creation/lazy slots/reconnect saves, deterministic learnset ordering/limits, PP defaults/legacy reload, Physical/Special reference damage, criticals, misses, STAB/dual types/immunity/variance, priority/Speed/ties, HP clamping/fainting, exhaustion/turn-limit draws, illegal/stale/foreign/concurrent submissions, session isolation, delivery/provider failure cleanup, console syntax, persistent move choices, legacy initialization, offline move-reference protection, and all existing monster/ownership/player regressions.
- 10 client checks passed, zero failures. The six resolution cases retain the roster/HUD regression assertions and exercise actual Gwen battle controls, HP values/bars, move/PP mapping, disabled Status moves, single submission while awaiting a result, authoritative refresh, foreign/stale packet rejection, completion, closing and logout state.
- 13 editor checks passed, zero failures, including PP control editing alongside existing copy/undo/cancel and all species/type/move/nature regressions.
- A focused follow-up additionally passed both battle packets through the production MessagePacker and built-in type registry, including revision/PP round trips and absence of an unrestricted battle-start packet.

These are targeted harness runs, not every solution test project via `dotnet test`. SQLite upgrade/downgrade/reload tests use GUID-named disposable databases. MySQL target-model agreement and SQL generation were checked without connecting to a MySQL server; real MySQL application remains a manual integration check. The old-schema nature fixture now seeds historical columns directly, avoiding use of a current EF model against a deliberately older table.

Logs are `%TEMP%\monster-m6c-server-tests-complete.log`, `%TEMP%\monster-m6c-client-tests.log`, `%TEMP%\monster-m6c-editor-tests.log`, `%TEMP%\monster-m6c-network-check.log`, and the corresponding `monster-m6c-*-build.log` files.

Final review logs are `%TEMP%\monster-m6c-review-server-tests-final2.log` (130 passes before the UI change), `%TEMP%\monster-m6c-review-rebuilt-server-tests.log` (130 passes against the rebuilt server), `%TEMP%\monster-m6c-review-client-final2.log`, `%TEMP%\monster-m6c-review-editor-final.log`, and `monster-m6c-review-*-build-final*.log`. The final review rebuilt server, client and editor after the battle-layout fix described below. The revised test sources were compiled against those outputs. An initial test-harness teardown hit a pooled SQLite file handle and raised an `Intersect.Tests.Server.exe` error dialog; releasing test-process pools fixed teardown, and the complete subsequent run passed. This harness error was separate from the running game server and client.

The prepared server starts successfully with the new SQLite schema and registered battle packets. In the connected Windows client, the user reported that the initial level-5 practice battle worked. Following the fixture repair described below, the user confirmed losing to a level-40 opponent. The server accepted a subsequent new battle, confirming defeat reservation cleanup. The user then confirmed Forfeit and Return to game worked; querying the forfeited battle through the operator cancellation command returned "Active battle was not found," confirming its removal. Read-only comparisons found no changed fields in the owned monster and the same two-monster collection count. The final review below additionally confirmed a fresh normal character, disconnect/reconnect, opening move availability and an explicit victory. The full resolution/interaction matrix remains a separate manual check; the user reports are not a claim that every item below was exercised.

## Matching builds

Keep each executable's complete output directory and dependencies together:

```text
C:\Users\calip\.codex\visualizations\2026\11\08\01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0\m6c-review-build\server\Intersect Server.exe
C:\Users\calip\.codex\visualizations\2026\11\08\01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0\m6c-review-build\client\Intersect Client.exe
C:\Users\calip\.codex\visualizations\2026\11\08\01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0\m6c-review-build\editor\Intersect Editor.exe
```

## Prepared disposable live world

```text
C:\Users\calip\.codex\visualizations\2026\11\08\01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0\m6c-live-ca2e8b26-3d4d-4389-bd8a-db63c3ef428f
```

This is a new world with independent SQLite game/player/logging databases and copied asset subfolders. Existing development/test databases and configurations were not modified. Client host is `127.0.0.1`, port **55962**. Its separate app settings use that test-server address, preserving settings for other servers.

During the connected test, the disposable character's autosave encountered missing inventory/hotbar rows and the existing concurrency protection shut down the test server. Only this fixture was repaired, after making `playerdata.pre-slot-repair.db`: inventory/spells/hotbar/bank now contain 35/35/10/100 rows, and reopening the account and exercising `User.Save` returned `Completed`. A read-only comparison confirmed every owned-monster field remained unchanged after repair. The final regression investigation below distinguishes the fixture error from normal lazy slot creation.

### Final regression investigation

Empty slot rows need not exist at character creation. `SlotList<TSlot>` creates entries when indexed; `Player.ValidateLists()` validates capacities without filling them. In the normal `CreateCharacterPacket` handler, equipment is explicitly initialized to `-1`. Join-game packets subsequently materialize inventory, spell and hotbar slots, which the first account save inserts. Bank slots can remain absent until bank access. Their initial absence is not evidence of migration damage.

The original external seed script bypassed character creation and left equipment at the constructor's zero values. Character-list equipment lookup then indexed the missing inventory slot zero. Selecting the character attached that unsaved slot in a short-lived relationship-loading context and assigned a UUID without saving it. A later detached account update treated it as an existing row. An isolated reproduction showed a zero slot UUID before selection, a generated UUID after selection, and `SaveChanges()` returning `-1` through the existing concurrency failure handler. The normal creation/save/reconnect control completed successfully. This is an invalid disposable fixture, not a demonstrated normal-player creation defect or a monster migration issue.

The external seed helper now initializes equipment to `-1`, validates capacities, and materializes its slots before its initial save. No production save policy, character-creation code or schema was changed to hide the error. `NormallyCreatedCharacterCanSaveLazySlotsAndMonstersAcrossReconnects` exercises the actual `User.TryAddCharacter` and `User.Save` paths with normal equipment initialization, lazy join-time slots, a monster grant, repeated relationship loads/reconnect saves, persistent slot counts and stable monster identity/placement. Its resource-directory override is in memory, scoped to a GUID-named disposable database and restored afterward; pooled SQLite test handles are released before fixture cleanup.

A second fresh live world was created at `C:\Users\calip\.codex\visualizations\2026\11\08\01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0\m6c-review-live-3f7a9686-cde6-4ed3-8fc2-4fa4e65246f6`, port **55963**. It started with zero accounts/characters and new SQLite databases. Only monster content was seeded. The user created account `m6review` and character `ReviewTrainer` through the real client. Operator grant created individual `52d8e9c2-2e24-4226-9419-ff3c79fff4f0`, and battle `12c719fb-1fa9-4635-97ff-66c0eb61d85d` started without database repairs. The user disconnected during that active battle; the server completed the character save and querying the former battle returned "Active battle was not found." Read-only inspection found normal equipment `[-1,-1,-1,-1,-1]` and 35 inventory, 10 hotbar, 35 spell and zero unused bank rows.


After the active disconnect, the user reconnected and the server accepted a new battle. A loss during that test exposed a real UI defect: saved Gwen layouts contain `Disabled` flags and load on the window's first draw, after the opening snapshot was rendered. Flags saved for another move order could disable a damaging move and visually enable Status until the next snapshot. Server legality checks and the client submission guard still rejected unsupported actions. `MonsterBattleWindow.OnJsonReloaded()` now restores actor labels/HP, move labels/availability and result controls from the current authoritative snapshot, preserving the awaiting-turn lock. It does not append duplicate log messages or resolve actions.

The client regression now feeds deliberately reversed saved button states through the real `LoadJsonUi` callback. It failed against the old assembly, passes with the fix at six resolutions, and checks a reload while awaiting a result. An exact move-label assertion also covers the separator text. The new build initially displayed misencoded separators; using explicit C# Unicode escapes corrected them. That final text-only correction was covered automatically, after the successful live win.

With the corrected layout handling and the same saved configuration, the user confirmed Practice Burst enabled and Status disabled before turn one, won battle `36ab4e87-1a82-4246-be15-d0990ec5836c`, and confirmed Return to game worked. The completed battle was absent from the registry. Read-only comparison after victory found zero changed fields in the persistent owned-monster record and exactly one owned monster.

Source rebuilds also caught malformed UTF-8 BOM prefixes in pending Milestone 6C source/generated model files. Proper BOMs were restored without changing migration operations or schema definitions. Matching outputs are under `m6c-review-build`; the shared core DLL is identical across server/client/editor. The review world's `client-fixed` runtime contains the corrected client DLL while retaining a byte-identical copy of its connection configuration and the existing saved layouts.

Test account: `m6ctester`, password `M6C-Test-2026!`, character `M6CTrainer`. These are synthetic local test credentials. Its party monster is level 10 with four assigned level-1 moves, three damaging and one unsupported Status move. Another individual is in storage. The optional API is disabled only in this scratch world's `appsettings.json` and `appsettings.Production.json`.

To start this scratch server, ensure another copy is not already using port 55962, open a normal console in its **server** directory, and run:

```powershell
& '.\Intersect Server.exe' --no-upnp --no-port-check --migrate-automatically
```

Keep its console/input stream open. A hidden launch with closed standard input can exit, and redirected output can encounter the existing Console.BufferWidth bootstrap limitation. No bootstrap code is changed for this milestone. Type `exit` for normal shutdown.

From the scratch **client** directory:

```powershell
& '.\Intersect Client.exe' --screen-width 1280 --screen-height 720
```

The matching editor can use a separate scratch runtime with the same asset-only resources and connection port. Do not point it at the existing development server for this test.

## Exact console commands

```text
startmonsterbattle "M6CTrainer" 84b2e401-7b38-4c4b-9f29-629078176f5a fb4e77bd-acbd-4bd6-b73b-9d6e8fb05b85 5
cancelmonsterbattle <battle-UUID-printed-by-start>
inspectmonster 84b2e401-7b38-4c4b-9f29-629078176f5a
```

General syntax is `startmonsterbattle <online-character-name-or-UUID> <owned-party-monster-UUID> <opponent-species-UUID> <opponent-level>`. Quoted names are supported. Starting/cancelling is registered only in the server console, not player chat or a public battle-start packet.

## Manual test procedure

1. For another fresh world, create a unique scratch directory and copy complete matching builds. Copy only asset subfolders from a client resource pack; do not import existing databases, server configuration or connection strings. Create fresh server resources/config.json selecting SQLite and an unused server port. Disable its unnecessary optional API in both scratch appsettings files. Start it from a normal console; migrations apply only to the new scratch databases.
2. Create a test account/trainer through normal workflows and log into that scratch server. In the matching editor create a type and several moves: e.g. Physical power 40/PP20/OneOpponent, Special power 60/accuracy90/PP20, a priority attack, and a Status move. Save and reopen to verify Maximum PP persists and its 1..999 limits.
3. Create a species with valid six base stats, optional sprite/types and level-1 learnset entries for those moves. Include an above-level entry to check it is excluded. Save the learnset before granting a new individual.
4. Run `grantmonster "<trainer>" <species-UUID> <new-request-UUID>`. Record the individual UUID and inspect its attributes/move slots. Retry the identical grant request and verify one unchanged individual. Move it to storage and back, then restart/reconnect and inspect again.
5. Start a practice battle using the owned **party** individual and an opponent species/level with usable damaging moves. The battle window must open automatically. Try a stored individual's UUID, another owner's UUID, an offline/busy character, a second simultaneous start, and an opponent with no damaging moves: they must be refused.
6. Select attacks. Verify remaining PP decrements, both HP values/bars change and messages agree with actual results. Status/unsupported targets, empty slots and zero-PP moves must be disabled. Repeated clicks must not resolve an extra turn. No console command supplies the client with damage.
7. In the prepared world, opponent level 5 is an easy victory test for the level-10 owned monster. Opponent level 40 provides a defeat test. After a faint, the fainted actor must not act and the result must be displayed. Return to normal gameplay, then start a new session; HP/PP reset intentionally.
8. Test Forfeit, operator cancellation, logout/disconnect during a battle, and a scratch-server restart. Each must release the active reservation; a subsequent new start must work. Reconnecting does not restore the old practice session.
9. Set all relevant move PP to 1 **before starting a new battle** and use sufficiently durable monsters to test exhaustion. The battle must end as a documented draw. Two mutually immune monsters must eventually reach the 200-turn draw limit. Editor changes during an active battle must not change its snapshotted PP/power/stats.
10. Inspect the owned monster before and after win, loss, forfeit and restart. UUID, ownership, IVs, EVs, nature, level, experience, placement and move choices must remain unchanged. HP and remaining PP must not appear as persistent owned-monster columns.
11. Remove a move from its species learnset, log its owner out, then try to delete the assigned move in the editor: persistent reference protection must still refuse it. Verify existing species/type/move/nature editors, monster details, party/storage and both HUD areas remain functional.
12. Check the battle window at 1280x720, 1366x768, 1600x900 and 1920x1080 where supported. At smaller sizes scroll the content; forfeit/return controls must remain accessible. Long custom names/messages have tooltips. Real Windows display/DPI, sprite rendering, tooltip and interaction checks remain distinct from automated layout tests.

## File manifest

Modified existing files (37):

```text
Framework/Intersect.Framework.Core/GameObjects/Monsters/MonsterMoveDescriptor.cs
Intersect (Core)/SharedConstants.cs
Intersect.Client.Core/Entities/Player.Monsters.cs
Intersect.Client.Core/Interface/Game/GameInterface.cs
Intersect.Client.Core/Networking/PacketHandler.Monsters.cs
Intersect.Editor/Forms/Editors/frmMonsterMove.cs
Intersect.Editor/Localization/Strings.MonsterMoves.cs
Intersect.Server.Core/Core/ServerContext.cs
Intersect.Server.Core/Database/DbInterface.cs
Intersect.Server.Core/Database/GameData/GameContext.Monsters.cs
Intersect.Server.Core/Database/GameData/GameContext.cs
Intersect.Server.Core/Database/PlayerData/PlayerContext.Monsters.cs
Intersect.Server.Core/Database/PlayerData/PlayerContext.cs
Intersect.Server.Core/Database/PlayerData/Players/MonsterInstance.cs
Intersect.Server.Core/Entities/Player.cs
Intersect.Server.Core/Localization/CommandsNamespace.Monsters.cs
Intersect.Server.Core/Migrations/Sqlite/Game/SqliteGameContextModelSnapshot.cs
Intersect.Server.Core/Migrations/Sqlite/Player/SqlitePlayerContextModelSnapshot.cs
Intersect.Server.Core/Monsters/MonsterDiagnostics.cs
Intersect.Server.Core/Monsters/MonsterMoveRules.cs
Intersect.Server.Core/Monsters/MonsterOwnershipService.cs
Intersect.Server.Core/Networking/PacketHandler.Monsters.cs
Intersect.Server/Core/ConsoleService.ConsoleThread.cs
Intersect.Server/Migrations/MySql/Game/MySqlGameContextModelSnapshot.cs
Intersect.Server/Migrations/MySql/Player/MySqlPlayerContextModelSnapshot.cs
Intersect.Server/Networking/NetworkedPacketHandler.cs
Intersect.Tests.Client/Monsters/MonsterRosterLayoutTests.cs
Intersect.Tests.Editor/MonsterMoveEditorTests.cs
Intersect.Tests.Server/GameObjects/MonsterIndividualTests.cs
Intersect.Tests.Server/GameObjects/MonsterMoveTests.cs
Intersect.Tests.Server/GameObjects/MonsterNatureTests.cs
Intersect.Tests.Server/GameObjects/MonsterOwnershipTests.cs
Intersect.Tests.Server/GameObjects/MonsterSpeciesTests.cs
Intersect.Tests.Server/GameObjects/MonsterTypeTests.cs
docs/monster-moves.md
docs/monster-natures.md
docs/monster-stat-calculations.md
```

Added files (21):

```text
Framework/Intersect.Framework.Core/Network/Packets/Client/MonsterBattleActionPacket.cs
Framework/Intersect.Framework.Core/Network/Packets/Server/MonsterBattleStatePacket.cs
Intersect.Client.Core/Interface/Game/Monsters/MonsterBattleWindow.cs
Intersect.Client.Core/Localization/Strings.MonsterBattle.cs
Intersect.Server.Core/Entities/Player.Monsters.cs
Intersect.Server.Core/Migrations/Sqlite/Game/20261109000100_AddMonsterMovePP.Designer.cs
Intersect.Server.Core/Migrations/Sqlite/Game/20261109000100_AddMonsterMovePP.cs
Intersect.Server.Core/Migrations/Sqlite/Player/20261109000000_AddMonsterMoveSlots.Designer.cs
Intersect.Server.Core/Migrations/Sqlite/Player/20261109000000_AddMonsterMoveSlots.cs
Intersect.Server.Core/Monsters/Battles/MonsterBattleCombatant.cs
Intersect.Server.Core/Monsters/Battles/MonsterBattleRegistry.cs
Intersect.Server.Core/Monsters/Battles/MonsterBattleSession.cs
Intersect.Server.Core/Monsters/Battles/MonsterPracticeBattles.cs
Intersect.Server.Core/Monsters/MonsterMoveAssignment.cs
Intersect.Server/Core/Commands/StartMonsterBattleCommand.cs
Intersect.Server/Migrations/MySql/Game/20261109000100_AddMonsterMovePP.Designer.cs
Intersect.Server/Migrations/MySql/Game/20261109000100_AddMonsterMovePP.cs
Intersect.Server/Migrations/MySql/Player/20261109000000_AddMonsterMoveSlots.Designer.cs
Intersect.Server/Migrations/MySql/Player/20261109000000_AddMonsterMoveSlots.cs
Intersect.Tests.Server/GameObjects/MonsterBattleTests.cs
docs/monster-battles.md
```
