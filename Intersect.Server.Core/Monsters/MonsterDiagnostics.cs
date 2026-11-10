using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Database.PlayerData.Players;
using Intersect.Server.Monsters.Calculations;

namespace Intersect.Server.Monsters;

public static class MonsterDiagnostics
{
    public static string Describe(MonsterInstance monster, MonsterSpeciesDescriptor? species, MonsterNatureDescriptor? nature, MonsterBattleRules rules)
    {
        var lines = new List<string>
        {
            $"Monster: {monster.Id}", $"Species: {species?.Name ?? "Missing definition"} ({monster.SpeciesId})",
            $"Level: {monster.Level}; Experience: {monster.Experience}",
            $"Placement: {monster.Location} #{monster.Slot + 1}",
            monster.NatureId == null ? "Nature: neutral/unassigned" : $"Nature: {nature?.Name ?? "Missing definition"} ({monster.NatureId})",
            "Order: HP / Attack / Defense / Special Attack / Special Defense / Speed",
            "Move slots: " + (monster.MovesInitialized ? string.Join(" / ", monster.MoveIds.Select(id => id?.ToString() ?? "Empty")) : "Pending one-time learnset initialization"),
            "IVs: " + string.Join(" / ", monster.IVs.ToArray()),
            "EVs: " + string.Join(" / ", monster.EVs.ToArray()),
        };
        try
        {
            if (species == null || (monster.NatureId != null && nature?.Id != monster.NatureId))
                throw new InvalidOperationException("Referenced species/nature definition is missing.");
            var stats = MonsterStatCalculator.Calculate(species.BaseStats, monster.Level, monster.IVs, monster.EVs, nature, rules);
            lines.Add("Calculated stats: " + string.Join(" / ", stats.ToArray()));
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        { lines.Add("Calculated stats unavailable: " + exception.Message); }
        return string.Join(Environment.NewLine, lines);
    }
}
