namespace Intersect.Server.Monsters.Calculations;

/// <summary>Uniform integer in [0, exclusiveUpperBound); injected into generation and calculations.</summary>
public interface IMonsterRandom { long NextInt64(long exclusiveUpperBound); }
public sealed class SystemMonsterRandom : IMonsterRandom
{
    public long NextInt64(long exclusiveUpperBound) => Random.Shared.NextInt64(exclusiveUpperBound);
}
public static class MonsterRandom
{
    public static long Draw(IMonsterRandom random, long exclusiveUpperBound)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (exclusiveUpperBound <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveUpperBound));
        var value = random.NextInt64(exclusiveUpperBound);
        if (value < 0 || value >= exclusiveUpperBound) throw new InvalidOperationException("Random source returned a value outside its contract.");
        return value;
    }
}
