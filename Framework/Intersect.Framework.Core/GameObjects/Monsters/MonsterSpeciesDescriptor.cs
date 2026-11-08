using System.ComponentModel.DataAnnotations.Schema;
using Intersect.Models;
using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Monsters;

public sealed class MonsterSpeciesDescriptor : DatabaseObject<MonsterSpeciesDescriptor>, IFolderable
{
    public MonsterSpeciesDescriptor() => Name = "New Monster Species";

    [JsonConstructor]
    public MonsterSpeciesDescriptor(Guid id) : base(id) => Name = "New Monster Species";

    public string Description { get; set; } = string.Empty;
    public string Sprite { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;

    [NotMapped]
    public MonsterBaseStats BaseStats { get; set; } = new();

    // Named JSON properties allow additional monster stats without altering RPG stat arrays.
    [Column("BaseStats"), JsonIgnore]
    public string BaseStatsJson
    {
        get => JsonConvert.SerializeObject(BaseStats);
        set => BaseStats = JsonConvert.DeserializeObject<MonsterBaseStats>(value ?? "{}") ?? new();
    }

    [NotMapped, JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Description != null && Sprite != null &&
                           Folder != null && BaseStats is { IsValid: true };

    public override void Load(string json, bool keepCreationTime = false)
    {
        var id = Id;
        try
        {
            base.Load(json, keepCreationTime);
        }
        finally
        {
            // Editing, copying, and synchronization must never replace an existing identity.
            Id = id;
        }
    }
}
