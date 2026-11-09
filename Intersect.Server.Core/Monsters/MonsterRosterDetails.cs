using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Database.PlayerData.Players;
using Intersect.Server.Monsters.Calculations;

namespace Intersect.Server.Monsters;

public static class MonsterRosterDetails
{
    public static OwnedMonsterDetails Create(MonsterInstance monster, MonsterSpeciesDescriptor? species,
        MonsterNatureDescriptor? nature, MonsterBattleRules rules)
    {
        var details = new OwnedMonsterDetails
        {
            NatureId = monster.NatureId, NatureName = nature?.Name,
            IVs = monster.IVs.ToArray(), EVs = monster.EVs.ToArray(),
        };
        try
        {
            if (species == null || species.Id != monster.SpeciesId ||
                (monster.NatureId != null && nature?.Id != monster.NatureId))
                throw new InvalidOperationException("Referenced species/nature definition is missing.");
            details.CalculatedStats = MonsterStatCalculator.Calculate(species.BaseStats, monster.Level,
                monster.IVs, monster.EVs, nature, rules).ToArray();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            // No account data, stack traces or database information belongs in the private UI payload.
            details.CalculationError = "Calculated stats unavailable. Check the monster definitions and server rules.";
        }
        return details;
    }
}
