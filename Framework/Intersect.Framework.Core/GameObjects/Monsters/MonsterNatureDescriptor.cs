using System.ComponentModel.DataAnnotations.Schema;
using Intersect.Models;
using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Monsters;

/// <summary>Editor-created nature content, separate from individual assignment and stat calculation.</summary>
public sealed class MonsterNatureDescriptor : DatabaseObject<MonsterNatureDescriptor>, IFolderable
{
    public MonsterNatureDescriptor() => Name = "New Monster Nature";
    [JsonConstructor]
    public MonsterNatureDescriptor(Guid id) : base(id) => Name = "New Monster Nature";
    public string Description { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public int SelectionWeight { get; set; } = 100;
    [NotMapped]
    public MonsterNatureModifiers Modifiers { get; set; } = new();
    [Column("Modifiers"), JsonIgnore]
    public string ModifiersJson
    {
        get => JsonConvert.SerializeObject(Modifiers);
        set => Modifiers = JsonConvert.DeserializeObject<MonsterNatureModifiers>(value ?? "{}") ?? new();
    }
    [NotMapped, JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Description != null && Folder != null &&
        Modifiers is { IsValid: true } && SelectionWeight is >= 0 and <= 100000;
    public override void Load(string json, bool keepCreationTime = false)
    {
        var id = Id;
        try
        {
            // PopulateObject can coerce fractional JSON numbers to integers; reject that for weights.
            var content = Newtonsoft.Json.Linq.JObject.Parse(json);
            if (content.Properties().Any(property => property.Name.Equals(nameof(SelectionWeight), StringComparison.OrdinalIgnoreCase) &&
                    property.Value.Type != Newtonsoft.Json.Linq.JTokenType.Integer))
                throw new JsonSerializationException("Selection Weight must be an integer.");
            base.Load(json, keepCreationTime);
        }
        finally { Id = id; }
    }
}
