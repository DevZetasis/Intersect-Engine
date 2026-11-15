using Intersect.Core;
using Intersect.Framework.Core;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Monsters;
using Intersect.Server.Monsters.Battles;
using Intersect.Server.Monsters.Calculations;
using Intersect.Utilities;
using Microsoft.Extensions.Logging;

namespace Intersect.Server.Entities;

public partial class Player
{
    internal bool IsAvailableForMonsterPractice => InGame && Client != null && !IsDead && !IsBusy();

    internal bool IsAvailableForWildEncounter => IsAvailableForMonsterPractice && MoveRoute == null &&
        !IsCasting && CombatTimer <= Timing.Global.Milliseconds &&
        !EventLookup.Values.Any(evt => evt.HoldingPlayer || evt.CallStack.Count > 0);

    private readonly MonsterEncounterCooldown _encounterCooldown = new();
    private long _encounterTransition;
    internal long EncounterTransition => Interlocked.Read(ref _encounterTransition);
    internal void MarkEncounterTransition() => Interlocked.Increment(ref _encounterTransition);

    internal void CheckWildEncounter(Guid mapId, int x, int y, long transition)
    {
        // Read map data and start under the content/registry gate to avoid duplicate starts and stale table reads.
        lock (MonsterTypeRules.ContentGate)
        {
            if (MapId != mapId || X != x || Y != y || EncounterTransition != transition ||
                !IsAvailableForWildEncounter || MonsterPracticeBattles.Registry.HasActive(Id)) return;
            if (Map?.Encounters?.Resolve(x, y) is not { } tile ||
                MonsterEncounterTableDescriptor.Get(tile.TableId) is not { Enabled: true } table ||
                (tile.Rate ?? table.Rate) is not (> 0 and <= 100)) return;

            if (_encounterCooldown.ConsumeEligibleStep()) return;
            var random = new SystemMonsterRandom();
            var roll = MonsterEncounterSelection.Roll(table, tile.Rate, Options.Instance.Monsters.Battle.MaxLevel,
                speciesId => MonsterSpeciesDescriptor.Get(speciesId) != null, random);
            if (roll == null) return;

            try
            {
                if (MonsterPracticeBattles.TryStartWild(this, roll, random))
                {
                    _encounterCooldown.Start(table.Cooldown);
                    ApplicationContext.Context.Value?.Logger.LogDebug(
                        "Wild encounter for {PlayerId}, table {TableId}, species {SpeciesId}, level {Level}",
                        Id, roll.TableId, roll.SpeciesId, roll.Level);
                }
            }
            catch (InvalidOperationException error)
            {
                ApplicationContext.Context.Value?.Logger.LogDebug(error, "Wild encounter could not start for {PlayerId}", Id);
            }
            catch (Exception error)
            {
                ApplicationContext.Context.Value?.Logger.LogError(error, "Wild encounter startup failed for {PlayerId}", Id);
            }
        }
    }
}
