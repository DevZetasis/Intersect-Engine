using System.Reflection;
using System.Runtime.CompilerServices;
using Intersect.Client.Entities;
using Intersect.Client.General;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Client;
using Intersect.Network;
using Intersect.Network.Packets.Server;
using NUnit.Framework;

namespace Intersect.Tests.Client.Monsters;

[TestFixture, NonParallelizable]
public class MonsterRosterPacketTests
{
    [Test]
    public void ProgressAndLearningRoundTripWithoutChangingTheDecisionIdentity()
    {
        var decision = Guid.NewGuid();
        var original = new OwnedMonsterSummary
        {
            Id = Guid.NewGuid(), Level = 3, Experience = 30,
            Details = new()
            {
                LevelStartExperience = 27, NextLevelExperience = 64,
                PendingMove = new() { DecisionId = decision, Name = "Leaf Strike", Power = 40, Available = true },
            },
        };
        var summary = MessagePack.MessagePackSerializer.Deserialize<OwnedMonsterSummary>(
            MessagePack.MessagePackSerializer.Serialize(original));
        Assert.That(summary.Experience, Is.EqualTo(30));
        Assert.That(summary.Details!.NextLevelExperience, Is.EqualTo(64));
        Assert.That(summary.Details.PendingMove!.Name, Is.EqualTo("Leaf Strike"));
        var response = new ResolveMonsterMoveLearningPacket { MonsterId = summary.Id, DecisionId = summary.Details.PendingMove.DecisionId, Slot = 3 };
        var decoded = MessagePack.MessagePackSerializer.Deserialize<ResolveMonsterMoveLearningPacket>(
            MessagePack.MessagePackSerializer.Serialize(response));
        Assert.That(decoded.DecisionId, Is.EqualTo(decision));
        Assert.That(decoded.MonsterId, Is.EqualTo(original.Id));
        Assert.That(decoded.Slot, Is.EqualTo(3));
    }

    [Test]
    public void RosterSnapshotOnlyAppliesToCurrentCharacter()
    {
        var original = Globals.Me;
        try
        {
            // This handler only changes roster state; no renderer or live connection is needed.
            var player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            GC.SuppressFinalize(player); // This state-only test bypasses the entity/renderer constructor.
            player.Id = Guid.NewGuid();
            Globals.Me = player;
            var type = typeof(Player).Assembly.GetType("Intersect.Client.Networking.PacketHandler")!;
            var handler = RuntimeHelpers.GetUninitializedObject(type);
            var apply = type.GetMethod("HandlePacket", BindingFlags.Instance | BindingFlags.Public,
                [typeof(IPacketSender), typeof(OwnedMonstersPacket)])!;
            var own = new OwnedMonstersPacket { PlayerId = player.Id, PartyCapacity = 6 };
            apply.Invoke(handler, [null, own]);
            Assert.That(player.MonsterRoster, Is.SameAs(own));
            var foreign = new OwnedMonstersPacket { PlayerId = Guid.NewGuid(), PartyCapacity = 6 };
            apply.Invoke(handler, [null, foreign]);
            Assert.That(player.MonsterRoster, Is.SameAs(own));
            Globals.Me = null;
            Assert.DoesNotThrow(() => apply.Invoke(handler, [null, own]));
        }
        finally { Globals.Me = original; }
    }
}
