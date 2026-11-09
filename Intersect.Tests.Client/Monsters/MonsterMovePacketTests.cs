using System.Reflection;
using System.Runtime.CompilerServices;
using Intersect.Client.Entities;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network;
using Intersect.Network.Packets.Server;
using NUnit.Framework;

namespace Intersect.Tests.Client.Monsters;

[TestFixture, NonParallelizable]
public class MonsterMovePacketTests
{
    [Test]
    public void GenericClientContentHandlerLoadsUpdatesAndDeletesMoves()
    {
        var type = new MonsterMoveDescriptor(Guid.NewGuid()) { Name = "Client Move", TypeId = Guid.NewGuid() };
        try
        {
            var handlerType = typeof(Player).Assembly.GetType("Intersect.Client.Networking.PacketHandler")!;
            var handler = RuntimeHelpers.GetUninitializedObject(handlerType);
            var method = handlerType.GetMethod("HandlePacket", BindingFlags.Instance | BindingFlags.Public,
                [typeof(IPacketSender), typeof(GameObjectPacket)])!;
            method.Invoke(handler, [null, new GameObjectPacket(type.Id, type.Type, type.JsonData, false, false)]);
            Assert.That(MonsterMoveDescriptor.Get(type.Id).JsonData, Is.EqualTo(type.JsonData));
            type.Priority = -3;
            method.Invoke(handler, [null, new GameObjectPacket(type.Id, type.Type, type.JsonData, false, false)]);
            Assert.That(MonsterMoveDescriptor.Get(type.Id).Priority, Is.EqualTo(-3));
            method.Invoke(handler, [null, new GameObjectPacket(type.Id, type.Type, string.Empty, true, false)]);
            Assert.That(MonsterMoveDescriptor.Get(type.Id), Is.Null);
        }
        finally { MonsterMoveDescriptor.Lookup.DeleteAt(type.Id); }
    }
}
