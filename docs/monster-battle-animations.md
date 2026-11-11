# Milestone 6G — Authored monster battle animations

## Existing animation system

Intersect's Animation Editor edits the same `AnimationDescriptor` used by maps, entities and existing game objects. A descriptor stores its UUID, sound, Complete Sound setting and two independently authored layers. Each layer selects a sprite sheet, horizontal and vertical divisions, frame count, milliseconds per frame and extra loops. It also carries per-frame lights, rotation settings and an alternate world layer option. The editor previews both layers and can preview sound.

The entity animation renderer calculates row-major sheet frames from elapsed time, caps usable frames at the number of cells, and treats Loop Count as repeats after the first pass. Its draw calls use world coordinates, global live-animation tracking, world lighting, and positional map audio. The world renderer does not share the Gwen battle window's clip rectangle or draw ordering. The battle adapter reuses the descriptor, cached animation textures, frame conventions and shared game audio. It draws through ordinary Gwen image panels instead of creating world animation instances. The existing Animation Editor, animation serialization and original real-time combat remain as they were.

## Move settings and synchronization

In the Monster Move Editor, **Animation** keeps the existing nullable animation UUID. **Effect position** adds Target, Attacker and Both. The value only selects where the visuals play. The move's gameplay Target and existing battle legality rules still determine who it can affect. Both draws at two distinct actors, or once if their IDs match.

SQLite and MySQL migration `20261110000100_AddMonsterMoveAnimationPosition` adds an integer column to `MonsterMoves` with Target (0) as its default. Legacy JSON also defaults to Target. Normal draft edits, save, reload, copy/paste, undo and cancel use the existing move content flow. Existing reference validation rejects new missing animation references and blocks deleting an animation referenced by a monster move. Clearing the reference is valid.

The server freezes the move's UUID reference and effect position with its battle move snapshot and includes them in the authoritative MoveUsed event. The existing AnimationId key stays in place. Key 10 adds position, and the handshake advances to `monster-battles-v5`. The same authenticated owner and revision checks apply. The existing content sync supplies animation descriptors and normal local resources supply textures and sounds. Battle packets carry no animation assets.

## Playback and lifecycle

1. When the ordered MoveUsed event starts, the client shows its message, loads each valid layer from the shared cache and starts one non-positional sound if one is assigned. Both anchors share that single audio trigger.
2. Each layer advances independently in row-major order, with milliseconds per frame and its authored repeat count. Playback uses at most four passes per layer and four seconds per move. A valid effect has at least the ordinary 0.35-second move-message interval. No authored impact marker exists in the current format.
3. At the end of MoveUsed, the effect panels clear. The next authoritative DamageApplied event begins the existing half-second HP interpolation and recoil if damage occurred. An authored visual layer replaces the generic red impact flash. A missing or invalid layer simply disappears, and moves with no usable art retain the generic lunge and impact. Misses can play their authored move effect but have no damage animation. An immune hit leaves HP at the authoritative unchanged value.
4. Critical-hit, effectiveness, faint and result events retain their server order. A canceled move has no MoveUsed event, so there is no effect or sound. Withdrawal and send-out events retain their own animations. The effect anchor resolves the event's individual UUIDs against the current displayed sprites, including after switching or forced replacement.

Lower art renders behind the sprites, and upper art in front of sprites but before the info panels. Both are centered on the actor's current scene-local sprite bounds. Frames retain native size up to a 160-pixel longest edge, then scale down to fit that cap. Gwen's scene clipping and point sampling preserve transparent pixel art and keep effects out of the commands and footer. Bounds follow the scene layout on resize. World lighting, direction-based rotation, alternate world render passes and map-positioned sound have no battle equivalents in this milestone. A corrupt layer or missing asset uses the available layer or generic effects.

The existing game sound system applies the client's sound volume. Audio starts once when the ordered event begins. Complete Sound allows natural completion through subsequent events in the same snapshot. Other sounds end with the MoveUsed event. Next ends the current event immediately and advances to the following event. Skip clears effects and audio and takes displayed HP and phase to the authoritative snapshot without submitting an action. An accepted new revision, cancel, forfeit, disconnect, closing the battle window or disposing it clears active and finishing sounds. Duplicate and stale packets do not restart them.

