using System.ComponentModel.DataAnnotations.Schema;
using Intersect.Models;
using Newtonsoft.Json;

namespace Intersect.Framework.Core.GameObjects.Monsters;

public enum MonsterStatusGroup { Major }
public enum MonsterStatusDuration { UntilCured, Fixed, Random }
public enum MonsterStatusDamage { None, Fixed, Percent }

public sealed class MonsterStatusDescriptor : DatabaseObject<MonsterStatusDescriptor>, IFolderable
{
    public MonsterStatusDescriptor() => Name = "New Monster Status";
    [JsonConstructor]
    public MonsterStatusDescriptor(Guid id) : base(id) => Name = "New Monster Status";

    public string Description { get; set; } = "";
    public string Folder { get; set; } = "";
    public MonsterStatusGroup Group { get; set; }
    public bool PersistOutsideBattle { get; set; }
    public bool RemoveWhenSwitching { get; set; }
    public MonsterStatusDuration Duration { get; set; }
    public int MinimumTurns { get; set; } = 1;
    public int MaximumTurns { get; set; } = 1;
    public int RecoveryChance { get; set; }
    public int PreventActionChance { get; set; }
    public MonsterStatusDamage Damage { get; set; }
    public decimal DamageAmount { get; set; }
    public int AttackPercent { get; set; } = 100;
    public int DefensePercent { get; set; } = 100;
    public int SpecialAttackPercent { get; set; } = 100;
    public int SpecialDefensePercent { get; set; } = 100;
    public int SpeedPercent { get; set; } = 100;
    public Guid? AppliedAnimationId { get; set; }
    public Guid? ActivationAnimationId { get; set; }
    public Guid? RemovedAnimationId { get; set; }
    public string PreventActionMessage { get; set; } = "";

    [NotMapped, JsonIgnore]
    public bool IsValid => !string.IsNullOrWhiteSpace(Name) && Description != null && Folder != null &&
        PreventActionMessage != null && Enum.IsDefined(Group) && Enum.IsDefined(Duration) &&
        MinimumTurns is >= 1 and <= 999 && MaximumTurns is >= 1 and <= 999 &&
        (Duration != MonsterStatusDuration.Random || MaximumTurns >= MinimumTurns) &&
        RecoveryChance is >= 0 and <= 100 && PreventActionChance is >= 0 and <= 100 &&
        new[] { AttackPercent, DefensePercent, SpecialAttackPercent, SpecialDefensePercent, SpeedPercent }.All(value => value is >= 1 and <= 100) &&
        AppliedAnimationId != Guid.Empty && ActivationAnimationId != Guid.Empty && RemovedAnimationId != Guid.Empty &&
        Damage switch
        {
            MonsterStatusDamage.None => true,
            MonsterStatusDamage.Fixed => DamageAmount is >= 1 and <= int.MaxValue && decimal.Truncate(DamageAmount) == DamageAmount,
            MonsterStatusDamage.Percent => DamageAmount is > 0 and <= 100 && decimal.Round(DamageAmount, 4) == DamageAmount,
            _ => false,
        };

    public IEnumerable<Guid> AnimationReferences() =>
        new[] { AppliedAnimationId, ActivationAnimationId, RemovedAnimationId }.OfType<Guid>();
}
