# Milestone 6F — Monster switching and full party battles

A practice battle reserves the authenticated character's persisted party in slot order, up to the configured capacity (default six, supported range 1–64). The console-selected starting monster must be an owned party member with positive HP and an executable move. The session keeps one active monster per side and a separate state for each permanent individual UUID. Snapshots use the actual level, calculated stats, assigned moves and stored HP/PP. The practice opponent still has one monster.

A voluntary switch uses the entire player action. The practice provider chooses from its legal moves using the pre-action view. The outgoing monster withdraws and the opponent attacks the incoming one. Neither player's monster spends PP on the switch. If the active monster faints before its move, that move neither executes nor spends PP. A healthy reserve can replace it after the faint animation. Replacement changes the active UUID and revision between turns, with no AI choice, extra attack or PP cost. The player can forfeit during replacement.

The session distinguishes commands, turn resolution, required replacement, completion and cancellation/forfeit. An opponent knockout ends the practice encounter in victory if any player member is alive. Defeat requires the entire party to faint. The result is a draw if both sides run out of living members, either side has no executable PP across its living party, or the turn cap is reached. With the current damage rules, simultaneous knockouts cannot be produced by an ordinary attack. The outcome classifier still handles them if later rules produce them. A healthy monster with no executable PP can enter while another party member still has a usable move.

Each resolved turn collects only the owned individuals whose HP or PP changed. The server checks their ownership, placement and expected database revisions, then writes their conditions in one EF transaction before publishing. A single transaction revision identifies all rows in that commit. After an ambiguous connection failure the server verifies every expected row and value. If the write cannot be confirmed, the session rolls back its in-memory turn checkpoint and releases its reservations. A delivery failure does not undo a committed turn. Switching and forced replacement preserve each monster's condition. The battle reservation covers active, benched and fainted party members, so the same service-level checks reject incompatible placement, healing, EV and move changes until cleanup.

Battle actions carry a typed command, battle ID, turn and revision, plus the target UUID for switching. The existing MessagePack keys remain in place, with new action keys 5–6, snapshot keys 14–15 and presentation key 9. The owner-only snapshot includes party slots, identity, appearance, level and health. Bench entries omit IVs, EVs, nature and moves. The existing active player move view includes usable slots and PP. The fork handshake uses `monster-battles-v4`. Use matching server, client and editor builds.

The battle's Monsters menu shows scrollable party rows with sprite, slot, name, level, HP bar, active and fainted state. During replacement, the party picker opens after the faint sequence, and Back and Fight are unavailable. Forfeit remains available. The client locks submissions while awaiting the server. It animates withdrawal with the outgoing actor still visible, changes the displayed actor at SendOut and plays subsequent damage against that actor. It then uses the new actor's move and PP snapshot. Missing battle art uses the existing idle sprite fallback. Saved Gwen layouts affect geometry and style. Server state governs action legality. Bag and Run remain unavailable.

There are no new database migrations. The 6E condition columns and SQLite/MySQL model snapshots continue to apply. The existing healing event and console command use the expanded reservation without changing their interfaces.

## Matching Windows builds

- Server — `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6f-build/server/Intersect Server.exe`
- Client — `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6f-build/client/Intersect Client.exe`
- Editor — `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6f-build/editor/Intersect Editor.exe`

The raw builds above are the matching binaries. For the live checklist use the prepared server and client below, which have disposable resources and localhost settings.

## Disposable Windows test

Use the prepared world at `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6f-live-55966`. It has its own SQLite files, a default map and class, three test species, a practice opponent, and a Monster Healer at tile (1,0) next to spawn. The prepared account and character tables are empty.

Open one PowerShell window for the server and keep its console available.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6f-live-55966/server'
& '.\Intersect Server.exe' --no-upnp --no-port-check --migrate-automatically
```

Wait for UDP port 55966. Open a second PowerShell window for the matching client.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6f-live-55966/client'
& '.\Intersect Client.exe'
```

The client is configured for `127.0.0.1:55966`. Use **New Account**, create a test account, and create **M6FTrainer** with the default class. Enter the map normally. Run the following lines in the server console. Record the returned individual UUIDs as A, B and C, respectively.

```text
grantmonster "M6FTrainer" 6f000000-0000-4000-8000-000000000001 6f000000-0000-4000-8000-000000000011
grantmonster "M6FTrainer" 6f000000-0000-4000-8000-000000000002 6f000000-0000-4000-8000-000000000012
grantmonster "M6FTrainer" 6f000000-0000-4000-8000-000000000003 6f000000-0000-4000-8000-000000000013
```

Those grant Mossling, Emberling and Pebbling. The four seeded species use the custom Practice type. The opponent knows a weak, reliable Leaf Tackle and is faster than the party at comparable levels. Mossling has authored front/back practice art. Emberling and Pebbling use overworld fallback frames. For the commands below replace `<A>`, `<B>` and `<C>` with the grant results.

1. Inspect each monster and record HP, PP and slot. Start the level-1 battle with A.

   ```text
   inspectmonster <A>
   inspectmonster <B>
   inspectmonster <C>
   startmonsterbattle M6FTrainer <A> 6f000000-0000-4000-8000-000000000004 1
   ```

