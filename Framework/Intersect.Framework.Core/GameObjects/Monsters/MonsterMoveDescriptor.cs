using System.ComponentModel.DataAnnotations.Schema;
using Intersect.Models;
using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Monsters;

public enum MonsterMoveCategory { Physical = 0, Special = 1, Status = 2 }
public enum MonsterMoveTarget { OneOpponent = 0, Self = 1, OneAlly = 2, AllOpponents = 3, AllBattlefield = 4 }

public enum BattleMoveAnimationPosition { Target = 0, Attacker = 1, Both = 2 }

/// <summary>Reusable content only; execution and individual learned moves are separate future systems.</summary>
public sealed class MonsterMoveDescriptor : DatabaseObject<MonsterMoveDescriptor>, IFolderable
{
    public MonsterMoveDescriptor() => Name = "New Monster Move";
    [JsonConstructor]
    public MonsterMoveDescriptor(Guid id) : base(id) => Name = "New Monster Move";
    public string Description { get; set; } = string.Empty;
    public string Folder { get; set; } = string.Empty;
    public Guid TypeId { get; set; }
    public MonsterMoveCategory Category { get; set; } = MonsterMoveCategory.Physical;
    public int BasePower { get; set; } = 1;
    public int MaximumPP { get; set; } = 20;
    public int Accuracy { get; set; } = 100;
    public bool AlwaysHits { get; set; }
    public int Priority { get; set; }
    public MonsterMoveTarget Target { get; set; } = MonsterMoveTarget.OneOpponent;
    public Guid? AnimationId { get; set; }
    public BattleMoveAnimationPosition AnimationPosition { get; set; }
    [NotMapped, JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Description != null && Folder != null &&
        TypeId != Guid.Empty && Enum.IsDefined(Category) && Enum.IsDefined(Target) &&
        BasePower >= (Category == MonsterMoveCategory.Status ? 0 : 1) && Accuracy is >= 1 and <= 100 &&
        MaximumPP is >= 1 and <= 999 && AnimationId != Guid.Empty && Enum.IsDefined(AnimationPosition);
    public override void Load(string json, bool keepCreationTime = false)
    {
        var id = Id;
        try
        {
            var properties = Newtonsoft.Json.Linq.JObject.Parse(json);
            if (properties.Property(nameof(MaximumPP), StringComparison.OrdinalIgnoreCase) == null) MaximumPP = 20;
            if (properties.Property(nameof(AnimationPosition), StringComparison.OrdinalIgnoreCase) == null)
                AnimationPosition = BattleMoveAnimationPosition.Target;
            base.Load(json, keepCreationTime);
        }
        finally { Id = id; }
    }
}
