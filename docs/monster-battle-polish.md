# Event battle dialogue and Run button verification

## Launch on Windows

Use the disposable world on UDP **55973**. Start the server in a PowerShell window and wait for its port announcement. The optional REST API is disabled for this test process.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ic-polish-live-55973/server'
$env:Api__Enabled = 'false'
& '.\Intersect Server.exe' --no-upnp --no-port-check --migrate-automatically
```

Start the client in a second PowerShell window.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ic-polish-live-55973/client'
& '.\Intersect Client.exe'
```

For optional editor access, use `poweracc polishtester 2` in the disposable server console after creating the account, then log out of the client. Start the matching editor with the disposable client directory as its working directory.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ic-polish-live-55973/client'
& 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ic-polish-build/editor/Intersect Editor.exe'
```

## Verify presentation and Run

1. Create a disposable account named **polishtester** and a character named **PolishTrainer** using the default class. Enter Route 1. Grant the lead below from the server console. The healer east of the spawn at (3,2) restores party HP and PP.
2. Interact with **Trial Mossling** at (6,2). Finish the battle with **Sparring Strike**. Let the final attack, HP decrease, faint, and battle message play. Wait briefly on the result screen and confirm that the event dialogue is still absent. Click **Return to game** and confirm the Victory dialogue appears once. Check the spent move PP in Monsters.
3. Reopen Trial Mossling. Verify the enabled button reads **Run**, then use it. Confirm the Escape result appears before the event dialogue. Close the result and check that the Escape dialogue appears once. Reopen the event to verify it can start again.
4. Interact with **Strong Mossling** at (8,2). For an easier defeat test, grant the optional weaker monster below and store Astral Mossling first. After the last party member faints, verify the Defeat dialogue and scripted warp to (2,2) only happen after the result closes.
5. Walk into the painted grass at (3,4). Verify the random wild encounter also has an enabled **Run** caption and returns to exploration. To compare a practice battle, run `startmonsterbattle PolishTrainer <owned-monster-UUID> 6f000000-0000-4000-8000-000000000004 1` from the console. Practice shows a disabled **Run (unavailable)**.
6. For an isolation check, create a second account with a different character. Keep one account on a finished result screen while the other finishes and closes its own battle. Only the second event should advance. For disconnect coverage, disconnect while a result is open, reconnect, and try the event again. Use the player-variable example in the [event battle runbook](<C:/Users/calip/Documents/GitHub/Intersect-Engine/docs/monster-event-battles.md>) if you also want to check player-specific disappearance.

```text
grantmonster "PolishTrainer" 6b000000-0000-4000-8000-000000000205 6b1c0000-0000-4000-8000-000000000041
grantmonster "PolishTrainer" 6f000000-0000-4000-8000-000000000001 6b1c0000-0000-4000-8000-000000000042
```

The server commits battle effects before publishing the result. The client acknowledges it only after closing the result window. The acknowledgment cannot change the result. If the event is cancelled, the player disconnects, or the acknowledgment never arrives, the registry abandons the branch and releases the event. The missing-acknowledgment safeguard is five minutes. Use matching `monster-battles-v11` builds.

## Verification

Windows Debug builds completed with zero errors. MSBuild reported 359 server warnings, 1,272 client warnings, and 1,118 editor warnings. Automated harnesses passed 54 server checks, 31 client checks, and 18 editor checks. The client tests exercised the real battle controls at six resolutions, including the HP animation, final message order, close acknowledgment, and both Run states. The server tests covered all authored outcomes, stale and duplicate acknowledgments, separate players, cancellation, the timeout, and random wild battles.

The seeded server loaded two maps and three events, listened on UDP 55973, and shut down cleanly. Live client gameplay and the two-account UI walkthrough remain manual checks.

[Build and test logs](<C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ic-polish-build/logs>)

## Changed files

- [MonsterBattleActionPacket.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Framework/Intersect.Framework.Core/Network/Packets/Client/MonsterBattleActionPacket.cs>)
- [MonsterBattleStatePacket.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Framework/Intersect.Framework.Core/Network/Packets/Server/MonsterBattleStatePacket.cs>)
- [SharedConstants.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect (Core)/SharedConstants.cs>)
- [MonsterBattleWindow.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Client.Core/Interface/Game/Monsters/MonsterBattleWindow.cs>)
- [Strings.MonsterBattle.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Client.Core/Localization/Strings.MonsterBattle.cs>)
- [MonsterBattleRegistry.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Server.Core/Monsters/Battles/MonsterBattleRegistry.cs>)
- [MonsterBattleSession.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Server.Core/Monsters/Battles/MonsterBattleSession.cs>)
- [MonsterRosterLayoutTests.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Tests.Client/Monsters/MonsterRosterLayoutTests.cs>)
- [MonsterBattleTests.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Tests.Server/GameObjects/MonsterBattleTests.cs>)
- [MonsterEncounterTests.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Tests.Server/GameObjects/MonsterEncounterTests.cs>)
- [MonsterEventBattleTests.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Tests.Server/GameObjects/MonsterEventBattleTests.cs>)
- [MonsterOwnershipTests.cs](<C:/Users/calip/Documents/GitHub/Intersect-Engine/Intersect.Tests.Server/GameObjects/MonsterOwnershipTests.cs>)
- [monster-battle-polish.md](<C:/Users/calip/Documents/GitHub/Intersect-Engine/docs/monster-battle-polish.md>)
