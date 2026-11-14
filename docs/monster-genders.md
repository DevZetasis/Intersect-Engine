# Milestone 6I-A — Customizable monster genders

The Monster Gender Editor is under **Game Editors**. Definitions have permanent UUIDs, names, optional descriptions and symbols, and folder metadata. The editor uses the existing draft, copy/paste, undo, save, cancel and deletion flow. Paste preserves the destination UUID.

The Species Editor's **Gender Distribution** assigns integer weights from 0 to 100,000. The server rolls once over the sum of positive weights and selects the corresponding UUID bucket. For example, weights 75 and 25 yield a 75% and 25% split. Zero is represented by an omitted entry in the editor. New definitions start at zero for every species. Saving requires at least one positive entry and valid referenced UUIDs. Missing entries can be cleared from the Species Editor. If damaged or incomplete content has no eligible definitions, generation falls back to Unspecified.

The SQLite and MySQL game migrations `20261113000000_AddMonsterGenders` create `MonsterGenders` and add nullable JSON text `MonsterSpecies.GenderWeights`. The migration seeds Male, Female and Genderless with fixed UUIDs. Migration history prevents reseeding after edits. Legacy species without explicit weights default to 50/50 Male and Female. Those two default UUIDs are protected from deletion so legacy and newly created species retain a valid default. Their names, symbols and folders are editable. Other definitions can be deleted once their species and owned references are cleared. The player migration `20261113000000_AddOwnedMonsterGender` adds nullable `OwnedMonsters.GenderId`. Existing individuals remain Unspecified, distinct from a selected Genderless definition.

The ownership service stores the generated UUID with the original grant, and the persistence guard rejects reassignment. Grant retries return the original individual. The server resolves names and symbols for the roster and inspection output. Battle actors retain their metadata through switching, while a generated practice opponent rolls once using its species weights. Missing definitions show Unavailable. The client always presents the name and adds the symbol only when its font supports the glyphs. Gender has no effect on battle calculations.

Gender definitions sync as content type 24. Roster detail keys 11–13 and battle actor keys 10–12 carry UUID, name and symbol. The handshake is `monster-battles-v8`.

## Disposable Windows test

Start the server in PowerShell and wait for UDP 55970.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ia-live-55970/server'
& '.\Intersect Server.exe' --no-upnp --no-port-check --migrate-automatically
```

Start the client in a separate PowerShell window.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ia-live-55970/client'
& '.\Intersect Client.exe'
```

