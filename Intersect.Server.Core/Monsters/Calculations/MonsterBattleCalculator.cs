using System.Numerics;
using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Server.Monsters.Calculations;

public sealed record MonsterCalculationActor(Guid Id, int Level, MonsterStatValues Stats, IReadOnlyList<Guid> TypeIds);
public sealed record MonsterBattleModifiers
{
    public int AttackStage { get; init; }
    public int DefenseStage { get; init; }
    public int SpecialAttackStage { get; init; }
    public int SpecialDefenseStage { get; init; }
    // Ordered, caller-supplied extension points; no burn/weather/item/ability systems are implemented.
    public IReadOnlyList<decimal> BeforeAddTwo { get; init; } = Array.Empty<decimal>();
    public IReadOnlyList<decimal> AfterCritical { get; init; } = Array.Empty<decimal>();
    public IReadOnlyList<decimal> AfterTypes { get; init; } = Array.Empty<decimal>();
}
public sealed record MonsterMoveAction(Guid ActorId, int Priority, long EffectiveSpeed);

/// <summary>Server-side Gen 4 core subset, not a move executor or complete battle simulator.</summary>
public static class MonsterBattleCalculator
{
    public static void ValidateStage(int stage)
    {
        if (stage is < -6 or > 6) throw new ArgumentOutOfRangeException(nameof(stage));
    }
    public static long ApplyStatStage(long stat, int stage)
    {
        ValidateStage(stage);
        if (stat <= 0) throw new ArgumentOutOfRangeException(nameof(stat));
        var result = stage >= 0 ? (BigInteger)stat * (2 + stage) / 2 : (BigInteger)stat * 2 / (2 - stage);
        return Math.Max(1, MonsterExactMath.Result(result));
    }
    public static int AccuracyThreshold(MonsterMoveDescriptor move, int accuracyStage, int evasionStage)
    {
        ArgumentNullException.ThrowIfNull(move);
        if (!move.IsValid) throw new ArgumentException("Invalid move.");
        ValidateStage(accuracyStage);
        ValidateStage(evasionStage);
        if (move.AlwaysHits) return 100;
        // Platinum combines and clamps stages BEFORE applying the 3-based ratio.
        var stage = Math.Clamp(accuracyStage - evasionStage, -6, 6);
        return Math.Min(100, stage >= 0 ? move.Accuracy * (3 + stage) / 3 : move.Accuracy * 3 / (3 - stage));
    }
    public static bool Hits(MonsterMoveDescriptor move, int accuracyStage, int evasionStage, IMonsterRandom random)
    {
        var threshold = AccuracyThreshold(move, accuracyStage, evasionStage);
        return move.AlwaysHits || MonsterRandom.Draw(random, 100) < threshold;
    }
    public static bool CriticalHit(int stage, IMonsterRandom random)
    {
        if (stage < 0) throw new ArgumentOutOfRangeException(nameof(stage));
        var denominator = stage switch { 0 => 16, 1 => 8, 2 => 4, 3 => 3, _ => 2 };
        return MonsterRandom.Draw(random, denominator) == 0;
    }
    private static void ValidateActor(MonsterCalculationActor actor, MonsterBattleRules rules)
    {
        if (actor.Stats == null || actor.TypeIds == null || actor.Id == Guid.Empty || actor.Level < 1 || actor.Level > rules.MaxLevel || actor.Stats.ToArray().Any(value => value <= 0) ||
            actor.TypeIds.Count > 2 || actor.TypeIds.Any(id => id == Guid.Empty) || actor.TypeIds.Distinct().Count() != actor.TypeIds.Count)
            throw new ArgumentException("Invalid monster calculation participant.");
    }
    public static long Damage(MonsterCalculationActor attacker, MonsterCalculationActor defender, MonsterMoveDescriptor move,
        MonsterTypeDescriptor moveType, MonsterBattleRules rules, MonsterBattleModifiers modifiers, bool critical, IMonsterRandom random)
    {
        ArgumentNullException.ThrowIfNull(attacker); ArgumentNullException.ThrowIfNull(defender);
        ArgumentNullException.ThrowIfNull(move); ArgumentNullException.ThrowIfNull(moveType);
        ArgumentNullException.ThrowIfNull(rules); ArgumentNullException.ThrowIfNull(modifiers);
        rules.Validate();
        ValidateActor(attacker, rules); ValidateActor(defender, rules);
        if (!move.IsValid || !moveType.IsValid || move.TypeId != moveType.Id) throw new ArgumentException("Invalid move/type definition.");
        foreach (var stage in new[] { modifiers.AttackStage, modifiers.DefenseStage, modifiers.SpecialAttackStage, modifiers.SpecialDefenseStage }) ValidateStage(stage);
        foreach (var factor in modifiers.BeforeAddTwo.Concat(modifiers.AfterCritical).Concat(modifiers.AfterTypes))
            if (factor is <= 0 or > 10) throw new ArgumentOutOfRangeException(nameof(modifiers), "Extension multipliers must be positive and at most 10.");
        if (move.Category == MonsterMoveCategory.Status) return 0;
        if (defender.TypeIds.Any(id => moveType.Effectiveness.GetValueOrDefault(id, 1) == 0)) return 0;
        var physical = move.Category == MonsterMoveCategory.Physical;
        var attackStage = physical ? modifiers.AttackStage : modifiers.SpecialAttackStage;
        var defenseStage = physical ? modifiers.DefenseStage : modifiers.SpecialDefenseStage;
        if (critical) { attackStage = Math.Max(0, attackStage); defenseStage = Math.Min(0, defenseStage); }
        var attack = ApplyStatStage(physical ? attacker.Stats.Attack : attacker.Stats.SpecialAttack, attackStage);
        var defense = ApplyStatStage(physical ? defender.Stats.Defense : defender.Stats.SpecialDefense, defenseStage);
        BigInteger damage = ((2 * (BigInteger)attacker.Level / 5 + 2) * move.BasePower * attack) / 50 / defense;
        foreach (var factor in modifiers.BeforeAddTwo) damage = MonsterExactMath.Multiply(damage, factor);
        damage += 2;
        if (critical) damage = MonsterExactMath.Multiply(damage, rules.CriticalMultiplier);
        foreach (var factor in modifiers.AfterCritical) damage = MonsterExactMath.Multiply(damage, factor);
        var roll = rules.MinimumDamagePercent + MonsterRandom.Draw(random, rules.MaximumDamagePercent - rules.MinimumDamagePercent + 1L);
        damage = damage * roll / 100;
        if (attacker.TypeIds.Contains(move.TypeId)) damage = MonsterExactMath.Multiply(damage, rules.StabMultiplier);
        // Custom type systems use the species' explicit primary/secondary order, with a floor at each step.
        foreach (var type in defender.TypeIds) damage = MonsterExactMath.Multiply(damage, (decimal)moveType.Effectiveness.GetValueOrDefault(type, 1));
        foreach (var factor in modifiers.AfterTypes) damage = MonsterExactMath.Multiply(damage, factor);
        return Math.Max(1, MonsterExactMath.Result(damage));
    }
    public static IReadOnlyList<MonsterMoveAction> OrderActions(IEnumerable<MonsterMoveAction> actions, IMonsterRandom random)
    {
        ArgumentNullException.ThrowIfNull(actions);
        var all = actions.ToArray();
        if (all.Any(action => action == null || action.ActorId == Guid.Empty || action.EffectiveSpeed <= 0) || all.Select(action => action.ActorId).Distinct().Count() != all.Length)
            throw new ArgumentException("Actions need distinct actor UUIDs and positive effective Speed.");
        var result = new List<MonsterMoveAction>();
        foreach (var group in all.OrderByDescending(action => action.Priority).ThenByDescending(action => action.EffectiveSpeed)
                     .GroupBy(action => (action.Priority, action.EffectiveSpeed)))
        {
            var tied = group.ToArray();
            for (var i = tied.Length - 1; i > 0; i--)
            {
                var j = (int)MonsterRandom.Draw(random, i + 1L);
                (tied[i], tied[j]) = (tied[j], tied[i]);
            }
            result.AddRange(tied);
        }
        return result;
    }
}
