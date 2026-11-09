using System.Reflection;
using System.Runtime.CompilerServices;
using Intersect.Client.Entities;
using Intersect.Client.General;
using Intersect.Network;
using Intersect.Network.Packets.Server;
using NUnit.Framework;

namespace Intersect.Tests.Client.Monsters;

[TestFixture, NonParallelizable]
public class MonsterRosterPacketTests
{
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
