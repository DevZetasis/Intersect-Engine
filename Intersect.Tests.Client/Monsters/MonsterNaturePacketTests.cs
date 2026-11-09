using System.Reflection;
using System.Runtime.CompilerServices;
using Intersect.Client.Entities;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network;
using Intersect.Network.Packets.Server;
using NUnit.Framework;

namespace Intersect.Tests.Client.Monsters;

[TestFixture, NonParallelizable]
public class MonsterNaturePacketTests
{
    [Test]
    public void GenericClientContentHandlerLoadsUpdatesAndDeletesNatures()
    {
        var type = new MonsterNatureDescriptor(Guid.NewGuid()) { Name = "Client Nature", SelectionWeight = 5 };
        try
        {
            var handlerType = typeof(Player).Assembly.GetType("Intersect.Client.Networking.PacketHandler")!;
            var handler = RuntimeHelpers.GetUninitializedObject(handlerType);
            var method = handlerType.GetMethod("HandlePacket", BindingFlags.Instance | BindingFlags.Public,
                [typeof(IPacketSender), typeof(GameObjectPacket)])!;
            method.Invoke(handler, [null, new GameObjectPacket(type.Id, type.Type, type.JsonData, false, false)]);
            Assert.That(MonsterNatureDescriptor.Get(type.Id).JsonData, Is.EqualTo(type.JsonData));
            type.SelectionWeight = 0;
            method.Invoke(handler, [null, new GameObjectPacket(type.Id, type.Type, type.JsonData, false, false)]);
            Assert.That(MonsterNatureDescriptor.Get(type.Id).SelectionWeight, Is.EqualTo(0));
            method.Invoke(handler, [null, new GameObjectPacket(type.Id, type.Type, string.Empty, true, false)]);
            Assert.That(MonsterNatureDescriptor.Get(type.Id), Is.Null);
        }
        finally { MonsterNatureDescriptor.Lookup.DeleteAt(type.Id); }
    }
}
