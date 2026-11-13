using Intersect.Framework.Core.GameObjects.Monsters;
using MessagePack;

namespace Intersect.Network.Packets.Server;

public enum MonsterBattleEventKind { MoveUsed = 0, MoveMissed = 1, DamageApplied = 2, CriticalHit = 3, TypeEffectiveness = 4, MonsterFainted = 5, BattleFinished = 6, Withdraw = 7, SendOut = 8, StatStageChanged = 9, HPRecovered = 10, EffectFailed = 11 }

/// <summary>Ordered resolved facts for one packet revision, never client-supplied combat input.</summary>
[MessagePackObject]
public sealed record MonsterBattlePresentationEvent
{
    [Key(0)] public MonsterBattleEventKind Kind { get; set; }
    [Key(1)] public int Order { get; set; }
    [Key(2)] public Guid ActorId { get; set; }
    [Key(3)] public Guid TargetId { get; set; }
    [Key(4)] public Guid MoveId { get; set; }
    [Key(5)] public Guid? AnimationId { get; set; }
    [Key(6)] public long HPBefore { get; set; }
    [Key(7)] public long HPAfter { get; set; }
    [Key(8)] public string Text { get; set; } = string.Empty;
    [Key(9)] public MonsterBattleActorView? Incoming { get; set; }
    [Key(10)] public BattleMoveAnimationPosition AnimationPosition { get; set; }
    [Key(11)] public MonsterStat Stat { get; set; }
    [Key(12)] public int StageAfter { get; set; }
}
