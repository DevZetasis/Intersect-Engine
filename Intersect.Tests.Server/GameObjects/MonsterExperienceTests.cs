using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;
using Intersect.Server.Monsters.Battles;
using Intersect.Server.Monsters.Calculations;
using NUnit.Framework;

namespace Intersect.Tests.Server.GameObjects;

[TestFixture]
public sealed class MonsterExperienceTests
{
    [TestCase(MonsterGrowthPreset.Fast, 6L, 800000L)]
    [TestCase(MonsterGrowthPreset.MediumFast, 8L, 1000000L)]
    [TestCase(MonsterGrowthPreset.MediumSlow, 9L, 1059860L)]
    [TestCase(MonsterGrowthPreset.Slow, 10L, 1250000L)]
    public void PresetsStartAtZeroAndIncreaseToSupportedCap(MonsterGrowthPreset preset, long second, long hundred)
    {
        Assert.That(MonsterExperience.Threshold(preset, 1), Is.Zero);
        Assert.That(MonsterExperience.Threshold(preset, 2), Is.EqualTo(second));
        Assert.That(MonsterExperience.Threshold(preset, 100), Is.EqualTo(hundred));
        var curve = new MonsterExperienceCurveDescriptor
        {
            Thresholds = Enumerable.Range(1, MonsterExperience.MaximumSupportedLevel)
                .Select(level => MonsterExperience.Threshold(preset, level)).ToArray(),
        };
        Assert.That(curve.IsValid(MonsterExperience.MaximumSupportedLevel), Is.True);
    }

    [Test]
    public void CustomThresholdsValidateCoverageAndRoundTripWithLongValues()
    {
        var curve = new MonsterExperienceCurveDescriptor(Guid.NewGuid()) { Thresholds = [0, 10, long.MaxValue] };
        var copy = new MonsterExperienceCurveDescriptor(Guid.NewGuid());
        copy.Load(curve.JsonData);
        Assert.That(copy.Id, Is.Not.EqualTo(curve.Id));
        Assert.That(copy.Thresholds, Is.EqualTo(curve.Thresholds));
        Assert.That(copy.IsValid(3), Is.True);
        Assert.That(copy.IsValid(4), Is.False);
        copy.Thresholds = [0, 10, 10];
        Assert.That(copy.IsValid(3), Is.False);
        copy.Thresholds = [1, 2, 3];
        Assert.That(copy.IsValid(3), Is.False);
    }

    [TestCase(50, 5, 35)]
    [TestCase(50, 20, 142)]
    [TestCase(75, 20, 214)]
    public void BaseRewardMatchesExamples(int yield, int level, long result) =>
        Assert.That(MonsterExperience.Reward(yield, level, new(), 1, false, true, false), Is.EqualTo(result));

    [Test]
    public void DistributionCaptureAndEventMultipliersUseOneFinalFloor()
    {
        var settings = new MonsterExperienceOptions
        {
            GlobalMultiplier = 1.5m, ShareWithEntireParty = true, ParticipatingPercent = 125,
        };
        long Reward(MonsterExperienceOptions options, bool captured, bool participated, bool fainted, decimal multiplier = 1.4m) =>
            MonsterExperience.Reward(50, 5, options, multiplier, captured, participated, fainted);
        Assert.That(Reward(settings, false, true, false), Is.EqualTo(91));
        Assert.That(Reward(settings, true, true, false), Is.EqualTo(45));
        Assert.That(Reward(settings, true, false, false), Is.EqualTo(18));
        Assert.That(Reward(settings, false, false, true), Is.Zero);
        Assert.That(Reward(settings with { AllowFainted = true }, false, false, true), Is.EqualTo(36));
        Assert.That(Reward(settings with { ShareWithEntireParty = false }, false, false, false), Is.Zero);
        Assert.That(Reward(settings with { AwardExperienceForCaptures = false }, true, true, false), Is.Zero);
        Assert.That(Reward(settings with { GlobalMultiplier = 0 }, false, true, false), Is.Zero);
        Assert.That(Reward(settings, false, true, false, 0), Is.Zero);
        Assert.That(MonsterExperience.Reward(int.MaxValue, 10000,
            new() { ParticipatingPercent = 200, GlobalMultiplier = 10, CapturePercent = 200 },
            10, true, true, false), Is.EqualTo((long)int.MaxValue * 10000 / 7 * 400));
        Assert.Throws<ArgumentException>(() => Reward(settings, false, true, false, 1.001m));
    }

    [Test]
    public void OldEventAndSpeciesDataUseDocumentedDefaults()
    {
        var command = Newtonsoft.Json.JsonConvert.DeserializeObject<StartMonsterBattleCommand>("{}");
        Assert.That(command!.AwardExperience, Is.True);
        Assert.That(command.ExperienceMultiplier, Is.EqualTo(1m));
        var species = new MonsterSpeciesDescriptor(Guid.NewGuid()) { BaseExperienceYield = 99, GrowthPreset = MonsterGrowthPreset.Slow };
        species.Load("{}");
        Assert.That(species.BaseExperienceYield, Is.Zero);
        Assert.That(species.GrowthPreset, Is.EqualTo(MonsterGrowthPreset.MediumFast));
    }

    [TestCase(MonsterBattleOutcome.Escaped)]
    [TestCase(MonsterBattleOutcome.Forfeit)]
    [TestCase(MonsterBattleOutcome.Cancelled)]
    [TestCase(MonsterBattleOutcome.Defeat)]
    public void NonrewardResultsNeverInvokeTheVictoryCommit(MonsterBattleOutcome outcome)
    {
        var owner = Guid.NewGuid();
        var type = new MonsterTypeDescriptor(Guid.NewGuid());
        var wild = new GeneratedWildMonster(Guid.NewGuid(), Guid.NewGuid(), 1, new(), null, null, []);
        MonsterBattleCombatant Actor(Guid id, int power, int speed)
        {
            var move = new MonsterMoveDescriptor(Guid.NewGuid()) { TypeId = type.Id, AlwaysHits = true, BasePower = power };
            return new(new(id, 1, new(1, 10, 10, 10, 10, speed), [type.Id]), "Combatant", "",
                [new(move, type), null, null, null]);
        }

        var session = new MonsterBattleSession(new(owner, [Actor(Guid.NewGuid(), 1, 1)]),
            new(null, [Actor(wild.Id, 500, 99)]), MonsterBattleRules.Generation4, new LastRandom(), wild: wild)
        {
            PersistVictory = _ => throw new InvalidOperationException("Unexpected reward."),
        };
        var registry = new MonsterBattleRegistry((_, _) => { });
        registry.Start(owner, () => session);
        if (outcome == MonsterBattleOutcome.Cancelled) registry.Cancel(owner, "Cancelled");
        else registry.Submit(owner, new()
        {
            BattleId = session.Id, Revision = session.Revision, Turn = session.Turn, MoveSlot = 0,
            Kind = outcome switch
            {
                MonsterBattleOutcome.Escaped => MonsterBattleActionKind.Run,
                MonsterBattleOutcome.Forfeit => MonsterBattleActionKind.Forfeit,
                _ => MonsterBattleActionKind.Move,
            },
        });
        Assert.That(session.Outcome, Is.EqualTo(outcome));
        Assert.That(session.Snapshot(1).ExperienceMessages, Is.Empty);
    }

    private sealed class LastRandom : IMonsterRandom
    {
        public long NextInt64(long bound) => bound - 1;
    }
}
