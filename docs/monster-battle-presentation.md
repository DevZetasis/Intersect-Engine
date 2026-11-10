# Milestone 6D: classic monster battle presentation

## Delivered behavior

The existing practice resolver remains authoritative and resolves the entire turn before publication. Its damage formulas, IV/EV/nature/type calculations, RNG calls, priority/Speed ordering, PP consumption, opponent provider, ownership checks and terminal/disconnect cleanup remain in place. Original Intersect combat and the roster/HUD are retained.

The Gwen battle window now composes an opponent in the upper-right and the player's monster in the lower-left, with readable name/level/HP panels. Both HP bars animate authoritative changes. Numerical HP appears in one dedicated label per actor; the bar's own text is empty. Low HP (at or below 20%) has a red bar and LOW HP label, with yellow at or below 50% and green otherwise. An explicitly inactive EXP area reserves space on normal-sized views; it is hidden in compact scenes. No experience progress or reward is fabricated.

Fight opens a two-by-two move menu with name, type, category and current/maximum PP. Back returns before committing an action. Empty, unsupported and exhausted moves are disabled. Bag, Monsters and Run are visibly unavailable and have no action handlers. Run never sends Forfeit. The separate Forfeit control remains functional at command selection, and Return to game becomes available after final playback. Existing server-side roster movement guards remain active during a battle.

## Species artwork and persistence

| Property | Asset convention | Empty or missing behavior |
| --- | --- | --- |
| `Sprite` | Existing entity spritesheet | Unchanged for party/storage and overworld use |
| `FrontBattleSprite` | Full static PNG/image in `resources/entities` | Use the first idle frame of `Sprite`, or a placeholder if unavailable |
| `BackBattleSprite` | Full static PNG/image in `resources/entities` | Use the first idle frame of `Sprite`, with a tooltip documenting that this is not dedicated back artwork; otherwise a placeholder |
| `BattleBackground` | Image in `resources/images` | Opponent setting, then player setting, then `monster-battle-default.png`, then the original Gwen landscape |

Dedicated images are interpreted as complete images, not sheets. Use alpha transparency and cropped canvases. Existing resource lookup and ImagePanel rendering are reused; no new loader or UI framework is introduced. The existing MonoGame renderer uses PointClamp sampling. Aspect ratio is preserved, enlargement uses integer scales up to 4x where space permits, and each sprite fits a separate scene region. Missing/corrupt optional textures cannot prevent battle presentation. Back fallback deliberately shows an idle overworld frame rather than pretending to synthesize a rear view. The two sides remain distinguishable by position and actor labels.

The Monster Species Editor has three additional resource selectors using the existing entity/image lists and draft/save/cancel/copy/undo workflow. Legacy JSON loads these properties as empty strings, including when reusing a draft; UUID and existing species fields are retained. References are limited to 255 characters.

SQLite and MySQL each receive `20261109000200_AddMonsterBattleAppearance`, its target-model designer, and an updated game snapshot. Only three content columns are added to MonsterSpecies: FrontBattleSprite, BackBattleSprite and BattleBackground, non-null with empty defaults. SQLite uses TEXT with the configured maximum length; MySQL uses varchar(255). There is no player database migration. No existing development/test database was migrated by this task; migration tests and live startup used new disposable databases.

Background selection is snapshotted when starting a session. Custom backgrounds use centered aspect-preserving cover cropping; author composition with the scene's wide aspect ratio in mind. The built-in fallback landscape and platforms are original Gwen primitives. Original geometric creature/background PNGs used for live checks exist only in the disposable runtime; no Nintendo/Pokemon artwork is shipped.

## Ordered network facts

Existing MessagePack keys are retained. Additions:

| Object | Appended keys |
| --- | --- |
| MonsterBattleActorView | 6 FrontBattleSprite, 7 BackBattleSprite |
| MonsterBattleStatePacket | 10 Events, 11 PreviousRevision, 12 IsCorrection, 13 Background |
| MonsterBattlePresentationEvent | 0 Kind, 1 Order, 2 ActorId, 3 TargetId, 4 MoveId, 5 AnimationId, 6 HPBefore, 7 HPAfter, 8 Text |

