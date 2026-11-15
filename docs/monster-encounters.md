# Milestone 6I-B â€” Random wild encounters

## Launch the disposable world

The isolated SQLite world uses UDP **55971**. Start its server from one PowerShell window. The environment variable disables the optional REST API for this process so it can run in the test environment without Windows DPAPI and Event Log access.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ib-live-55971/server'
$env:Api__Enabled = 'false'
& '.\Intersect Server.exe' --no-upnp --no-port-check --migrate-automatically
```

Wait for **Server started. Using UDP Port #55971.** Launch the matching client from a separate PowerShell window.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ib-live-55971/client'
& '.\Intersect Client.exe'
```

Use **New Account** to create a disposable account named **m6ibtester**, with a password of your choice and an email such as `m6ibtester@example.invalid`. Create **M6IBTrainer** using the default class and enter the map. A fresh character can walk everywhere accessible before receiving a party.

For editor access, enter `poweracc m6ibtester 2` in the disposable server console, log out of the client, then launch the editor from a separate PowerShell window. The working directory supplies that world's client assets and connection settings.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ib-live-55971/client'
& 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ib-build/editor/Intersect Editor.exe'
```

### Grant the test party

Run these lines in the new server's console. The two grants have distinct request UUIDs. Keep the returned individual UUIDs for `inspectmonster <individual-UUID>` and `healmonster <individual-UUID> all`.

```text
grantmonster "M6IBTrainer" 6f000000-0000-4000-8000-000000000001 6b190000-0000-4000-8000-000000000031
grantmonster "M6IBTrainer" 6b000000-0000-4000-8000-000000000205 6b190000-0000-4000-8000-000000000032
```

| Species | UUID | Test use |
|---|---|---|
| Mossling | `6f000000-0000-4000-8000-000000000001` | Party lead and painted route opponent, levels 2â€“4 |
| Emberling | `6f000000-0000-4000-8000-000000000002` | Painted route opponent, levels 3â€“5. Whole-map opponent, levels 3â€“4 |
| Pebbling | `6f000000-0000-4000-8000-000000000003` | Painted cave override, levels 4â€“6, always Genderless |
| Astral Mossling | `6b000000-0000-4000-8000-000000000205` | Second party member with Apply Poison, Clear Condition and Sparring Strike |

### Walk the two maps

Coordinates are zero-based. The party healer is east of the spawn at (3,2).

| Map and area | Location | Expected result |
|---|---|---|
| Route 1 â€” Painted Grass | Spawn (2,2), path (2,3) and (3,3) | Safe. Stay still or face another way to verify there is no roll |
| Route 1 block | (2,4) | A standard Block under an encounter overlay. Walking south from (2,3) cannot enter it |
| Route 1 grass | Green rectangle x=2â€“8, y=4â€“8 | Enter at (3,4). Painted Grass table picks Mossling 60% or Emberling 40% |
| Route 1 warp | (9,3) | From the safe path move east along y=3. The warp shares a painted assignment and takes you to Cave Path (2,2) |
| Cave Path â€” Whole Map | Tan area x=0â€“4, y=0â€“4 | Explicit No Encounter. The warp arrival is here |
| Cave default | Outside the safe area and pocket, for example (5,2) | Default table produces Emberling |
| Cave pocket | x=8â€“12, y=2â€“7 | Painted table takes precedence and produces Pebbling |
| Cave return warp | (0,1) | Returns to Route 1 (9,2) |

Each table is initially at 100% rate with a two-step cooldown. Open the first wild battle and run `inspectwild M6IBTrainer` while it is active. The console reports the encounter table, individual UUID, species, level, IVs, Nature, gender and moves. Repeat after a turn to verify that the individual and its generated attributes stay the same. Use the battle panel to check level, gender and HP. Compare two distinct encounters using their UUIDs and IVs.

Use **Fight** and **Monsters** to check normal actions and switching. **Run** on the main command menu always escapes immediately without taking a turn or spending HP/PP. **Forfeit** is available during forced replacement. Check the result, click **Return to game**, and stay on the tile. No battle starts until a new eligible step. Walk two eligible tiles and confirm they are skipped. The third eligible tile can start another battle. A safe tile, disabled table, or 0% tile does not spend cooldown. Crossing into another map or changing encounter tables keeps the remaining steps while the same character is in play.

Use the healer or the console command when your party runs low on HP or PP. Try a damaging move and a self-status move, then inspect the roster and begin another encounter. Check that HP, PP and persistent conditions carry over and battle-only conditions clear. Compare the party/storage count before and after encounters. To test loss, enter against the cave pocket with only an exhausted or low-HP Mossling still usable. To test victory, heal both party members and use damaging moves. Log out during a battle and confirm `inspectwild` no longer finds it. Reconnect and take new eligible steps.

### Editor workflow

1. Open **Game Editors â†’ Monster Encounter Table Editor**. Adjust species, ranges and weights. The **Chance** column shows each entry's proportion of the total configured weight. Disabled tables are labeled in the list. Save and reopen to check persistence. **Duplicate Table** copies a saved table into an independent entry named with a `(copy)` suffix. **Copy**, **Paste** and **Undo** work with drafts. Delete only unused tables. Release all map and painted references before deleting a table, including assignments on disabled maps.
2. Open a map. Use **Attributes â†’ Paint Encounters** or **Map Properties â†’ Monster Encounters**. Choose **Disabled**, **Painted only** or **Whole map**. For Whole map, choose a default table. Use `-1` in either rate field to inherit the assigned table's rate.
3. Select the **Encounter table** brush, table and optional tile-specific rate, then click or drag over the grid. **No Encounter** paints blue safety overrides. **Clear override** or right-drag removes an assignment. Orange marks a specific table. Hover for coordinates and table name. Click **Apply**, then save the map using the editor's regular map save command. Reopen to verify the assignments and standard Block and Warp behavior.
4. Change a reused table and save. Verify that future encounters on every referencing map use the new settings. A battle already in progress keeps its original opponent. Save the map and table changes, type `exit` in the disposable server console, wait for shutdown, and restart with the launch command above. Reconnect to the same world and repeat the painted and whole-map checks.

For two clients, open two PowerShell windows in the disposable client directory and launch the same executable in each. Log in using distinct accounts. Create **M6IBTrainerTwo** on the second account. If the clients share window settings, use windowed mode and move the windows apart.

```text
grantmonster "M6IBTrainerTwo" 6f000000-0000-4000-8000-000000000001 6b190000-0000-4000-8000-000000000033
```

Walk both trainers into the painted grass. Compare the two results of `inspectwild M6IBTrainer` and `inspectwild M6IBTrainerTwo`. Run or disconnect one trainer, and confirm the other still has the same battle and its own cooldown.

## Implementation notes

- The movement packet handler checks for a completed one-tile server move with matching coordinates. It excludes same-position packets, blocked movement, connected-map crossings, warps, slides, event routes and active interactions. A diagonal move counts once. The shared registry serializes admission and publishes only to the owner. A trainer needs a non-fainted party member with a usable move. An opponent with no usable moves passes its action.
- The server rolls rate, relative entry weight, and inclusive level separately. It then generates IVs, Nature, gender and level-appropriate moves once, and initializes the opponent's stats, HP and PP through the existing battle path. The battle holds the generated UUID and attributes in memory. Only the trainer's party is persisted. Cooldown is in-memory character state, so it does not survive a fresh character load or server restart.
- Both game providers add `MonsterEncounterTables` and nullable `Maps.Encounters` in migration `20261114000000_AddMonsterEncounters`. The encounter overlay is JSON independently of the existing tile and attribute blobs. Legacy maps load with encounters disabled. Table and species references are protected on delete. Network content type 25, a duplicate source key, and a wild battle flag extend the existing protocol. Use builds with `monster-battles-v9`.
- Important implementation files are [MapEncounters.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Framework/Intersect.Framework.Core/GameObjects/Maps/MapEncounters.cs>), [MonsterEncounterTableDescriptor.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Framework/Intersect.Framework.Core/GameObjects/Monsters/MonsterEncounterTableDescriptor.cs>), [Player.Monsters.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Server.Core/Entities/Player.Monsters.cs>), [MonsterPracticeBattles.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Server.Core/Monsters/Battles/MonsterPracticeBattles.cs>), [frmMonsterEncounterTable.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Editor/Forms/Editors/frmMonsterEncounterTable.cs>) and [MapEncounterEditor.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Editor/Forms/DockingElements/MapEncounterEditor.cs>).

## Verification

The final Windows Debug builds reported 292 server warnings, 1,272 client warnings and 1,114 editor warnings, with zero errors for each. Shared protocol assemblies match across all three builds. The copied server and client binaries match their build output.

The focused server run passed 47 checks with zero failures. The client passed 31 checks, and the editor passed 17. The migration checks include real SQLite upgrades using disposable databases and offline MySQL model parity. Two SQLite migration tests in the initial broad run shared an incomplete synthetic legacy map fixture. Supplying its light-color JSON corrected the fixture and both tests passed on rerun.

The final seeded server loaded two maps, reached UDP 55971, recognized the read-only `inspectwild` command, and shut down cleanly. After shutdown, both map overlays and all three encounter tables remained in SQLite. Interactive player, map editor, and two-client walkthroughs remain pending.

The initial broad server run recorded 193 passes and the two fixture failures above. The 47-check focused rerun passed both corrected cases and covered the new no-move wild victory case.

[Build and test logs](<C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ib-build/logs>)
