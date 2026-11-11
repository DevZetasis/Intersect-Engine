using MessagePack;

namespace Intersect.Network.Packets.Server;

public enum MonsterBattleOutcome { Active = 0, Victory = 1, Defeat = 2, Forfeit = 3, Cancelled = 4, Draw = 5 }

public enum MonsterBattlePhase { Commands, Resolving, Replacement, Completed, Ended }

[MessagePackObject]
public sealed class MonsterBattlePartyMember
{
    [Key(0)] public int Slot { get; set; }
    [Key(1)] public MonsterBattleActorView Actor { get; set; } = new();
}

[MessagePackObject]
public sealed class MonsterBattleActorView
{
    [Key(0)] public Guid Id { get; set; }
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public string Sprite { get; set; } = string.Empty;
    [Key(3)] public int Level { get; set; }
    [Key(4)] public long HP { get; set; }
    [Key(5)] public long MaximumHP { get; set; }
    [Key(6)] public string FrontBattleSprite { get; set; } = string.Empty;
    [Key(7)] public string BackBattleSprite { get; set; } = string.Empty;
}

[MessagePackObject]
public sealed class MonsterBattleMoveView
{
    [Key(0)] public int Slot { get; set; }
    [Key(1)] public string Name { get; set; } = string.Empty;
    [Key(2)] public string Type { get; set; } = string.Empty;
    [Key(3)] public string Category { get; set; } = string.Empty;
    [Key(4)] public int PP { get; set; }
    [Key(5)] public int MaximumPP { get; set; }
    [Key(6)] public bool Usable { get; set; }
    [Key(7)] public string UnavailableReason { get; set; } = string.Empty;
}

/// <summary>One private authoritative snapshot serves start, turn result and end.</summary>
[MessagePackObject]
public sealed class MonsterBattleStatePacket : IntersectPacket
{
    [Key(0)] public Guid PlayerId { get; set; }
    [Key(1)] public Guid BattleId { get; set; }
    [Key(2)] public int Turn { get; set; }
    [Key(3)] public Guid Revision { get; set; }
    [Key(4)] public MonsterBattleOutcome Outcome { get; set; }
    [Key(5)] public MonsterBattleActorView Player { get; set; } = new();
    [Key(6)] public MonsterBattleActorView Opponent { get; set; } = new();
    [Key(7)] public MonsterBattleMoveView[] Moves { get; set; } = [];
    [Key(8)] public string[] Messages { get; set; } = [];
    [Key(9)] public long Sequence { get; set; }
    [Key(10)] public MonsterBattlePresentationEvent[] Events { get; set; } = [];
    [Key(11)] public Guid PreviousRevision { get; set; }
    [Key(12)] public bool IsCorrection { get; set; }
    [Key(13)] public string Background { get; set; } = string.Empty;
    [Key(14)] public MonsterBattlePhase Phase { get; set; }
    [Key(15)] public MonsterBattlePartyMember[] Party { get; set; } = [];

}
