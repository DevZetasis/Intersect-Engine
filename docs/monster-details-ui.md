# Monster details and bottom-right HUD menu (Milestone 6A/6B)

## Implementation

The existing Monsters HUD button, roster, sprites, party/storage controls and ownership packets are reused. The roster now has a separately scrollable detail panel with nickname/species, level, experience, placement, Nature, six calculated stats, six IVs/EVs and total EVs. HP means calculated **maximum** HP; no battle-health state is created. Refresh and Store/Retrieve stay outside the scrollable area. Fonts are explicitly measurable and at least 14px. The preview preserves sprite aspect ratio.

Selection is retained by individual UUID when a fresh snapshot arrives. Selecting another monster replaces every detail field. Removed selections, empty collections, logout and foreign-character snapshots do not retain private details or sprites from the previous selection. Missing/older detail payloads display an unavailable message instead of inventing statistics. Long text wraps inside the detail scroller; individual fields also have tooltips.

## Server authority and compatibility

`OwnedMonsterSummary` retains MessagePack keys 0–7 and appends optional `Details` at key 8. `OwnedMonsterDetails` contains optional Nature UUID/name, six-value IV and EV arrays, optional six-value calculated-stat array, and a safe calculation-error indicator. Array order is HP, Attack, Defense, Special Attack, Special Defense, Speed. Values are small MessagePack integer arrays, not duplicated species definitions or account records.

The existing request contains no target-player UUID. The handler accepts only an authenticated, in-game non-editor client and sends the roster to that character. `GetOwned` filters persistent rows by that owner's UUID **before** invoking the detail mapper. Other characters' rows never reach it. The move request retains its owner and revision checks. No client mutation command or packet for IVs, EVs, natures or calculated stats was added.

`MonsterRosterDetails` calls the existing Milestone 5B `MonsterStatCalculator` with current species, nature and `Options.Monsters.Battle` rules. It uses the same inputs/formulas as `inspectmonster`; there is no client stat formula. Snapshot construction holds the content gate so definition changes cannot split one snapshot across versions. The persisted IVs/EVs and Nature UUID are read, not regenerated.

Existing login, grant and move paths send fresh snapshots. The Refresh button re-queries current persistent values, including after future authorized changes. Such future operations should call `MonsterRoster.Send` after committing to push changes immediately; there is no background polling or automatic battle/progression system. Definition changes also take effect on the next roster refresh.

No database migrations, game-object enum changes, packet registrations, editor redesigns or existing player-data changes are required. Old summaries without key 8 remain readable; matching new builds are required to display the added details. Full backward compatibility with every separately modified fork/protocol is not claimed.

## Bottom-right overflow: actual affected control and cause

Live testing identified the bottom-right **MenuContainer**, containing Options and Monsters, rather than the separate top item/spell `HotBarWindow`. The item/spell hotbar is unchanged in the final implementation.

The existing layout contained a 324×44 parent sized for the eight original 36px icon containers. Its appended `MonstersButton` was persisted at **6×6**, with no useful minimum size. The constructor's recursive `SizeToChildren` allowed the new text button to collapse; a one-time parent measurement around asynchronous JSON loading and docking did not reliably account for the additional button. This explains both the tiny sliver and the final control exceeding the old menu width. These were layout/content measurements, not a need to shrink the icons or reset all UI preferences.

`MenuContainer.Layout.cs` repairs the Monsters text button's font/minimum size, measures the required width from visible left-docked controls plus margins/spacing/padding, and grows the parent before docking. The same operation runs after JSON reload and each menu layout, so stale saved bounds and later layout changes are handled. It clamps an invalid parent position within its logical canvas, respecting padding and at least a four-pixel viewport margin. Valid free positions and existing larger configured menu sizes are retained. Existing icon controls retain their configured sizes and actions. Shared Gwen sizing, rendering, input and application settings are unchanged.

The viewport must physically accommodate the configured buttons at their chosen sizes. Default buttons fit the tested logical viewport sizes, including 640px-wide scaled layouts. Arbitrarily huge custom controls or an exceptionally tiny logical canvas cannot be made to fit without reflow, scrolling or resizing; those new menu behaviors are outside this change.