Event kinds have explicit values: MoveUsed=0, MoveMissed=1, DamageApplied=2, CriticalHit=3, TypeEffectiveness=4, MonsterFainted=5, BattleFinished=6. The containing packet supplies battle UUID, turn/revision, recipient and monotonic sequence. PreviousRevision identifies the input revision whose turn produced the facts; packet Turn retains the existing current/next-command meaning. No event order is inferred by parsing text or comparing speeds on the client.

The resolver emits events in its already calculated action order. A miss has no damage event; immunity has a damage fact with equal before/after HP. Fainting prevents a later action as before. The final event follows the faint/action messages. Snapshots retain authoritative final HP/PP and outcomes for correction. Rejections carry IsCorrection and no replay events. Snapshot event objects are copied, and only the latest turn batch is retained in a session; no growing battle history is introduced. Clients reject invalid/oversized batches and fall back to the snapshot. The presentation batch limit is 32, comfortably above the current two-action resolver's output.

The opponent chooses before action ordering and calculation, never after a client animation. The UI reveals each resolved move when its MoveUsed event reaches playback, rather than displaying the second action early. The packet contains resolved facts, not a pending decision or private opponent IV/EV/nature/move inventory.

The fork compatibility marker changes `monster-battles-v1` to `monster-battles-v2`; the older marker segments and all existing enum values/packet keys remain unchanged. Matching server/client/editor builds are required. Appending another long marker originally exceeded the existing RSA hail plaintext limit; versioning the battle segment keeps its length unchanged. A regression performs real production HailPacket RSA encryption/decryption. No encryption code, keys, Windows settings or transport algorithm was changed.

## Playback and animation

`MonsterBattlePlayback` is a renderer-independent state machine with Waiting, CommandSelection, AwaitingServer, PlayingSequence and ShowingResult phases. Fight/Main navigation is a separate view flag. The window feeds it monotonic-clock deltas; there are no UI-thread sleeps. A newly accepted sequence starts at elapsed zero. A move is submitted once using the existing battle/turn/revision/slot packet, then commands lock until the server responds and playback completes. Hidden move buttons also remain disabled outside Fight, preventing a stale keyboard focus from submitting a new command.

A resolved sequence plays one event at a time. HP interpolates only during the relevant DamageApplied event, between the server's values. Generic presentation includes a short lunge, target flash/recoil on actual damage, smooth HP reduction, faint fade/disappearance, and short message transitions. Misses and immunity do not flash false damage. Result controls appear after final events. Next advances the current presentation event; Skip finishes presentation at the authoritative snapshot endpoints without sending any action or advancing a server turn.

Sequence/revision checks prevent stale and duplicate replay. A missing revision chain or correction synchronizes to the final snapshot instead of inventing an animation. Cancellation/forfeit interrupts stale presentation; disconnect clears local state/artwork, and disposal removes the window from input-blocking components. Returned result windows stay hidden until a genuinely new authoritative state opens them.

Move AnimationId references feed a limited existing AnimationDescriptor adapter: lower/upper texture layers, X/Y frame grids, FrameCount and FrameSpeed render at the target on a hit. It plays one cycle with a bounded presentation duration (0.5 to 2 seconds); longer authored effects may be cut short. Missing layers/definitions/textures leave the generic animation usable. Sound, lighting, rotations, loop semantics, map-space effects, projectiles, beams and particle orchestration remain future work; this is not a second animation editor.

## Layout details and fixes found live

The new `MonsterBattleWindowV2` layout name keeps incompatible old V1 geometry intact on disk. Core scene/menu geometry fits the viewport, with compact info panels at smaller sizes, wrapped/truncated long names and full-name tooltips. Backgrounds/sprites cannot extend into the menu area.

Saved Gwen layouts persist control Disabled flags, textures and hidden state. OnJsonReloaded reapplies authoritative phase, move availability, HP, result controls and entity/image artwork. Both menu views are explicitly sized from the command panel even while hidden: otherwise Gwen's deferred hidden-panel docking could open Fight with tiny buttons until another click. This was found live and is now checked immediately after opening Fight, before a deferred layout frame. Duplicate numerical HP was also found live and fixed. These fixes preserve the earlier authoritative-control restoration behavior.

