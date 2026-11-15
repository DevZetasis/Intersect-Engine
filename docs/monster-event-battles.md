# Milestone 6I-C — Event-based monster battles

## Launch the isolated Windows world

Start the server from PowerShell. The REST API is disabled for this launch so the disposable server can start without Windows DPAPI or Event Log access.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ic-live-55972/server'
$env:Api__Enabled = 'false'
& '.\Intersect Server.exe' --no-upnp --no-port-check --migrate-automatically
```

Wait for **Server started. Using UDP Port #55972.** Open a second PowerShell window for the client.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ic-live-55972/client'
& '.\Intersect Client.exe'
```

Use **New Account** to create **m6ictester** with a password of your choice and an email such as `m6ictester@example.invalid`. Create **M6ICTrainer** using the default class. The character starts at (2,2) on **Route 1 — Painted Grass**.

Grant a strong party lead after you enter the map. Record its individual UUID if you need `healmonster <individual-UUID> all`. The healer at (3,2) also restores the party.

```text
grantmonster "M6ICTrainer" 6b000000-0000-4000-8000-000000000205 6b1c0000-0000-4000-8000-000000000031
```

An optional alternate grant supplies a smaller party member for the loss test. Store the stronger lead first if you want the battle to end when this monster faints.

```text
grantmonster "M6ICTrainer" 6f000000-0000-4000-8000-000000000001 6b1c0000-0000-4000-8000-000000000032
```