The adapter uses four fixed Gwen panels per battle window and borrows texture handles from Intersect's cache. It resolves the animation and loads textures once per played move, so authored edits and resource changes can be picked up on later moves. It releases its references when the effect ends. The existing bounded, tick-driven event player controls duration and input.

## Windows builds

- Server — `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6g-build/server/Intersect Server.exe`
- Client — `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6g-build/client/Intersect Client.exe`
- Editor — `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6g-build/editor/Intersect Editor.exe`

## Disposable Windows verification

The prepared world is `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6g-live-55967`. It uses its own SQLite databases, normal default class and map, and matching protocol. It contains Mossling, Emberling, Pebbling, a faster practice opponent and a healer. It starts without seeded users or characters. The animation below is created through the normal Animation Editor as part of the live check.

Start the server in one PowerShell window and wait for UDP 55967.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6g-live-55967/server'
& '.\Intersect Server.exe' --no-upnp --no-port-check --migrate-automatically
```

In a second PowerShell window, start the prepared client.

```powershell
Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6g-live-55967/client'
& '.\Intersect Client.exe'
```

1. Set 1280×720 if available. Create an account named **m6gtester** with a disposable password and an email such as `m6gtester@example.invalid`. Create **M6GTrainer** using the default class and enter the map. Log out before connecting with the editor. If editor access is needed, run `poweracc m6gtester 2` in this disposable server console.
2. Launch the matching editor from the prepared client directory so it uses that directory's localhost configuration and resources.

   ```powershell
   Set-Location -LiteralPath 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6g-live-55967/client'
   & 'C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6g-build/editor/Intersect Editor.exe'
   ```

3. Log in with the disposable account. Under **Game Editors → Animation Editor**, create **6G Practice Slash**. In the upper layer select the included `Slashing.png` sheet, set 10 horizontal divisions, one vertical division, 10 frames, 120 ms per frame, and Loop Count 0. Preview it. Select `bite-small.wav` if testing audio. Leave Complete Sound unchecked for the first run. Save and reopen the Animation Editor to confirm the fields persisted.
4. Under **Game Editors → Monster Move Editor**, select **Leaf Tackle**, which is used by the player species and the practice opponent. Select **6G Practice Slash** in Animation, choose **Target** in Effect position, and save. Reopen to confirm the reference and position. Close the editor and reconnect in the matching client.
5. Grant the three species in the server console. Record the returned individual UUIDs as A, B and C.

   ```text
   grantmonster "M6GTrainer" 6f000000-0000-4000-8000-000000000001 6f000000-0000-4000-8000-000000000021
   grantmonster "M6GTrainer" 6f000000-0000-4000-8000-000000000002 6f000000-0000-4000-8000-000000000022
   grantmonster "M6GTrainer" 6f000000-0000-4000-8000-000000000003 6f000000-0000-4000-8000-000000000023
   startmonsterbattle M6GTrainer <A> 6f000000-0000-4000-8000-000000000004 1
   ```

6. Use **Fight → Leaf Tackle**. Confirm the faster opponent's move message and white slash play first over the player sprite, then the player animates over the opponent if still able to act. Confirm the sprite sheet visibly changes frames and that HP starts falling after the effect ends. Listen for one sound per move and lower the client's sound volume to confirm it applies. This is the primary live authoring verification.
7. Forfeit and Return. Log out, reopen the Move Editor, change Effect position to **Attacker**, save, and start a fresh practice battle with the same command. Each move's effect should follow its own actor. Repeat for **Both** and confirm one sound with two visible anchors. For a two-layer check, add `Rings.png` to Lower with nine horizontal divisions, one vertical division, nine frames and 80 ms per frame. Preview and save in the Animation Editor. Reopen the battle to verify the lower layer is behind the sprites. Clear Lower again before testing missing art.
8. During a move effect, click **Next** and confirm the next event begins without replaying the sound. During a second effect, click **Skip playback**. Confirm HP jumps to the authoritative endpoints, overlays and audio clear, and the next legal command phase appears. Run `inspectmonster <A>` to compare stored HP and PP.
9. Try the generic fallback by clearing Leaf Tackle's Animation in the Move Editor, saving, and starting another battle. To exercise missing art with the reference still set, assign it again, close the client/editor, and temporarily rename `Slashing.png` inside the **disposable client** `resources/animations` folder to `Slashing.png.m6g-backup`. Reopen the client and verify ordinary impact/HP playback. Restore that filename after the check. The sound can still play if its resource is present.
10. Restore the authored move. In a fresh battle, switch from A to B through Monsters. The opponent's effect should anchor to B after send-out. Use a move as B and switch back. Confirm the outgoing sprite's effect does not persist. For forced replacement, use `startmonsterbattle M6GTrainer <A> 6f000000-0000-4000-8000-000000000004 40` and let the faster opponent faint A. Choose B. Only send-out should play during replacement. An attack canceled by fainting has no authored move effect.
11. Test Forfeit and Return, then disconnect during an effect and reconnect. Inspect `<A>`, `<B>` and `<C>` using `inspectmonster` and compare HP/PP with Monsters. The healer next to spawn or `healmonster <A> all` can reset condition between runs. Repeat at 800×600 and a larger resolution to check anchors and clipping.

## Verification

All matching Debug builds succeeded with zero errors. The server reported 359 warnings, the client 465 and the editor 1,118. Diagnostics included dependency audit, nullable reference and platform analyzer warnings. The shared handshake and packet assemblies matched across all three outputs.

The server regression run passed 170 checks with zero failures. The new tests verify frozen move animation metadata on the wire, SQLite upgrade and Target defaults, existing reference protection, and MySQL model and generated SQL compatibility. MySQL was checked offline. The client passed 27 checks, including ordered frame and HP playback, anchors, sound trigger and cleanup with a test sound, and six Gwen viewport sizes. The editor passed 14 checks, including selecting and clearing an animation and retaining its effect position through copy/paste and undo.

The seeded disposable server booted on UDP 55967 and shut down cleanly. An initial launch ran before the runtime copy finished and could not load Serilog. It started successfully after the copy completed. Startup also exercised the existing hardware-information fallback. The seeded player database contains no accounts or characters.

Interactive confirmation of an animation authored and saved in the Windows editor playing in a real battle is pending. The automated Gwen fixture renders authored sheet frames using controlled elapsed time and test textures. Actual visual and sound output still needs the checklist above. Status effects, healing moves, buffs, double battles and custom impact markers are outside the milestone.

## Source changes

- Modified `Framework/Intersect.Framework.Core/GameObjects/Monsters/MonsterMoveDescriptor.cs`
- Modified `Framework/Intersect.Framework.Core/Network/Packets/Server/MonsterBattlePresentationEvent.cs`
- Modified `Intersect (Core)/SharedConstants.cs`
- Modified `Intersect.Client.Core/Interface/Game/Monsters/MonsterBattlePlayback.cs`
- Modified `Intersect.Client.Core/Interface/Game/Monsters/MonsterBattleWindow.cs`
- Modified `Intersect.Editor/Forms/Editors/frmMonsterMove.cs`
- Modified `Intersect.Editor/Localization/Strings.MonsterMoves.cs`
- Modified `Intersect.Server.Core/Database/GameData/GameContext.cs`
- Modified `Intersect.Server.Core/Migrations/Sqlite/Game/SqliteGameContextModelSnapshot.cs`
- Modified `Intersect.Server.Core/Monsters/Battles/MonsterBattleSession.cs`
- Modified `Intersect.Server/Migrations/MySql/Game/MySqlGameContextModelSnapshot.cs`
- Modified `Intersect.Tests.Client/Monsters/MonsterBattlePresentationTests.cs`
- Modified `Intersect.Tests.Client/Monsters/MonsterRosterLayoutTests.cs`
- Modified `Intersect.Tests.Editor/MonsterMoveEditorTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterBattleTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterMoveTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterNatureTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterOwnershipTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterSpeciesTests.cs`
- Modified `Intersect.Tests.Server/GameObjects/MonsterTypeTests.cs`
- Added `Intersect.Client.Core/Interface/Game/Monsters/MonsterBattleAnimation.cs`
- Added `Intersect.Server.Core/Migrations/Sqlite/Game/20261110000100_AddMonsterMoveAnimationPosition.Designer.cs`
- Added `Intersect.Server.Core/Migrations/Sqlite/Game/20261110000100_AddMonsterMoveAnimationPosition.cs`
- Added `Intersect.Server/Migrations/MySql/Game/20261110000100_AddMonsterMoveAnimationPosition.Designer.cs`
- Added `Intersect.Server/Migrations/MySql/Game/20261110000100_AddMonsterMoveAnimationPosition.cs`
- Added `docs/monster-battle-animations.md`