2. At 1280×720, open **Monsters**. Check the three rows, visible HP bars, active indicator, scroll behavior and **Back**. Use Leaf Tackle once with A, then record its HP/PP. Reopen Monsters and switch to B. A should withdraw, B should appear with its own name and max HP, then the attack should land on B. Inspect A and B. A's HP/PP stays at the post-Leaf Tackle values. B loses only the damage shown and spends no PP.
3. Switch back to A. At send-out it should start at the recorded HP. If the opponent hits it, only that attack subtracts HP. Leaf Tackle PP stays at its previous value. Inspect all three individuals to verify the right rows changed. Switch to B again and use **Practice Burst** to finish the gentle battle, then choose **Return to game**. If you need to reset between cases, interact with the healer beside spawn or run `healmonster <A> all`, `healmonster <B> all`, and `healmonster <C> all`.
4. With three healthy members, start the high-level practice opponent. Use ordinary attacks with A until it faints. Compare the move PP immediately before and after the fatal turn. The faster opponent should faint A before A can act, so that turn should leave A's move PP unchanged. Confirm the faint presentation ends before the forced picker appears.

   ```text
   startmonsterbattle M6FTrainer <A> 6f000000-0000-4000-8000-000000000004 40
   inspectmonster <A>
   healmonster <B> all
   ```

   The healing command should be refused during the battle, including while B is benched. Fight and Back should be unavailable. Select B and check that its HP and PP are unchanged through SendOut and that the turn does not advance again until you submit a move or voluntary switch. Allow B to faint, then replace it with C. Defeat should appear only after C faints. Confirm **Return to game**. If a build/rule customization changes the damage, continue until each member faints.
5. Verify fainted starters are rejected by trying `startmonsterbattle M6FTrainer <A> 6f000000-0000-4000-8000-000000000004 1` before healing. Heal A and B, leave C fainted and start again. In Monsters, A is highlighted as active and C is disabled as fainted. Forfeit and confirm Return. Move C to storage from the normal roster, start a new battle and confirm C is absent from the battle picker. Storage, foreign UUIDs and stale or duplicate packet rejection are also covered by automated tests.
6. During a practice turn, spend a move and disconnect. Reconnect and check the roster, then verify another battle can begin. Repeat the disconnect while awaiting a replacement to test that cleanup path. After reconnecting, try a normal party/storage transfer, then return the member to the party for subsequent tests.
7. Spend HP/PP in a battle. Inspect A, B and C, then type `exit` in the disposable server console. Restart with the same PowerShell command, reconnect, and inspect the three UUIDs again. Each should retain its own condition. Confirm the normal roster matches. To test operator cancellation, start another battle, copy the returned battle UUID, then run `cancelmonsterbattle <battle-UUID>` and confirm the reservation releases.
8. Face the Monster Healer at (1,0) and interact after battle. Confirm the recovery message and full HP/PP for party members. Stored members should keep their previous condition. The console commands `healmonster <A> hp`, `healmonster <A> pp`, and `healmonster <A> all` allow independent restoration checks.

## Verification and limitations

The final server, client and editor builds succeeded with zero errors. The build logs reported 359 server warnings, 466 client warnings and 1,118 editor warnings. Diagnostics include dependency audit, nullable reference and Windows API analyzer warnings. The executable builds have matching protocol and packet assembly hashes.

The broad server regression run passed 167 checks with zero failures. After extracting and testing simultaneous knockout classification, the final battle and condition run passed 49 checks with zero failures, including overlapping regression cases. The client passed 24 checks, including party navigation and saved Gwen layout tests at six viewport sizes from 800×600 to 1920×1080. The editor passed 14 checks, including the existing healing event editor. The server fixtures include ownership/slot authorization, stale actions, switch ordering, last-monster defeat, simultaneous knockout classification, forced-replacement disconnect, and a disposable SQLite transaction failure on the second write. The existing MySQL checks inspect the model and generated migration SQL without a live MySQL connection.

The prepared server booted on UDP 55966 against new SQLite files, loaded the seeded content, and shut down cleanly. The hardware statistics warning used the existing runtime memory fallback. No player records were seeded.

Live manual play remains on the checklist. The practice flow still has one controller and one active monster per side. Future NPC and PvP work can build on the stable party and active-slot model. Simultaneous combatants, additional opponent parties, items, trapping, escape, capture, encounters, rewards and special switching moves are outside this milestone.

## Source changes

- Modified `Framework/Intersect.Framework.Core/Network/Packets/Client/MonsterBattleActionPacket.cs`
- Modified `Framework/Intersect.Framework.Core/Network/Packets/Server/MonsterBattlePresentationEvent.cs`
- Modified `Framework/Intersect.Framework.Core/Network/Packets/Server/MonsterBattleStatePacket.cs`
- Modified `Intersect (Core)/SharedConstants.cs`
- Modified `Intersect.Client.Core/Interface/Game/Monsters/MonsterBattlePlayback.cs`
- Modified `Intersect.Client.Core/Interface/Game/Monsters/MonsterBattleWindow.cs`
- Modified `Intersect.Client.Core/Localization/Strings.MonsterBattle.cs`
- Modified `Intersect.Server.Core/Monsters/Battles/MonsterBattleCombatant.cs`
- Modified `Intersect.Server.Core/Monsters/Battles/MonsterBattleRegistry.cs`
- Modified `Intersect.Server.Core/Monsters/Battles/MonsterBattleSession.cs`
- Modified `Intersect.Server.Core/Monsters/Battles/MonsterPracticeBattles.cs`
- Modified `Intersect.Server.Core/Monsters/MonsterOwnershipService.Condition.cs`
- Modified `Intersect.Server.Core/Monsters/MonsterOwnershipService.cs`
- Modified `Intersect.Tests.Client/Monsters/MonsterBattlePresentationTests.cs`
- Modified `Intersect.Tests.Client/Monsters/MonsterRosterLayoutTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterBattleTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterConditionTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterOwnershipTests.cs`
- Added `docs/monster-party-battles.md`
