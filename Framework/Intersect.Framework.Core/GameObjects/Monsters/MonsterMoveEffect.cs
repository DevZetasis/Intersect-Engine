using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Monsters;

public enum MonsterMoveEffectKind { StatStage = 0, RestoreHP = 1 }
public enum MonsterMoveEffectTarget { Self = 0, Opponent = 1 }
public enum MonsterHealingMethod { Fixed = 0, Percentage = 1 }

/// <summary>An ordered, authored effect. Amount is a signed stage delta, HP, or a whole percent.</summary>
public sealed record MonsterMoveEffect
{
    public MonsterMoveEffectKind Kind { get; init; }
    public MonsterMoveEffectTarget Target { get; init; }
    public MonsterStat Stat { get; init; } = MonsterStat.Attack;
    public MonsterHealingMethod HealingMethod { get; init; }
    public int Amount { get; init; } = 1;

    public static bool SupportsStage(MonsterStat stat) => stat is >= MonsterStat.Attack and <= MonsterStat.Speed;

    [JsonIgnore]
    public bool IsValid => Kind switch
    {
        MonsterMoveEffectKind.StatStage => Enum.IsDefined(Target) && SupportsStage(Stat) &&
            Amount is >= -6 and <= 6 and not 0,
        MonsterMoveEffectKind.RestoreHP => Target == MonsterMoveEffectTarget.Self && HealingMethod switch
        {
            MonsterHealingMethod.Fixed => Amount > 0,
            MonsterHealingMethod.Percentage => Amount is >= 1 and <= 100,
            _ => false,
        },
        _ => false,
    };
}