Create a disposable account named **m6iatester** and a character named **M6IATrainer** using the default class. For editor access, run `poweracc m6iatester 2` in this server console and log out of the client before connecting to the editor.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ia-live-55970/client'
& 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ia-build/editor/Intersect Editor.exe'
```

Raw matching build executables are `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ia-build/server/Intersect Server.exe`, `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ia-build/client/Intersect Client.exe`, and `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6ia-build/editor/Intersect Editor.exe`.

Run these grants and record the returned individual UUIDs. The repeated Mossling lines use distinct request UUIDs so each creates a new individual.

```text
grantmonster "M6IATrainer" 6f000000-0000-4000-8000-000000000001 6a190000-0000-4000-8000-000000000011
grantmonster "M6IATrainer" 6f000000-0000-4000-8000-000000000001 6a190000-0000-4000-8000-000000000012
grantmonster "M6IATrainer" 6f000000-0000-4000-8000-000000000002 6a190000-0000-4000-8000-000000000013
grantmonster "M6IATrainer" 6f000000-0000-4000-8000-000000000003 6a190000-0000-4000-8000-000000000014
grantmonster "M6IATrainer" 6b000000-0000-4000-8000-000000000205 6a190000-0000-4000-8000-000000000015
```

1. Use `inspectmonster <individual-UUID>` and the Monsters details panel to compare UUID and gender. Mossling is 50/50, Emberling is 75/25, Pebbling is 100% Genderless, and Astral Mossling is 100% Astral. A small random sample may give the same result twice.
2. Move a monster to storage and back. Re-run the identical grant request and check that it returns the same UUID without moving the monster. Log out, type `exit` in the disposable server console, wait for shutdown, and rerun the server command above. Log in with the same account and character. Compare the UUID, gender and placement in Monsters and with `inspectmonster`.
3. Start a battle using `startmonsterbattle M6IATrainer <individual-UUID> 6f000000-0000-4000-8000-000000000004 1`. Check both active labels and switch to a different party member. For a guaranteed custom gender on the opponent, use species UUID `6b000000-0000-4000-8000-000000000205` in the same command. Astral Mossling knows Apply Poison, Clear Condition and Sparring Strike for checking the existing status and HP/PP flow. The healer is one tile east of the default spawn.
4. In the Gender Editor rename Astral or change its symbol and save. Check that the same UUID displays the new name after refreshing. Create another definition, assign a positive weight to it in a species, and save/reopen. Check that a zero-weight or unconfigured definition never appears. Try deleting the still-referenced Astral definition and confirm the refusal.

## Verification

The matching Windows builds succeeded with zero errors. MSBuild reported 359 server, 1,272 client and 1,118 editor warnings. The client passed 30 checks, including roster layouts from 800×600 to 1920×1080. The editor passed 16 checks. The client and editor harness logs reported one and 119 warning lines, respectively.

The seeded server reached UDP 55970 and shut down cleanly. Both gender migrations and all four sample distributions were verified directly in the disposable SQLite databases. The runbook covers the pending interactive login, editor and battle checks. MySQL verification uses offline model and generated SQL checks.

The broad server regression harness passed 188 checks with zero failures. A focused rerun passed all three updated migration checks. Those three are included in the broad count. Each server harness log reported one nullability warning and zero errors.

## Changed files

- `Framework/Intersect.Framework.Core/Descriptors/GameObjectType.cs`
- `Framework/Intersect.Framework.Core/GameObjects/Monsters/MonsterSpeciesDescriptor.cs`
- `Framework/Intersect.Framework.Core/GameObjects/Monsters/OwnedMonsterDetails.cs`
- `Framework/Intersect.Framework.Core/Network/Packets/Server/MonsterBattleStatePacket.cs`
- `Intersect (Core)/SharedConstants.cs`
- `Intersect.Client.Core/Interface/Game/Monsters/MonsterBattleWindow.cs`
- `Intersect.Client.Core/Interface/Game/Monsters/MonsterRosterWindow.cs`
- `Intersect.Editor/Forms/Editors/frmMonsterSpecies.cs`
- `Intersect.Editor/Forms/frmMain.cs`
- `Intersect.Editor/Networking/PacketHandler.cs`
- `Intersect.Server.Core/Database/DbInterface.cs`
- `Intersect.Server.Core/Database/GameData/GameContext.Monsters.cs`
- `Intersect.Server.Core/Database/GameData/GameContext.cs`
- `Intersect.Server.Core/Database/GameData/IGameContext.cs`
- `Intersect.Server.Core/Database/PlayerData/PlayerContext.Monsters.cs`
- `Intersect.Server.Core/Database/PlayerData/Players/MonsterInstance.cs`
- `Intersect.Server.Core/Migrations/Sqlite/Game/SqliteGameContextModelSnapshot.cs`
- `Intersect.Server.Core/Migrations/Sqlite/Player/SqlitePlayerContextModelSnapshot.cs`
- `Intersect.Server.Core/Monsters/Battles/MonsterBattleCombatant.cs`
- `Intersect.Server.Core/Monsters/Battles/MonsterPracticeBattles.cs`
- `Intersect.Server.Core/Monsters/Calculations/MonsterAttributeGeneration.cs`
- `Intersect.Server.Core/Monsters/MonsterDiagnostics.cs`
- `Intersect.Server.Core/Monsters/MonsterOwnershipService.cs`
- `Intersect.Server.Core/Monsters/MonsterRosterDetails.cs`
- `Intersect.Server.Core/Networking/PacketSender.cs`
- `Intersect.Server/Migrations/MySql/Game/MySqlGameContextModelSnapshot.cs`
- `Intersect.Server/Migrations/MySql/Player/MySqlPlayerContextModelSnapshot.cs`
- `Intersect.Server/Networking/NetworkedPacketHandler.cs`
- `Intersect.Tests.Client/Monsters/MonsterBattlePresentationTests.cs`
- `Intersect.Tests.Client/Monsters/MonsterRosterLayoutTests.cs`
- `Intersect.Tests.Editor/MonsterMoveEditorTests.cs`
- `Intersect.Tests.Editor/MonsterSpeciesEditorTests.cs`
- `Intersect.Tests.Server/GameObjects/MonsterBattleTests.cs`
- `Intersect.Tests.Server/GameObjects/MonsterIndividualTests.cs`
- `Intersect.Tests.Server/GameObjects/MonsterMoveTests.cs`
- `Intersect.Tests.Server/GameObjects/MonsterNatureTests.cs`
- `Intersect.Tests.Server/GameObjects/MonsterOwnershipTests.cs`
- `Intersect.Tests.Server/GameObjects/MonsterSpeciesTests.cs`
- `Intersect.Tests.Server/GameObjects/MonsterTypeTests.cs`
- `Framework/Intersect.Framework.Core/GameObjects/Monsters/MonsterGenderDescriptor.cs`
- `Intersect.Client.Core/Interface/Game/Monsters/MonsterGenderText.cs`
- `Intersect.Editor/Forms/Editors/frmMonsterGender.cs`
- `Intersect.Server.Core/Migrations/Sqlite/Game/20261113000000_AddMonsterGenders.Designer.cs`
- `Intersect.Server.Core/Migrations/Sqlite/Game/20261113000000_AddMonsterGenders.cs`
- `Intersect.Server.Core/Migrations/Sqlite/Player/20261113000000_AddOwnedMonsterGender.Designer.cs`
- `Intersect.Server.Core/Migrations/Sqlite/Player/20261113000000_AddOwnedMonsterGender.cs`
- `Intersect.Server/Migrations/MySql/Game/20261113000000_AddMonsterGenders.Designer.cs`
- `Intersect.Server/Migrations/MySql/Game/20261113000000_AddMonsterGenders.cs`
- `Intersect.Server/Migrations/MySql/Player/20261113000000_AddOwnedMonsterGender.Designer.cs`
- `Intersect.Server/Migrations/MySql/Player/20261113000000_AddOwnedMonsterGender.cs`
- `docs/monster-genders.md`