## Automated verification and builds

Targeted cached NUnit harnesses compile and execute the actual repository test sources. Results:

- Server: 114 cases passed, zero failures, including existing calculator, nature/move/type/species, ownership, grant and player regressions. Two new tests cover owner-scoped detail mapping/MessagePack round-trip/current calculated values/reopened contexts/storage movement and neutral/unassigned data.
- Client: 10 checks passed, zero failures: existing content and character snapshot handling plus six resolution cases. Each layout case checks roster selection/details/IV/EV mapping/empty state and reproduces the old 324px HUD with a 6×6 Monsters button. It also checks valid/free positions, invalid saved positions, anchor transforms, viewport resizing, scale-equivalent logical sizes, and preservation of 36px icon containers.
- Editor: 13 existing species/type/move/nature checks passed, zero failures.
- All three Debug builds completed with zero errors. Final incremental logs report server 357 warnings, client 460 warnings and editor 1116 warnings. Warnings remain; clean/full rebuild warning counts can differ from incremental builds.

These are targeted harness runs, not a claim that every solution test project was run through `dotnet test`. Persistence tests use GUID-named disposable SQLite databases. No original development/test databases were opened for testing or migrated. No new schema is required.

Windows live verification: the user logged into the new disposable world and confirmed the monster statistics displayed correctly. After the corrected HUD menu build was installed and the test server restarted, the user confirmed the full Monsters button and the other bottom-right icons were visible. Windows capture failed with `FrameArrived timed out` / `window capture timed out`, so these are user-observed results, not agent screenshot verification. The complete resolution/DPI/interaction matrix below remains a manual follow-up. The final build also reapplies the bounds guard during parent-driven realignment; automated tests pass, while its full live resize matrix remains pending.

Exact matching executable paths (keep their complete folders and dependencies together):

```text
C:\Users\calip\.codex\visualizations\2026\11\08\01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0\m6-build\server\Intersect Server.exe
C:\Users\calip\.codex\visualizations\2026\11\08\01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0\m6-build\client\Intersect Client.exe
C:\Users\calip\.codex\visualizations\2026\11\08\01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0\m6-build\editor\Intersect Editor.exe
```

Build/test logs are `%TEMP%\monster-m6-{server,client,editor}-{build,tests}.log`.

## Disposable live environment

A new isolated world was created at:

```text
C:\Users\calip\.codex\visualizations\2026\11\08\01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0\m6-live-57a96565-1477-4fda-a442-b90685ec209e
```

It contains independent `server` and `client` runtimes, new SQLite databases and copied asset-only resources. Its client connects to `127.0.0.1:55961`; existing client settings for other servers are preserved. The scratch server's API is disabled in its own `appsettings.json` and `appsettings.Production.json` to avoid unrelated API certificate/port configuration. Existing configuration files and Windows settings were not changed.

The only test account is `m6tester`, password `M6UiTest-2026!`, character `M6Trainer`. These are synthetic local test credentials, not credentials for an existing world. It has a party monster and a stored monster. Login is a user handoff; Windows screen capture timed out, so visual checks must be attributed to the user, not claimed as automated screenshot verification.

To restart this existing disposable server, first ensure another copy of this scratch server is not already using port 55961, then run from its `server` directory in a normal console:

```powershell
& '.\Intersect Server.exe' --no-upnp --no-port-check --migrate-automatically
```

Do not use redirected standard output for this server's normal console bootstrap: it reads `Console.BufferWidth`, and a redirected launch encountered an invalid console handle. This is existing bootstrap behavior, not changed here. Use `exit` in its console for normal shutdown. Leave unrelated running applications alone.

From the disposable `client` directory:

```powershell
& '.\Intersect Client.exe' --screen-width 1280 --screen-height 720
```

