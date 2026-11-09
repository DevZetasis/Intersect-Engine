using Intersect.Server.Core.CommandParsing;
using Intersect.Server.Core.Commands;
using NUnit.Framework;

namespace Intersect.Tests.Server.GameObjects;

[TestFixture]
public class MonsterGrantCommandTests
{
    [Test]
    public void ConsoleGrantAcceptsQuotedCharacterAndRequiresIdempotencyKey()
    {
        var parser = new CommandParser();
        Assert.That(parser.Register<GrantMonsterCommand>(), Is.True);
        var complete = parser.Parse($"grantmonster \"Trainer One\" {Guid.NewGuid()} {Guid.NewGuid()}");
        Assert.That(complete.Errors, Is.Empty);
        Assert.That(complete.Missing, Is.Empty);
        var missingKey = parser.Parse($"grantmonster \"Trainer One\" {Guid.NewGuid()}");
        Assert.That(missingKey.Missing, Has.Count.EqualTo(1));
    }
}
