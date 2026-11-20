using System.ComponentModel.DataAnnotations.Schema;
using Intersect.Models;
using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Monsters;

public sealed class MonsterExperienceCurveDescriptor : DatabaseObject<MonsterExperienceCurveDescriptor>, IFolderable
{
    [JsonConstructor]
    public MonsterExperienceCurveDescriptor(Guid id) : base(id) => Name = "New Experience Curve";

    public MonsterExperienceCurveDescriptor() => Name = "New Experience Curve";

    public string Folder { get; set; } = string.Empty;

    [NotMapped]
    public long[] Thresholds { get; set; } = [];

    [Column("Thresholds"), JsonIgnore]
    public string ThresholdsJson
    {
        get => JsonConvert.SerializeObject(Thresholds);
        set => Thresholds = JsonConvert.DeserializeObject<long[]>(value ?? "[]") ?? [];
    }

    public bool IsValid(int cap) => cap is >= 1 and <= MonsterExperience.MaximumSupportedLevel &&
        !string.IsNullOrWhiteSpace(Name) && Folder != null &&
        Thresholds is { Length: >= 1 and <= MonsterExperience.MaximumSupportedLevel } &&
        Thresholds.Length >= cap && Thresholds[0] == 0 &&
        Thresholds.Zip(Thresholds.Skip(1), (lower, higher) => higher > lower).All(valid => valid);

    public override void Load(string json, bool keepCreationTime = false)
    {
        var id = Id;
        try { base.Load(json, keepCreationTime); }
        finally { Id = id; }
    }

    public static long ForSpecies(MonsterSpeciesDescriptor species, int level) =>
        species.ExperienceCurveId is { } id
            ? (Get(id) ?? throw new InvalidOperationException("The species experience curve is missing.")).Thresholds[level - 1]
            : MonsterExperience.Threshold(species.GrowthPreset, level);
}
