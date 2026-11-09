namespace Intersect.Config;

public sealed class MonsterOptions
{
    /// <summary>Maximum active roster size (1–64). Existing stored monsters are unaffected.</summary>
    public int PartyCapacity { get; set; } = 6;
}
