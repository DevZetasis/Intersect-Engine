using System.Numerics;
using Intersect.Config;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Server.Monsters.Calculations;

internal static class MonsterExactMath
{
    public static (BigInteger Numerator, BigInteger Denominator) Fraction(decimal value)
    {
        var bits = decimal.GetBits(value);
        var numerator = (BigInteger)(uint)bits[0] | ((BigInteger)(uint)bits[1] << 32) | ((BigInteger)(uint)bits[2] << 64);
        if ((bits[3] & int.MinValue) != 0) numerator = -numerator;
        return (numerator, BigInteger.Pow(10, (bits[3] >> 16) & 255));
    }
    public static BigInteger Multiply(BigInteger value, decimal multiplier)
    {
        if (value < 0 || multiplier < 0) throw new ArgumentOutOfRangeException(nameof(multiplier));
        var (n, d) = Fraction(multiplier);
        return value * n / d;
    }
    public static long Result(BigInteger value) => checked((long)value);
}

public static class MonsterStatCalculator
{
    public static void ValidateIVs(MonsterStatValues values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.ToArray().Any(value => value is < 0 or > 31)) throw new ArgumentOutOfRangeException(nameof(values), "IVs must be 0–31.");
    }
    public static void ValidateEVs(MonsterStatValues values, MonsterBattleRules rules)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();
        var array = values.ToArray();
        if (array.Any(value => value < 0 || value > rules.MaxEvPerStat) || array.Sum() > rules.MaxEvTotal)
            throw new ArgumentOutOfRangeException(nameof(values), "EVs exceed the configured per-stat or total limit.");
    }
    public static MonsterStatValues Calculate(MonsterBaseStats baseStats, int level, MonsterStatValues ivs,
        MonsterStatValues evs, MonsterNatureDescriptor? nature, MonsterBattleRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        rules.Validate();
        if (baseStats is not { IsValid: true } || level < 1 || level > rules.MaxLevel || nature is { IsValid: false })
            throw new ArgumentException("Invalid base stats, level or nature definition.");
        ValidateIVs(ivs);
        ValidateEVs(evs, rules);
        long Stat(int basis, MonsterStat stat)
        {
            var core = ((2 * (BigInteger)basis + ivs.Get(stat) + evs.Get(stat) / 4) * level) / 100;
            if (stat == MonsterStat.HP) return MonsterExactMath.Result(core + level + 10);
            core += 5;
            if (nature != null)
            {
                var increased = nature.Modifiers.Increase.Stat == stat;
                var decreased = nature.Modifiers.Decrease.Stat == stat;
                var magnitude = increased ? nature.Modifiers.Increase.Percentage : decreased ? nature.Modifiers.Decrease.Percentage : 0m;
                var (n, d) = MonsterExactMath.Fraction(magnitude);
                core = core * (100 * d + (decreased ? -n : n)) / (100 * d);
            }
            return MonsterExactMath.Result(core);
        }
        return new(Stat(baseStats.HP, MonsterStat.HP), Stat(baseStats.Attack, MonsterStat.Attack), Stat(baseStats.Defense, MonsterStat.Defense),
            Stat(baseStats.SpecialAttack, MonsterStat.SpecialAttack), Stat(baseStats.SpecialDefense, MonsterStat.SpecialDefense), Stat(baseStats.Speed, MonsterStat.Speed));
    }
}
