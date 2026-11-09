using System.ComponentModel.DataAnnotations.Schema;
using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Server.Database.PlayerData.Players;

/// <summary>Persistent ownership, independent of map entities and the autosaved player graph.</summary>
public sealed class MonsterInstance
{
    private MonsterInstance() { }

    internal MonsterInstance(Guid ownerId, Guid speciesId, Guid grantRequestId, MonsterLocation location, int slot)
    {
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
