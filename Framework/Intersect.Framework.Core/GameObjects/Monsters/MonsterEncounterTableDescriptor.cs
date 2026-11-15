using System.ComponentModel.DataAnnotations.Schema;
using Intersect.Models;
using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Monsters;

public sealed record MonsterEncounterEntry(Guid SpeciesId, int Weight, int MinimumLevel, int MaximumLevel)
{
    public bool IsValid(int maxLevel, Func<Guid, bool> exists) =>
        SpeciesId != Guid.Empty && exists(SpeciesId) && Weight is >= 0 and <= 100000 &&
        MinimumLevel >= 1 && MinimumLevel <= MaximumLevel && MaximumLevel <= maxLevel;
}

public sealed class MonsterEncounterTableDescriptor : DatabaseObject<MonsterEncounterTableDescriptor>, IFolderable
{
    public MonsterEncounterTableDescriptor() => Name = "New Encounter Table";

    [JsonConstructor]
    public MonsterEncounterTableDescriptor(Guid id) : base(id) => Name = "New Encounter Table";

    public string Description { get; set; } = "";
    public string Folder { get; set; } = "";
    public bool Enabled { get; set; }
    public int Rate { get; set; } = 10;
    public int Cooldown { get; set; } = 3;
    public const int MaximumCooldown = 100000;

    [NotMapped]
    public List<MonsterEncounterEntry> Entries { get; set; } = [];

    [Column("Entries"), JsonIgnore]
    public string EntriesJson
    {
        get => JsonConvert.SerializeObject(Entries);
        set
        {
            try { Entries = JsonConvert.DeserializeObject<List<MonsterEncounterEntry>>(value ?? "[]") ?? []; }
            catch (JsonException) { Entries = []; }
        }
    }

    public string? ValidationError(int maxLevel, Func<Guid, bool> exists)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 120 || Name.Any(char.IsControl))
            return "Enter a table name of 1–120 characters.";
        if (Rate is < 0 or > 100 || Cooldown is < 0 or > MaximumCooldown)
            return "Set a rate of 0–100% and a cooldown of 0–100000 steps.";
        if (Entries == null || Entries.Any(entry => entry == null || !entry.IsValid(maxLevel, exists)))
            return $"Each entry needs an existing species, a weight of 0–100000 and an ordered level range within 1–{maxLevel}.";
        if (Enabled && !Entries.Any(entry => entry.Weight > 0))
            return "An enabled table needs at least one entry with positive weight.";
        return null;
    }

    public static MonsterEncounterTableDescriptor Copy(Guid destinationId, MonsterEncounterTableDescriptor source)
    {
        var copy = new MonsterEncounterTableDescriptor(destinationId);
        copy.Load(source.JsonData, keepCreationTime: true);
        return copy;
    }
}
