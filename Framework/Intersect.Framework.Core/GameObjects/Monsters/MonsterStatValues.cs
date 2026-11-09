namespace Intersect.Framework.Core.GameObjects.Monsters;

/// <summary>Immutable six-stat values, independent of the player RPG stat arrays.</summary>
public sealed record MonsterStatValues(long HP = 0, long Attack = 0, long Defense = 0, long SpecialAttack = 0, long SpecialDefense = 0, long Speed = 0)
{
    public long[] ToArray() => [HP, Attack, Defense, SpecialAttack, SpecialDefense, Speed];
    public long Get(MonsterStat stat) => stat switch
    {
        MonsterStat.HP => HP, MonsterStat.Attack => Attack, MonsterStat.Defense => Defense,
        MonsterStat.SpecialAttack => SpecialAttack, MonsterStat.SpecialDefense => SpecialDefense, MonsterStat.Speed => Speed,
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };
    public MonsterStatValues With(MonsterStat stat, long value) => stat switch
    {
        MonsterStat.HP => this with { HP = value }, MonsterStat.Attack => this with { Attack = value },
        MonsterStat.Defense => this with { Defense = value }, MonsterStat.SpecialAttack => this with { SpecialAttack = value },
        MonsterStat.SpecialDefense => this with { SpecialDefense = value }, MonsterStat.Speed => this with { Speed = value },
        _ => throw new ArgumentOutOfRangeException(nameof(stat)),
    };
}