## Verification results

- **139 server cases passed, zero failures**, against matching rebuilt assemblies: existing calculation/ownership/player tests plus typed ordering, faster opponents, priority, miss/immunity, KO action omission, revision association, event copying/bounds/wire serialization, appearance compatibility/migration/persistence, and real RSA hail round trip.
- **22 client checks passed, zero failures**: 12 deterministic presentation/art cases, four existing content/roster packet checks and six real-Gwen roster/HUD/battle cases at 800x600, 1024x768, 1280x720, 1366x768, 1600x900 and 1920x1080. They include first-open menu sizes, HP endpoints, visible lunge, valid/missing authored effects, skip without action submission, unavailable options, Run/Forfeit separation, stale/foreign packets, saved layout restoration and awaiting-state locks.
- **13 editor checks passed, zero failures**: existing species/type/move/nature coverage, now including appearance draft/cancel and copy/undo values.

After a whitespace-only cleanup reduced GameContext to just the three new mappings, focused SQLite persistence, MySQL model/SQL, additive migration operations and RSA hail checks were rerun against the final outputs.

These are targeted cached NUnit harnesses compiling actual repository test sources, not every solution test via dotnet test. SQLite tests use GUID-named disposable databases. MySQL model agreement and SQL generation are checked without connecting to a live MySQL server. Physical DPI and GPU rendering are not proven by logical-pixel layout tests.

All matching Debug outputs built with **zero errors**. Final logs contain 351 server warnings, 464 client warnings and 277 editor warnings; existing package/analyzer warnings remain and counts vary with incremental rebuilds. Logs are under `%TEMP%`: `monster-m6d-server-build-complete.log`, `monster-m6d-client-build-complete.log`, `monster-m6d-editor-build-complete.log`, `monster-m6d-server-tests-complete.log`, `monster-m6d-client-tests-complete.log`, `monster-m6d-editor-tests2.log`, and `monster-m6d-handshake.log`.

## Live Windows results

The isolated client was launched with 1280x720 arguments against localhost:55964. The user created the account and M6DTrainer through the normal client workflow; only game content was seeded. The character received a monster through grantmonster, without player-record repairs.

The user confirmed dedicated back art at lower-left, front art at upper-right, the meadow image, Fight/Back navigation, single readable HP labels and readable move buttons immediately on first opening. The user confirmed visible attacks and sequential HP updates, then explicitly confirmed Victory and Return to game. A level-3 opponent test confirmed the faster opponent acted first, player faint fade, Defeat and Return. A further new battle confirmed disabled Run, separate Forfeit and normal return. The server found none of those completed battle IDs active afterward. Closing/reconnecting during the earlier active test also released the reservation, as the subsequent start succeeded.

Read-only comparison found one owned monster with unchanged UUID, owner, species, level/experience, IVs, EVs, nature, nickname, final party slot and move assignments. Only the placement Revision changed: server logs show explicit accepted Store and Retrieve requests between the comparisons. This is an expected user collection operation, not battle progression. Battle HP and PP remain temporary.

The Windows computer-use helper repeatedly timed out listing windows, including after its prescribed reset. Therefore the live visual confirmations above are user observations, not independently captured screenshots. Dedicated missing-art/background fallbacks, authored effects, skip, draw and detailed stale/correction behavior have automated coverage; they were not all exercised live. The final authored-frame/placeholder-color refinements were tested automatically after the successful generic-animation live checks.

## Matching executable paths

Keep each executable with its entire output folder:

```text
C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6d-build/server/Intersect Server.exe
C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6d-build/client/Intersect Client.exe
C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6d-build/editor/Intersect Editor.exe
```

The shared core DLL is identical across the three output folders. Existing running applications were not replaced. The dedicated live copy under `C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6d-live-b86fd2d1-e93e-4452-947a-51d222723d10` has its own server/client resources and databases. Both stopped live runtime copies were refreshed from the final outputs at completion, with their databases/resources/configurations preserved. The prepared server is currently stopped; start it from its own server directory using the command below before reconnecting.