For a different fresh world, copy complete matching build folders into a new unique directory; copy only client/editor asset subfolders, not existing databases or server connection configuration. Create fresh server `resources/config.json` selecting SQLite and an unused server port, disable its optional API in both scratch appsettings files if unnecessary, and point only the scratch client/editor at that port. Initialize content/account through the existing workflows. Never point these instructions at the original development resources directory.

## Manual monster details checks

1. Open Monsters from the HUD and select the party monster, then the stored monster. Verify species/nickname, sprite, level/experience, placement, Nature, Max HP and all five other stats. Check all six IVs/EVs and total EVs; fields must be read-only.
2. Scroll the detail panel at smaller window sizes. Refresh and Store/Retrieve must remain outside the scroll area and reachable. Long names must wrap or be readable through their tooltip. The roster list scrolls independently for larger collections.
3. Compare the party monster with the server console command below. For the initially seeded individual, calculated values were `12 / 5 / 6 / 6 / 5 / 5`, IVs `16 / 3 / 24 / 23 / 0 / 7`, zero EVs, and Nature `Patient custom nature 15.5 percent`.

   ```text
   inspectmonster f1c76fd8-6c64-419b-94e8-da8dc76618a5
   inspectmonster a6a8dbdb-116c-4910-aef5-d38fe97843f3
   ```

4. Move the selected monster between party and storage; selection should remain on that UUID and location/action text should update. Refresh, log out/in, restart the scratch client and restart the scratch server. Verify identity/attributes and placement persist.
5. In a separate empty scratch world, open an empty roster. It must show zero counts and selection guidance without a stale sprite/action. Grant a monster with no eligible natures and confirm Neutral/Unassigned. Use a fresh request UUID for each new individual, or reuse one to test the existing idempotent retry.
6. Optionally create content in the matching editor connected only to this disposable server. A new grant uses current definitions, and Refresh recalculates from current species/nature definitions. Referenced nature deletion must still be refused, including offline references.

## Manual bottom-right HUD checks

1. Confirm the full bottom-right menu fits inside the game viewport, especially the complete **Monsters** text button immediately beside Options. Test every existing menu action and tooltip. Separately verify item/spell hotbar icons, drag/drop and activation still behave normally.
2. Test windowed resolutions 1280×720, 1366×768, 1600×900 and 1920×1080 where the display supports them. Use the existing game resolution settings; report unsupported modes instead of claiming they were checked.
3. Change resolution down and back up, then restart the client. Check right and bottom margins each time. Exercise supported UI/DPI scaling without changing Windows configuration for the test.
4. Keep the existing saved HUD layout, including the old 6×6 button and 324px parent, when first testing; the fix must repair it in memory. Do not delete all layouts/preferences. Advanced custom-position tests should edit only a copy in this disposable runtime, retain a valid position, and then test a deliberately off-screen position.

## Limitations

No battles, current HP, capture, encounters, rewards, EV training or new editing commands were introduced. Future authorized attribute writes should push a fresh roster snapshot after commit. Arbitrary custom UI skins/fonts, every DPI/display mode, client restart and full live interaction regressions still need the manual checklist above; passing compile/layout tests alone does not establish Windows visual correctness.

## File manifest

Modified existing files:

```text
Framework/Intersect.Framework.Core/GameObjects/Monsters/OwnedMonsterSummary.cs
Intersect.Client.Core/Interface/Game/MenuContainer.cs
Intersect.Client.Core/Interface/Game/Monsters/MonsterRosterWindow.cs
Intersect.Client.Core/Localization/Strings.Monsters.cs
Intersect.Server.Core/Monsters/MonsterOwnershipService.cs
Intersect.Server.Core/Monsters/MonsterRoster.cs
Intersect.Tests.Client/Monsters/MonsterRosterLayoutTests.cs
Intersect.Tests.Server/GameObjects/MonsterIndividualTests.cs
```

Added files:

```text
Framework/Intersect.Framework.Core/GameObjects/Monsters/OwnedMonsterDetails.cs
Intersect.Client.Core/Interface/Game/MenuContainer.Layout.cs
Intersect.Server.Core/Monsters/MonsterRosterDetails.cs
docs/monster-details-ui.md
```
