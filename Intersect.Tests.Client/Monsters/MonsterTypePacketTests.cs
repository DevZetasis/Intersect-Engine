using System.Reflection;
using System.Runtime.CompilerServices;
using Intersect.Client.Entities;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network;
using Intersect.Network.Packets.Server;
using NUnit.Framework;

namespace Intersect.Tests.Client.Monsters;

[TestFixture, NonParallelizable]
public class MonsterTypePacketTests
{
    [Test]
    public void GenericClientContentHandlerLoadsUpdatesAndDeletesCustomTypes()
    {
        var type = new MonsterTypeDescriptor(Guid.NewGuid()) { Name = "Client Type", Effectiveness = new() { [Guid.NewGuid()] = 2 } };
        try
        {
            var handlerType = typeof(Player).Assembly.GetType("Intersect.Client.Networking.PacketHandler")!;
            var handler = RuntimeHelpers.GetUninitializedObject(handlerType);
            var method = handlerType.GetMethod("HandlePacket", BindingFlags.Instance | BindingFlags.Public,
                [typeof(IPacketSender), typeof(GameObjectPacket)])!;
            method.Invoke(handler, [null, new GameObjectPacket(type.Id, type.Type, type.JsonData, false, false)]);
            Assert.That(MonsterTypeDescriptor.Get(type.Id).JsonData, Is.EqualTo(type.JsonData));
            type.DisplayColor = "#123ABC";
            method.Invoke(handler, [null, new GameObjectPacket(type.Id, type.Type, type.JsonData, false, false)]);
            Assert.That(MonsterTypeDescriptor.Get(type.Id).DisplayColor, Is.EqualTo("#123ABC"));
            method.Invoke(handler, [null, new GameObjectPacket(type.Id, type.Type, string.Empty, true, false)]);
            Assert.That(MonsterTypeDescriptor.Get(type.Id), Is.Null);
        }
        finally { MonsterTypeDescriptor.Lookup.DeleteAt(type.Id); }
    }
}
