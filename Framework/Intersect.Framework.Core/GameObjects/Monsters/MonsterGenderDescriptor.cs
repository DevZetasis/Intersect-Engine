using System.ComponentModel.DataAnnotations.Schema;
using Intersect.Models;
using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Monsters;

public sealed class MonsterGenderDescriptor : DatabaseObject<MonsterGenderDescriptor>, IFolderable
{
    public static readonly Guid DefaultMale = new("6a190000-0000-4000-8000-000000000001");
    public static readonly Guid DefaultFemale = new("6a190000-0000-4000-8000-000000000002");
    public static readonly Guid DefaultGenderless = new("6a190000-0000-4000-8000-000000000003");

    public MonsterGenderDescriptor() => Name = "New Monster Gender";
    [JsonConstructor]
    public MonsterGenderDescriptor(Guid id) : base(id) => Name = "New Monster Gender";

    public string Description { get; set; } = "";
    public string Folder { get; set; } = "";
    public string Symbol { get; set; } = "";

    [NotMapped, JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Name.Length <= 80 && !Name.Any(char.IsControl) &&
        Description != null && Folder != null && Symbol is { Length: <= 16 } && !Symbol.Any(char.IsControl);

    public static Dictionary<Guid, int> DefaultDistribution() => new()
    {
        [DefaultMale] = 50,
        [DefaultFemale] = 50,
    };

    public static bool ValidDistribution(Dictionary<Guid, int>? weights, Func<Guid, bool> exists) =>
        weights != null && weights.Values.Any(weight => weight > 0) &&
        weights.All(pair => pair.Key != Guid.Empty && pair.Value is >= 0 and <= 100000 && exists(pair.Key));
}