## Exact disposable test instructions

1. Create a new scratch runtime outside existing worlds, copy complete matching output folders, and copy only licensed client asset folders into its client resources. Configure **only the new copy** for SQLite and an unused localhost port. Disable its optional API in both scratch appsettings files if not needed. Never launch migrations from an existing development world's directory. Keep server console input open.
2. In the new server directory run `& '.\Intersect Server.exe' --no-upnp --no-port-check --migrate-automatically`. In its client directory run `& '.\Intersect Client.exe' --screen-width 1280 --screen-height 720`. Verify game/window resolution in client options. Start the matching editor against this same scratch port.
3. Create an account and character normally. In the Monster Type/Move editors create a type, a damaging Physical move, a Special move, a priority move and an unsupported Status move. Give them level-1 learnset entries **before** granting an individual. Optionally select an existing Animation Editor definition on a move.
4. In the Species Editor select the unchanged overworld Sprite, a full static front image, a full static back image and an image background. Save, reopen and inspect values. Copy/undo/cancel must retain UUID/draft behavior. Use a second species with only a weak damaging move for an easy win, and a higher-level opponent to test defeat/order.
5. Run `grantmonster "<character>" <species-UUID> <new-request-UUID>` and record the individual UUID. Retry the exact request to verify idempotency. Store/Retrieve outside battle must still work. During an active battle the server must reject collection movement.
6. Run `startmonsterbattle "<character>" <owned-party-monster-UUID> <opponent-species-UUID> <level>`. Check scene composition, sharp transparency/aspect ratios, name/level/HP and inactive EXP. Fight must show readable buttons immediately. Back must return without a packet. Bag/Monsters/Run stay unavailable. Forfeit is a distinct control.
7. Select an attack. Check first actor message/lunge, target hit reaction, HP transition, critical/effectiveness messages when applicable, then the second actor. Commands must stay locked. For an order test use a faster opponent and a normal-priority player move. For a priority override test configure the opponent with only a lower-priority move; the player priority move must go first despite lower Speed.
8. Use a copied low-accuracy move (AlwaysHits off) and a custom immunity relationship in this disposable content to check misses/immunity. They must not show false HP loss or a damage flash. A KO must prevent the fainted actor's later action/PP spend. Skip/Next must remain responsive and never submit another move; skip must land exactly on final HP/outcome.
9. Win, lose and forfeit. Let faint playback finish before the result; alternatively skip it explicitly. Return to game must work. Start another battle and confirm full temporary HP/PP. Disconnect during playback, reconnect, and start again. Operator `cancelmonsterbattle <battle-UUID>` must interrupt presentation cleanly. Completed IDs must no longer be active.
10. On a duplicate disposable species select None for back art, then all sprite art, and for the background. Grant fresh individuals as needed. Verify overworld-frame fallback, explicit no-art placeholder and the original procedural landscape. To test missing files, alter only a newly created test asset copy, with the client closed; never rename/delete original resource packs. Restart to avoid cached textures masking the missing asset.
11. Test an authored two-layer animation with valid grid/frame speed, then a missing reference. Generic lunge/impact must still work. Longer loops/sound/map-space semantics are deliberately deferred.
12. Inspect before/after ownership and inspectmonster output; IV/EV/nature/level/experience and saved move UUIDs must not change from battles. No rewards, capture, switching, items, escape calculation or persistent HP/PP exist in this milestone.
13. Repeat at 1366x768, 1600x900 and 1920x1080, with long custom names and tall/wide sprites. On machines already using different DPI settings, verify real scaling; physical DPI testing remains follow-up. Avoid changing Windows settings or existing worlds for this task.

### Prepared live world

`C:/Users/calip/.codex/visualizations/2026/11/08/01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0/m6d-live-b86fd2d1-e93e-4452-947a-51d222723d10`

Synthetic test account: `m6dtester`, password `M6D-Test-2026!`, character `M6DTrainer`. The world already has this normally created character; do not rerun the empty-world content seed against it. Use its own server console, not an existing development server. Check that port 55964 is free before starting another copy.