Grant editor access in the disposable server console with `poweracc m6ictester 2`, then log out of the client. Start the matching editor from the disposable client directory so it uses that world's assets and connection settings.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ic-live-55972/client'
& 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ic-build/editor/Intersect Editor.exe'
```

## Create a visible Mossling event

1. In the Map Editor, open **Route 1 — Painted Grass**, select **Events**, and double-click an empty safe tile such as (5,1). Name the event **Mossling Trial**. Keep **Global** unchecked for this example.
2. On page 1, choose the **Action Button** trigger. Open the event graphic selector and choose the entity sprite `Mobs_Sm_Slime.png`. Leave movement stationary. Turn off walk-through to make the event solid, and enable interaction freeze if you later give the event a movement route.
3. In the command list, select its empty row and click **Insert**. Choose **Player Control → Start Monster Battle**, pick **Mossling** and set the opponent to level **1**. Save the command. The command list shows the species and level, followed by three indented branches with an insertion row under each.
4. Under **On Victory**, insert **Show Text** with a victory message. Under **On Defeat**, insert **Show Text** with a defeat message and then **Warp Player** to Route 1 at (2,2). This uses the existing map warp to the authored respawn tile. Under **On Escape**, insert **Show Text** with an escape message. Leave the event eligible to interact again during this first test.
5. Save the event, then save the map. Reopen it to check species, level, and branches. You can copy and paste the command to a different event or page and edit its branches independently. If the species is deleted, the editor shows the missing reference and the server refuses the battle.

## Test the results

- **Victory.** Interact with your level-1 event or the seeded **Trial Mossling** at (6,2). Use Astral Mossling's **Sparring Strike**. Confirm Victory, return to the map, and check that the authored victory dialog runs once. The console command `inspectwild M6ICTrainer` shows the generated opponent while the battle is active.
- **Defeat.** Heal your party as needed. Approach **Strong Mossling** at (8,2), which uses level 40. Use the smaller party member by itself to make defeat easier. Confirm Defeat, dismiss the authored dialog, and check that the event warps you to (2,2). Heal before making another attempt.
- **Escape and repeat.** Use **Run** from the battle command menu. After returning to the map, confirm the escape dialog and interact again. A new battle should start. Forfeit and server-side cancellation end the script without running an outcome branch. To verify disconnect cleanup, disconnect mid-battle, reconnect, and start a new attempt.
- **Player-specific disappearance and persistence.** In the editor, create a **Boolean Player Variable** named `MosslingTrialWon`. Under Victory, add **Set Variable** for that player's `MosslingTrialWon = True` after the victory dialog. Keep party synchronization off. Add a higher-priority page conditioned on that variable being true, with no graphic, walk-through enabled, and no commands. Save the event and map. Win, then check that the event disappears for the winner. Log out and back in, and restart the disposable server to verify that the winner's page choice persists. Use a second account to confirm that the event is still visible and can start an independent battle. Put the variable change at the end of Victory because changing page conditions can end the current page's command list.
- **Temporary disappearance.** For a separate event, set a self switch at the end of Victory. Condition a higher, blank, passable **Autorun** page on that switch. Put **Wait** followed by clearing that self switch on the higher page. This uses ordinary event timing for the current event instance. For repeat battles, leave the interaction page eligible.
- **Random encounters.** Leave the event path and enter the green painted region at (3,4). Check that the ordinary route table starts a wild battle. Finish it and verify that the next two eligible steps are skipped. For whole-map regression, enter the warp at (9,3) and test the safe entrance and the default tiles beyond x=4, starting at (5,2). See the [random-encounter runbook](<C:/Users/calip/Documents/GitHub/Intersect-Engine/docs/monster-encounters.md>) for the full map layout.

## Lifecycle and compatibility

The command uses Intersect's existing per-player event stack and stores its branch UUIDs in the event page JSON alongside other commands. The event starts the same server-authoritative wild session as random encounters and keeps waiting on that session's terminal result. The registry reports a result after turn persistence and publication. Victory, Defeat, and Run map to their matching branches. Draw, forfeit, disconnect, cancellation, failed delivery, and failed persistence abort the current event script and release that event's hold. A page that disappears or an event that is removed cancels only its own still-active battle. The event never writes location, completion flags, or a respawn destination on its own.

Event authors control rewards, dialogue, disappearance, and repeat availability. An Autorun page can start another battle if its conditions still match after a result. Use a page condition or an authored wait if you want a one-time or delayed trigger. Encounters require a healthy party monster with a usable move. Go To Label cannot jump into an outcome branch.

Event data uses the existing schema. Matching applications use the `monster-battles-v10` handshake marker.

## Build and verification

The Windows Debug builds succeeded with zero errors. MSBuild reported 359 server warnings, 1,272 client warnings, and 1,114 editor warnings.

The focused server harness passed 52 checks, including all five event-battle tests, existing battle and random encounter regressions, and a SQLite upgrade check. The client passed 31 checks. The editor passed 18 checks, including species and level validation and branch rendering. The disposable SQLite data was reopened to verify the two event commands, their species and levels, and all six branch lists. The seeded server reloaded two maps and three events, reached UDP 55972, and shut down cleanly. Live player interaction and two-client UI checks remain manual.

[Build, test and seed logs](<C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ic-build/logs>)

## Changed files

- [StartMonsterBattleCommand.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Framework/Intersect.Framework.Core/GameObjects/Events/Commands/StartMonsterBattleCommand.cs>)
- [EventCommandType.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Framework/Intersect.Framework.Core/GameObjects/Events/EventCommandType.cs>)
- [SharedConstants.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect (Core)/SharedConstants.cs>)
- [CommandPrinter.MonsterBattle.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Editor/Forms/Editors/Events/CommandPrinter.MonsterBattle.cs>)
- [CommandPrinter.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Editor/Forms/Editors/Events/CommandPrinter.cs>)
- [EventCommand_StartMonsterBattle.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Editor/Forms/Editors/Events/Event Commands/EventCommand_StartMonsterBattle.cs>)
- [frmEvent.Designer.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Editor/Forms/Editors/Events/frmEvent.Designer.cs>)
- [frmEvent.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Editor/Forms/Editors/Events/frmEvent.cs>)
- [Strings.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Editor/Localization/Strings.cs>)
- [CommandInstance.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Server.Core/Entities/Events/CommandInstance.cs>)
- [CommandProcessing.MonsterBattle.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Server.Core/Entities/Events/CommandProcessing.MonsterBattle.cs>)
- [Event.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Server.Core/Entities/Events/Event.cs>)
- [Player.Monsters.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Server.Core/Entities/Player.Monsters.cs>)
- [Player.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Server.Core/Entities/Player.cs>)
- [MonsterBattleRegistry.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Server.Core/Monsters/Battles/MonsterBattleRegistry.cs>)
- [MonsterPracticeBattles.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Server.Core/Monsters/Battles/MonsterPracticeBattles.cs>)
- [MonsterHealingEventEditorTests.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Tests.Editor/MonsterHealingEventEditorTests.cs>)
- [MonsterBattleTests.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Tests.Server/GameObjects/MonsterBattleTests.cs>)
- [MonsterEventBattleTests.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Tests.Server/GameObjects/MonsterEventBattleTests.cs>)
- [MonsterOwnershipTests.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Tests.Server/GameObjects/MonsterOwnershipTests.cs>)
- [monster-event-battles.md](<C:/Users/calip/Documents/GitHub/Intersect-Engine/docs/monster-event-battles.md>)
