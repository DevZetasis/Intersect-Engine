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
        var gender = monster.GenderId is { } genderId ? MonsterGenderDescriptor.Get(genderId) : null;
        var details = new OwnedMonsterDetails
        {
            GenderId = monster.GenderId, GenderName = gender?.Name, GenderSymbol = gender?.Symbol,
            StatusName = monster.StatusId is { } statusId ? MonsterStatusDescriptor.Get(statusId)?.Name ?? "" : "",
            ConditionInitialized = monster.ConditionInitialized, CurrentHP = monster.CurrentHP, MaximumHP = monster.MaximumHPAtLastUpdate,
            Moves = monster.MovePP.Select((p, slot) => new OwnedMonsterMovePP { Slot = slot, MoveId = p.MoveId,
                Name = p.MoveId is { } id ? MonsterMoveDescriptor.Get(id)?.Name ?? "Missing move" : "Empty slot", CurrentPP = p.Current, MaximumPP = p.Maximum }).ToArray(),
            NatureId = monster.NatureId, NatureName = nature?.Name,
            IVs = monster.IVs.ToArray(), EVs = monster.EVs.ToArray(),
            AtLevelCap = monster.Level >= rules.MaxLevel,
            PendingMove = DescribeMove(monster.Learning.FirstOrDefault()),
        };
        try
        {
            if (species == null || species.Id != monster.SpeciesId ||
                (monster.NatureId != null && nature?.Id != monster.NatureId))
                throw new InvalidOperationException("Referenced species/nature definition is missing.");
            details.CalculatedStats = MonsterStatCalculator.Calculate(species.BaseStats, monster.Level,
                monster.IVs, monster.EVs, nature, rules with { MaxLevel = Math.Max(rules.MaxLevel, monster.Level) }).ToArray();
            if (monster.Level <= rules.MaxLevel)
                details.LevelStartExperience = MonsterExperienceCurveDescriptor.ForSpecies(species, monster.Level);
            if (!details.AtLevelCap)
                details.NextLevelExperience = MonsterExperienceCurveDescriptor.ForSpecies(species, monster.Level + 1);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            // No account data, stack traces or database information belongs in the private UI payload.
            details.CalculationError = "Calculated stats unavailable. Check the monster definitions and server rules.";
        }
        return details;
    }

    private static OwnedMonsterMoveLearning? DescribeMove(MonsterMoveLearning? decision)
    {
        if (decision == null) return null;
        if (MonsterMoveDescriptor.Get(decision.MoveId) is not { } move)
            return new() { DecisionId = decision.Id, Name = "Unavailable move" };
        return new()
        {
            DecisionId = decision.Id, Available = true, Name = move.Name,
            Type = MonsterTypeDescriptor.Get(move.TypeId)?.Name ?? "", Category = move.Category.ToString(),
            Power = move.Category == MonsterMoveCategory.Status ? null : move.BasePower,
            Accuracy = move.Accuracy, MaximumPP = move.MaximumPP, Description = move.Description,
        };
    }
}
