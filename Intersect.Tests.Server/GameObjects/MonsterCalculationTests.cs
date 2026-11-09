using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Monsters.Calculations;
using NUnit.Framework;

namespace Intersect.Tests.Server.GameObjects;

internal sealed class ControlledMonsterRandom(params long[] values) : IMonsterRandom
{
    private readonly Queue<long> _values = new(values);
    public List<long> Bounds { get; } = new();
    public long NextInt64(long bound) { Bounds.Add(bound); return _values.Count > 0 ? _values.Dequeue() : throw new InvalidOperationException("Unexpected random draw."); }
}

[TestFixture, NonParallelizable]
public class MonsterCalculationTests
{
    private static readonly MonsterBattleRules Rules = MonsterBattleRules.Generation4;
    private static MonsterNatureDescriptor Nature(MonsterStat up, decimal increase, MonsterStat down = MonsterStat.None, decimal decrease = 10) => new(Guid.NewGuid())
    {
        Modifiers = new() { Increase = new() { Stat = up, Percentage = increase }, Decrease = new() { Stat = down, Percentage = decrease } },
    };
    [Test]
    public void IvsUseSixControlledUniformDrawsIncludingZeroAndThirtyOne()
    {
        var random = new ControlledMonsterRandom(0, 31, 7, 8, 9, 10);
        Assert.That(MonsterAttributeGeneration.GenerateIVs(random), Is.EqualTo(new MonsterStatValues(0, 31, 7, 8, 9, 10)));
        Assert.That(random.Bounds, Is.EqualTo(Enumerable.Repeat(32L, 6)));
        Assert.Throws<InvalidOperationException>(() => MonsterAttributeGeneration.GenerateIVs(new ControlledMonsterRandom(32)));
    }
    [Test]
    public void NatureWeightsUseExactBoundariesAndNoDrawForEmptyOrAllZeroCatalogs()
    {
        var natures = Enumerable.Range(1, 4).Select(index => new MonsterNatureDescriptor(Guid.Parse($"00000000-0000-0000-0000-{index:D12}"))
            { SelectionWeight = new[] { 100, 25, 5, 0 }[index - 1] }).ToArray();
        foreach (var (roll, index) in new[] { (0, 0), (99, 0), (100, 1), (124, 1), (125, 2), (129, 2) })
        {
            var random = new ControlledMonsterRandom(roll);
            Assert.That(MonsterAttributeGeneration.SelectNature(natures.Reverse(), random), Is.EqualTo(natures[index].Id));
            Assert.That(random.Bounds.Single(), Is.EqualTo(130));
        }
        var noDraw = new ControlledMonsterRandom();
        Assert.That(MonsterAttributeGeneration.SelectNature(Array.Empty<MonsterNatureDescriptor>(), noDraw), Is.Null);
        Assert.That(MonsterAttributeGeneration.SelectNature(natures.Where(nature => nature.SelectionWeight == 0), noDraw), Is.Null);
        Assert.That(noDraw.Bounds, Is.Empty);
    }
    [Test]
    public void Gen4ReferenceStatsAndCustomNatureRoundingAreExact()
    {
        // Independently checked against Smogon's DP stats examples: HP 95/31/156 and Attack 135/31/252.
        var basis = new MonsterBaseStats { HP = 95, Attack = 135, Defense = 80, SpecialAttack = 100, SpecialDefense = 90, Speed = 110 };
        var ivs = new MonsterStatValues(31, 31, 31, 31, 31, 31);
        var evs = new MonsterStatValues(156, 252);
        var nature = Nature(MonsterStat.Attack, 10, MonsterStat.SpecialAttack, 10);
        Assert.That(MonsterStatCalculator.Calculate(basis, 100, ivs, evs, nature, Rules), Is.EqualTo(new MonsterStatValues(370, 405, 196, 212, 216, 256)));
        Assert.That(MonsterStatCalculator.Calculate(basis, 50, ivs, evs, nature, Rules), Is.EqualTo(new MonsterStatValues(190, 205, 100, 108, 110, 130)));
        var flat = new MonsterBaseStats { HP = 100, Attack = 100, Defense = 100, SpecialAttack = 100, SpecialDefense = 100, Speed = 100 };
        var custom = Nature(MonsterStat.Attack, 15.5m, MonsterStat.Defense, 15.5m);
        var calculated = MonsterStatCalculator.Calculate(flat, 100, new(), new(), custom, Rules);
        Assert.That((calculated.HP, calculated.Attack, calculated.Defense), Is.EqualTo((310L, 236L, 173L)));
        Assert.That(MonsterStatCalculator.Calculate(flat, 100, new(), new(Attack: 3), null, Rules).Attack, Is.EqualTo(205));
        Assert.That(MonsterStatCalculator.Calculate(flat, 100, new(), new(Attack: 4), null, Rules).Attack, Is.EqualTo(206));
        Assert.That(MonsterStatCalculator.Calculate(flat, 100, new(), new(), new MonsterNatureDescriptor(), Rules),
            Is.EqualTo(MonsterStatCalculator.Calculate(flat, 100, new(), new(), null, Rules)));
    }
    [Test]
    public void InvalidInputsAndRulesRejectSafelyAndLargeBaseStatsDoNotWrap()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterStatCalculator.ValidateIVs(new(HP: 32)));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterStatCalculator.ValidateEVs(new(Attack: 256), Rules));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterStatCalculator.ValidateEVs(new(HP: 255, Attack: 255, Defense: 1), Rules));
        Assert.DoesNotThrow(() => MonsterStatCalculator.ValidateEVs(new(HP: 255, Attack: 255), Rules));
        foreach (var invalid in new[] { Rules with { MaxLevel = 0 }, Rules with { MaxEvTotal = 1531 }, Rules with { StabMultiplier = 0 },
                     Rules with { MinimumDamagePercent = 101 }, Rules with { MaximumDamagePercent = 80 } })
            Assert.Throws<ArgumentException>(() => invalid.Validate());
        Assert.Throws<ArgumentException>(() => MonsterStatCalculator.Calculate(new(), 101, new(), new(), null, Rules));
        var high = MonsterStatCalculator.Calculate(new() { HP = int.MaxValue }, 10000, new(), new(), null, Rules with { MaxLevel = 10000 });
        Assert.That(high.HP, Is.EqualTo(429496739410L));
        Assert.That(Rules.MaxLevel, Is.EqualTo(100), "Reference rules remain independent of custom rules.");
        Assert.DoesNotThrow(() => MonsterStatCalculator.ValidateEVs(new(HP: 255, Attack: 255, Defense: 255), Rules with { MaxEvTotal = 1000 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterStatCalculator.ValidateEVs(new(HP: 101), Rules with { MaxEvPerStat = 100, MaxEvTotal = 500 }));
    }
    private static (MonsterCalculationActor Attacker, MonsterCalculationActor Defender, MonsterMoveDescriptor Move, MonsterTypeDescriptor Type) Example()
    {
        var type = new MonsterTypeDescriptor(Guid.NewGuid());
        var defending = Guid.NewGuid(); type.Effectiveness[defending] = 2;
        var attacker = new MonsterCalculationActor(Guid.NewGuid(), 47, new(100, 140, 50, 210, 50, 100), new[] { type.Id });
        var defender = new MonsterCalculationActor(Guid.NewGuid(), 47, new(100, 50, 77, 50, 100, 100), new[] { defending });
        return (attacker, defender, new MonsterMoveDescriptor(Guid.NewGuid()) { TypeId = type.Id, BasePower = 60 }, type);
    }
    [Test]
    public void PublishedDppDamageExampleHasCorrectInclusiveRangeAndSpecialStats()
    {
        var (a, d, move, type) = Example();
        Assert.That(MonsterBattleCalculator.Damage(a, d, move, type, Rules, new(), false, new ControlledMonsterRandom(0)), Is.EqualTo(114));
        Assert.That(MonsterBattleCalculator.Damage(a, d, move, type, Rules, new(), false, new ControlledMonsterRandom(15)), Is.EqualTo(134));
        move.Category = MonsterMoveCategory.Special;
        Assert.That(MonsterBattleCalculator.Damage(a, d, move, type, Rules, new(), false, new ControlledMonsterRandom(0)), Is.EqualTo(132));
        Assert.That(MonsterBattleCalculator.Damage(a, d, move, type, Rules, new(), false, new ControlledMonsterRandom(15)), Is.EqualTo(156));
        move.Category = MonsterMoveCategory.Status;
        Assert.That(MonsterBattleCalculator.Damage(a, d, move, type, Rules, new(), false, new ControlledMonsterRandom()), Is.Zero);
    }
    [Test]
    public void CriticalAndExtensionPhaseOrderAndStageExceptionsArePreserved()
    {
        var (a, d, move, type) = Example();
        // 43 + 2 = 45; critical = 90; 86% floors to 77; STAB floors to 115; weakness = 230.
        Assert.That(MonsterBattleCalculator.Damage(a, d, move, type, Rules, new(), true, new ControlledMonsterRandom(1)), Is.EqualTo(230));
        Assert.That(MonsterBattleCalculator.Damage(a, d, move, type, Rules, new() { AttackStage = -2, DefenseStage = 2 }, false, new ControlledMonsterRandom(15)), Is.EqualTo(36));
        Assert.That(MonsterBattleCalculator.Damage(a, d, move, type, Rules, new() { AttackStage = -6, DefenseStage = 6 }, true, new ControlledMonsterRandom(15)), Is.EqualTo(270));
        Assert.That(MonsterBattleCalculator.Damage(a, d, move, type, Rules, new() { AttackStage = 1, DefenseStage = -1 }, true, new ControlledMonsterRandom(15)), Is.EqualTo(600));
        Assert.That(MonsterBattleCalculator.Damage(a, d, move, type, Rules, new() { BeforeAddTwo = new[] { .5m } }, false, new ControlledMonsterRandom(15)), Is.EqualTo(68));
        Assert.That(MonsterBattleCalculator.Damage(a, d, move, type, Rules, new() { AfterCritical = new[] { .5m } }, false, new ControlledMonsterRandom(15)), Is.EqualTo(66));
        move.Category = MonsterMoveCategory.Special;
        Assert.That(MonsterBattleCalculator.Damage(a, d, move, type, Rules, new() { SpecialAttackStage = -6, SpecialDefenseStage = 6 }, true, new ControlledMonsterRandom(15)), Is.EqualTo(312));
    }
    [Test]
    public void DualTypesImmunityMinimumDamageAndCustomRulesWorkWithoutModernModifierCombining()
    {
        var (a, d, move, type) = Example();
        var resistant = Guid.NewGuid(); type.Effectiveness[resistant] = .5;
        var weak = d.TypeIds[0];
        Assert.That(MonsterBattleCalculator.Damage(a, d with { TypeIds = new[] { resistant, weak } }, move, type, Rules, new(), false, new ControlledMonsterRandom(15)), Is.EqualTo(66));
        Assert.That(MonsterBattleCalculator.Damage(a, d with { TypeIds = new[] { weak, resistant } }, move, type, Rules, new(), false, new ControlledMonsterRandom(15)), Is.EqualTo(67));
        type.Effectiveness[resistant] = 0;
        Assert.That(MonsterBattleCalculator.Damage(a, d with { TypeIds = new[] { weak, resistant } }, move, type, Rules, new(), false, new ControlledMonsterRandom()), Is.Zero);
        var neutral = d with { TypeIds = Array.Empty<Guid>(), Stats = new(100, 1, 10000, 1, 10000, 1) };
        Assert.That(MonsterBattleCalculator.Damage(a with { Stats = new(1, 1, 1, 1, 1, 1), TypeIds = Array.Empty<Guid>() }, neutral,
            move, type, Rules, new(), false, new ControlledMonsterRandom(0)), Is.EqualTo(1));
        var custom = Rules with { StabMultiplier = 2, MinimumDamagePercent = 100, MaximumDamagePercent = 100 };
        Assert.That(MonsterBattleCalculator.Damage(a, d, move, type, custom, new(), false, new ControlledMonsterRandom(0)), Is.EqualTo(180));
        Assert.Throws<ArgumentException>(() => MonsterBattleCalculator.Damage(a, d with { Stats = new(Defense: 0) }, move, type, Rules, new(), false, new ControlledMonsterRandom()));
        Assert.Throws<OverflowException>(() => MonsterBattleCalculator.Damage(a with { Stats = new(long.MaxValue, long.MaxValue, 1, 1, 1, 1) },
            d with { Stats = new(1, 1, 1, 1, 1, 1) }, move, type, Rules, new(), false, new ControlledMonsterRandom(15)));
    }
    [Test]
    public void AccuracyUsesCombinedClampedGen4StagesAndExplicitAlwaysHits()
    {
        var (_, _, move, _) = Example();
        move.Accuracy = 100;
        Assert.That(MonsterBattleCalculator.AccuracyThreshold(move, -1, 2), Is.EqualTo(50));
        Assert.That(MonsterBattleCalculator.AccuracyThreshold(move, 6, -6), Is.EqualTo(100));
        Assert.That(MonsterBattleCalculator.Hits(move, -1, 2, new ControlledMonsterRandom(49)), Is.True);
        Assert.That(MonsterBattleCalculator.Hits(move, -1, 2, new ControlledMonsterRandom(50)), Is.False);
        move.Accuracy = 1;
        Assert.That(MonsterBattleCalculator.AccuracyThreshold(move, -6, 0), Is.Zero);
        move.AlwaysHits = true;
        Assert.That(MonsterBattleCalculator.Hits(move, -6, 6, new ControlledMonsterRandom()), Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterBattleCalculator.AccuracyThreshold(move, 7, 0));
    }
    [Test]
    public void EveryGen4CriticalStageHasDeterministicBoundaryChecks()
    {
        foreach (var (stage, bound) in new[] { (0, 16), (1, 8), (2, 4), (3, 3), (4, 2), (9, 2) })
        {
            var random = new ControlledMonsterRandom(0, bound - 1);
            Assert.That(MonsterBattleCalculator.CriticalHit(stage, random), Is.True);
            Assert.That(MonsterBattleCalculator.CriticalHit(stage, random), Is.False);
            Assert.That(random.Bounds, Is.EqualTo(new long[] { bound, bound }));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => MonsterBattleCalculator.CriticalHit(-1, new ControlledMonsterRandom()));
    }
    [Test]
    public void PrioritySpeedAndUnbiasedTieShuffleSupportArbitraryActorCounts()
    {
        var a = new MonsterMoveAction(Guid.NewGuid(), 0, 100);
        var b = new MonsterMoveAction(Guid.NewGuid(), 1, 1);
        var c = new MonsterMoveAction(Guid.NewGuid(), 0, 200);
        var d = new MonsterMoveAction(Guid.NewGuid(), -1, 999);
        Assert.That(MonsterBattleCalculator.OrderActions(new[] { a, b, c, d }, new ControlledMonsterRandom()), Is.EqualTo(new[] { b, c, a, d }));
        var tied = b with { Priority = 0, EffectiveSpeed = 100 };
        Assert.That(MonsterBattleCalculator.OrderActions(new[] { a, tied }, new ControlledMonsterRandom(0)), Is.EqualTo(new[] { tied, a }));
        Assert.That(MonsterBattleCalculator.OrderActions(new[] { a, tied }, new ControlledMonsterRandom(1)), Is.EqualTo(new[] { a, tied }));
        var four = new[] { a, tied, c with { EffectiveSpeed = 100 }, d with { Priority = 0, EffectiveSpeed = 100 } };
        var random = new ControlledMonsterRandom(0, 0, 0);
        Assert.That(MonsterBattleCalculator.OrderActions(four, random), Is.EquivalentTo(four));
        Assert.That(random.Bounds, Is.EqualTo(new long[] { 4, 3, 2 }));
        Assert.That(MonsterBattleCalculator.ApplyStatStage(100, -1), Is.EqualTo(66));
    }
}
