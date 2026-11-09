using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;
using Intersect.Models;
using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Monsters;

/// <summary>Editable attacking type. Relationships are keyed by defending type UUID.</summary>
public sealed class MonsterTypeDescriptor : DatabaseObject<MonsterTypeDescriptor>, IFolderable
{
    public MonsterTypeDescriptor() => Name = "New Monster Type";
    [JsonConstructor]
    public MonsterTypeDescriptor(Guid id) : base(id) => Name = "New Monster Type";
    public string Description { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public string DisplayColor { get; set; } = "#FFFFFF";
    [NotMapped]
    public Dictionary<Guid, double> Effectiveness { get; set; } = new();
    [Column("Effectiveness"), JsonIgnore]
    public string EffectivenessJson
    {
        get => JsonConvert.SerializeObject(Effectiveness);
        set => Effectiveness = JsonConvert.DeserializeObject<Dictionary<Guid, double>>(value ?? "{}") ?? new();
    }
    [NotMapped, JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Description != null && Folder != null &&
        DisplayColor != null && DisplayColor.Length == 7 && Regex.IsMatch(DisplayColor, "^#[0-9a-fA-F]{6}$") && Effectiveness != null &&
        Effectiveness.All(pair => pair.Key != Guid.Empty && MonsterTypeEffectiveness.IsSupported(pair.Value));
    public override void Load(string json, bool keepCreationTime = false)
    {
        var id = Id;
        try { base.Load(json, keepCreationTime); }
        finally { Id = id; }
    }
}
