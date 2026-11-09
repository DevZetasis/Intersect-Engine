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

    [NotMapped]
    public Guid[] TypeIds { get; set; } = Array.Empty<Guid>();

    [Column("Types"), JsonIgnore]
    public string TypeIdsJson
    {
        get => JsonConvert.SerializeObject(TypeIds);
        set => TypeIds = JsonConvert.DeserializeObject<Guid[]>(value ?? "[]") ?? Array.Empty<Guid>();
    }

    // Named JSON properties allow additional monster stats without altering RPG stat arrays.
    [Column("BaseStats"), JsonIgnore]
    public string BaseStatsJson
    {
        get => JsonConvert.SerializeObject(BaseStats);
        set => BaseStats = JsonConvert.DeserializeObject<MonsterBaseStats>(value ?? "{}") ?? new();
    }

    [NotMapped, JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Description != null && Sprite != null &&
                           Folder != null && BaseStats is { IsValid: true } && TypeIds != null && TypeIds.Length <= 2 &&
                           TypeIds.All(id => id != Guid.Empty) && TypeIds.Distinct().Count() == TypeIds.Length;

    public override void Load(string json, bool keepCreationTime = false)
    {
        var id = Id;
        try
        {
            // Missing assignments in older content mean unconfigured, even when reusing a draft.
            if (Newtonsoft.Json.Linq.JObject.Parse(json).Property(nameof(TypeIds), StringComparison.OrdinalIgnoreCase) == null)
                TypeIds = Array.Empty<Guid>();
            base.Load(json, keepCreationTime);
        }
        finally
        {
            // Editing, copying, and synchronization must never replace an existing identity.
            Id = id;
        }
    }
}