```text
grantmonster "M6DTrainer" e5ea246f-ab58-44da-bff6-34d936a5dde0 2b567a49-917a-45e1-8218-e0a79174fddf
startmonsterbattle "M6DTrainer" a099360e-5808-4a57-8f21-e0f759757001 90fccba6-2d12-44d0-8ebb-d967e22a8d09 1
startmonsterbattle "M6DTrainer" a099360e-5808-4a57-8f21-e0f759757001 e5ea246f-ab58-44da-bff6-34d936a5dde0 3
inspectmonster a099360e-5808-4a57-8f21-e0f759757001
```

The first grant command is an idempotent retry, not a new request. Level 1 gentle opponent is the win test; level 3 Mossling is the faster-opponent/defeat test. The original procedural test-art PNGs are in this world's client resources only.

## Deliberate placeholders and future integration

Bag items, monster switching, escape, captures, wild encounters, trainer AI/PvP, doubles, rewards/leveling, persistent HP/PP and full Status effects are not implemented. EXP is explicitly inactive. Future encounter context can choose the session background; future active-side/action collection can reuse ordered events. Full authored animation/sound/particle orchestration and localization of server combat messages remain future work. No complete Generation 4 simulator or universal DPI validation is claimed.

No commit or push was performed. Existing world databases/configurations, engine-base and Intersect-Base-Copy remain untouched.

## File manifest

### Modified (22)

```text
Framework/Intersect.Framework.Core/GameObjects/Monsters/MonsterSpeciesDescriptor.cs
Framework/Intersect.Framework.Core/Network/Packets/Server/MonsterBattleStatePacket.cs
Intersect (Core)/SharedConstants.cs
Intersect.Client.Core/Interface/Game/Monsters/MonsterBattleWindow.cs
Intersect.Client.Core/Localization/Strings.MonsterBattle.cs
Intersect.Editor/Forms/Editors/frmMonsterSpecies.cs
Intersect.Editor/Localization/Strings.MonsterSpecies.cs
Intersect.Server.Core/Database/GameData/GameContext.cs
Intersect.Server.Core/Migrations/Sqlite/Game/SqliteGameContextModelSnapshot.cs
Intersect.Server.Core/Monsters/Battles/MonsterBattleCombatant.cs
Intersect.Server.Core/Monsters/Battles/MonsterBattleSession.cs
Intersect.Server.Core/Monsters/Battles/MonsterPracticeBattles.cs
Intersect.Server/Migrations/MySql/Game/MySqlGameContextModelSnapshot.cs
Intersect.Tests.Client/Monsters/MonsterRosterLayoutTests.cs
Intersect.Tests.Editor/MonsterSpeciesEditorTests.cs
Intersect.Tests.Server/GameObjects/MonsterBattleTests.cs
Intersect.Tests.Server/GameObjects/MonsterMoveTests.cs
Intersect.Tests.Server/GameObjects/MonsterNatureTests.cs
Intersect.Tests.Server/GameObjects/MonsterOwnershipTests.cs
Intersect.Tests.Server/GameObjects/MonsterSpeciesTests.cs
Intersect.Tests.Server/GameObjects/MonsterTypeTests.cs
docs/monster-battles.md
```

### Added (10)

```text
Framework/Intersect.Framework.Core/Network/Packets/Server/MonsterBattlePresentationEvent.cs
Intersect.Client.Core/Interface/Game/Monsters/MonsterBattleArt.cs
Intersect.Client.Core/Interface/Game/Monsters/MonsterBattlePlayback.cs
Intersect.Client.Core/Interface/Game/Monsters/MonsterBattleScene.cs
Intersect.Server.Core/Migrations/Sqlite/Game/20261109000200_AddMonsterBattleAppearance.Designer.cs
Intersect.Server.Core/Migrations/Sqlite/Game/20261109000200_AddMonsterBattleAppearance.cs
Intersect.Server/Migrations/MySql/Game/20261109000200_AddMonsterBattleAppearance.Designer.cs
Intersect.Server/Migrations/MySql/Game/20261109000200_AddMonsterBattleAppearance.cs
Intersect.Tests.Client/Monsters/MonsterBattlePresentationTests.cs
docs/monster-battle-presentation.md
```

