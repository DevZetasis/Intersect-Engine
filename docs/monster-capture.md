# Monster capturing

A capture attempt consumes one inventory device and the turn. If the monster breaks free, the wild opponent gets its normal action and end-of-turn effects run. Successful captures preserve the generated individual and its current HP, move slots, PP, and persistent status. The first open party slot is used, then the first open storage slot.

- Species use a base chance of 0–100%, with 40% as the default for new and existing data. Base 0% always fails with ordinary devices. Base 100% always succeeds in an eligible battle.
- Capture device multipliers are 0.01×–10.00×, with two decimal places and a 1.00× default. Guaranteed devices bypass the species chance and the normal cap within an eligible battle.
- Status modifiers range from 0.00× to 5.00× and default to 1.00×. Only the highest active modifier is used. Sleep and Freeze are seeded at 2.00× and Poison, Sick, Burn, and Paralysis at 1.50× in the disposable world. Existing authored statuses migrate to 1.00×.
- The server calculates `base × (1 + (1 − current HP / max HP) × 0.5) × status × device` in decimal, with a 95% cap on ordinary rolls. For 40% base, 75% HP, and 2.00× status, the 1.00× device yields 90% and a 1.50× device yields 95% after the cap. The server draws a uniform integer from 0 through 99,999,999 and compares it with the unrounded chance in percent times 1,000,000. Each roll step is 0.000001 percentage points.
- Random wild encounters permit capture. Existing Start Monster Battle commands default to capture prohibited. Edit and save a command to add its On Capture branch and enable it when appropriate. The server commits the inventory deduction and ownership in one transaction. Event branches still wait for the result window to close. If that acknowledgment is abandoned, the committed capture stays in the roster and the branch is abandoned under the existing event policy.

## Disposable Windows test world

Use the matching Debug builds with protocol `monster-battles-v12`. The following commands start the seeded SQLite world at `127.0.0.1:55974`. Open one PowerShell for each application and set the two paths in each.

```powershell
$artifacts = 'C:\Users\calip\.codex\visualizations\2026\11\08\01a1b8fc-bacb-7d70-84ef-7c44b21ea2a0'
$build = Join-Path $artifacts 'm6ja-build'
$world = Join-Path $artifacts 'm6ja-live-55974'
```

Server

```powershell
Set-Location (Join-Path $world 'server')
$env:Api__Enabled = 'false'
& (Join-Path $build 'Server\Intersect Server.exe') --no-upnp --no-port-check --migrate-automatically
```

Client

```powershell
Set-Location (Join-Path $world 'client')
& (Join-Path $build 'Client\Intersect Client.exe')
```

Editor

```powershell
Set-Location (Join-Path $world 'client')
& (Join-Path $build 'Editor\Intersect Editor.exe')
```

1. Use New Account in the client to create `capturetester` with a password you choose and an address such as `capturetester@example.invalid`. Create `CaptureTrainer` with the default class. To use that account in the editor, run `poweracc capturetester 2` in the disposable server console, then use the same credentials in the editor.
2. Enter the following commands in the **server console** while the character is online and outside battle. Keep the starting monster's printed individual UUID for the practice check and healing. The lead has Apply Sleep, Apply Freeze, Clear Condition, and Sparring Strike.

   ```text
   grantmonster "CaptureTrainer" 6b000000-0000-4000-8000-000000000203 6a060000-0000-4000-8000-000000000101
   grantcapturedevice "CaptureTrainer" 6a060000-0000-4000-8000-000000000001 5
   grantcapturedevice "CaptureTrainer" 6a060000-0000-4000-8000-000000000002 5
   grantcapturedevice "CaptureTrainer" 6a060000-0000-4000-8000-000000000003 5
   ```

3. Spawn is at (2,2). Interact from an adjacent tile with the healer at (3,2), Common Mossling at (5,2), Rare Emberling at (6,2), Trial Pebbling at (7,2), or Protected Mossling at (8,2). Trial Pebbling has 0% base chance. Use **Items → Basic Capture Device** against it. Observe the attempt, the monster breaking free, its action, and the next command menu. Reopen Items and check Basic went from ×5 to ×4. Try another eligible battle with no devices to verify the disabled Items label.
4. Run `inspectwild "CaptureTrainer"` during the Pebbling battle and record its individual UUID, species, level, IVs, nature, gender, and moves. Use the Guaranteed device. Verify the capture sequence, party placement, one device consumed, and no opposing attack. The **On Capture** event dialogue should appear only after **Return to game**. Dismiss the dialogue and verify Trial Pebbling disappears for that character. Run `inspectmonster <recorded-individual-UUID>` and compare the printed data, the roster and the inventory after reconnecting.
5. Create a second account and character in this world. Repeat the grant commands for that character, using a **different** grant request UUID. Trial Pebbling should still be visible and give that player a different wild UUID. On either character, attempt to use a guaranteed device against Protected Mossling. Verify Items is prohibited and the quantity stays the same. Run should still be enabled. Start a console practice battle with `startmonsterbattle "CaptureTrainer" <starting-individual-UUID> 6f000000-0000-4000-8000-000000000004 3` and verify capture and Run are both prohibited.
6. In the editor, open Monster Status and choose Sleep. In Capture Settings the default preview sample gives 45% without status and 90% with 2.00× Sleep. Change only **Preview Device Multiplier** to 1.50× to see 95%. In battle, weaken Rare Emberling without fainting it and apply Sleep or Freeze before using an ordinary device. Compare the HP/status panel with the preview's inputs. The rare species remains probabilistic at 8% base. The editor also exposes species Base Capture Chance and the existing Item Editor's Monster Capture Device fields. Use Save, reopen the editor, and verify the settings round-trip.
7. Fill any remaining party slots using the following commands until you reach six. Capture Common Mossling with a Guaranteed device. Verify its result and roster show Storage, with no replacement prompt.

   ```text
   grantmonster "CaptureTrainer" 6f000000-0000-4000-8000-000000000001 6a060000-0000-4000-8000-000000000102
   grantmonster "CaptureTrainer" 6f000000-0000-4000-8000-000000000001 6a060000-0000-4000-8000-000000000103
   grantmonster "CaptureTrainer" 6f000000-0000-4000-8000-000000000001 6a060000-0000-4000-8000-000000000104
   grantmonster "CaptureTrainer" 6f000000-0000-4000-8000-000000000001 6a060000-0000-4000-8000-000000000105
   ```

8. Walk through the painted grass below the starting row (inside x=2–8, y=4–8) for a random encounter. Verify Items works and Run remains available before the attempt. Catch another monster or escape. Check Victory and Defeat through the event battles when desired, using the healer between tests.
9. Close the disposable client, type `exit` in the disposable server console and relaunch the server with the same command. Reconnect and verify the captured UUIDs, party/storage placement, inventory counts, and Trial Pebbling's player-specific disappearance.

The automated tests cover the formula, forced failures and successes, battle action ordering, stale actions, acknowledgment gating, explicit zero persistence, storage, and capture rollback. The live desktop checks above verify rendered behavior and the full login, inventory, and event workflow.
