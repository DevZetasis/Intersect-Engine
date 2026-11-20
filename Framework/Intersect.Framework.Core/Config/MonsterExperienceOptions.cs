using Intersect.Framework.Core.GameObjects.Monsters;
using Newtonsoft.Json;

namespace Intersect.Config;

public sealed record MonsterExperienceOptions
{
    public decimal GlobalMultiplier { get; init; } = 1m;
    public bool ShareWithEntireParty { get; init; }
    public int ParticipatingPercent { get; init; } = 100;
    public int NonparticipatingPercent { get; init; } = 50;
    public bool AllowFainted { get; init; }
    public bool AwardExperienceForCaptures { get; init; } = true;
    public int CapturePercent { get; init; } = 50;

    [JsonIgnore]
    public bool IsValid => MonsterExperience.ValidMultiplier(GlobalMultiplier) &&
        ParticipatingPercent is >= 0 and <= 200 && NonparticipatingPercent is >= 0 and <= 200 &&
        CapturePercent is >= 0 and <= 200;
}
