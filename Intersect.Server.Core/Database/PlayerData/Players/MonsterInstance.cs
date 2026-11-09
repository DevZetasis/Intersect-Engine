using System.ComponentModel.DataAnnotations.Schema;
using Intersect.Config;
using Intersect.Server.Monsters.Calculations;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Server.Database.PlayerData.Players;

/// <summary>Persistent ownership, independent of map entities and the autosaved player graph.</summary>
public sealed class MonsterInstance
{
    private MonsterInstance() { }

    internal MonsterInstance(Guid ownerId, Guid speciesId, Guid grantRequestId, MonsterLocation location, int slot, MonsterStatValues ivs, Guid? natureId)
    {
        MonsterStatCalculator.ValidateIVs(ivs);
        NatureId = natureId;
        IvHP = checked((int)ivs.HP);
        IvAttack = checked((int)ivs.Attack);
        IvDefense = checked((int)ivs.Defense);
        IvSpecialAttack = checked((int)ivs.SpecialAttack);
        IvSpecialDefense = checked((int)ivs.SpecialDefense);
        IvSpeed = checked((int)ivs.Speed);
        Id = Guid.NewGuid();
        OwnerPlayerId = ownerId;
        SpeciesId = speciesId;
        GrantRequestId = grantRequestId;
        Location = location;
        Slot = slot;
        CreatedAtUtcMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; private set; }
    public Guid SpeciesId { get; private set; }
    public Guid OwnerPlayerId { get; private set; }
    public int Level { get; private set; } = 1;
    public long Experience { get; private set; }
    public string Nickname { get; private set; }
    public MonsterLocation Location { get; private set; }
    public int Slot { get; private set; }
    public long CreatedAtUtcMilliseconds { get; private set; }
    public Guid GrantRequestId { get; private set; }
    public Guid Revision { get; private set; } = Guid.NewGuid();

    public Guid? NatureId { get; private set; }
    public int IvHP { get; private set; }
    public int IvAttack { get; private set; }
    public int IvDefense { get; private set; }
    public int IvSpecialAttack { get; private set; }
    public int IvSpecialDefense { get; private set; }
    public int IvSpeed { get; private set; }
    public int EvHP { get; private set; }
    public int EvAttack { get; private set; }
    public int EvDefense { get; private set; }
    public int EvSpecialAttack { get; private set; }
    public int EvSpecialDefense { get; private set; }
    public int EvSpeed { get; private set; }
    [NotMapped]
    public MonsterStatValues IVs => new(IvHP, IvAttack, IvDefense, IvSpecialAttack, IvSpecialDefense, IvSpeed);
    [NotMapped]
    public MonsterStatValues EVs => new(EvHP, EvAttack, EvDefense, EvSpecialAttack, EvSpecialDefense, EvSpeed);

    internal void SetEVs(MonsterStatValues evs, MonsterBattleRules rules)
    {
        MonsterStatCalculator.ValidateEVs(evs, rules);
        EvHP = checked((int)evs.HP);
        EvAttack = checked((int)evs.Attack);
        EvDefense = checked((int)evs.Defense);
        EvSpecialAttack = checked((int)evs.SpecialAttack);
        EvSpecialDefense = checked((int)evs.SpecialDefense);
        EvSpeed = checked((int)evs.Speed);
        Revision = Guid.NewGuid();
    }

    internal void Place(MonsterLocation location, int slot)
    {
        Location = location;
        Slot = slot;
        Revision = Guid.NewGuid();
    }

    public OwnedMonsterSummary ToSummary() => new()
    {
        Id = Id, SpeciesId = SpeciesId, Nickname = Nickname, Level = Level,
        Experience = Experience, Location = Location, Slot = Slot, Revision = Revision,
    };
}
