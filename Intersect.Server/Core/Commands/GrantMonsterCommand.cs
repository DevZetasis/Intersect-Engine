using Intersect.Localization;
using Intersect.Server.Core.CommandParsing;
using Intersect.Server.Core.CommandParsing.Arguments;
using Intersect.Server.Database;
using Intersect.Server.Entities;
using Intersect.Server.Monsters;
using Intersect.Server.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Intersect.Server.Core.Commands;

/// <summary>Privileged server-console command, never registered as a player chat command or packet.</summary>
internal sealed class GrantMonsterCommand : ServerCommand
{
    public GrantMonsterCommand() : base(Strings.Commands.GrantMonster,
        new VariableArgument<string>(new LocaleArgument { Name = "character", Description = "Character name or UUID" }, RequiredIfNotHelp, true),
        new VariableArgument<string>(new LocaleArgument { Name = "species", Description = "Species UUID" }, RequiredIfNotHelp, true),
        new VariableArgument<string>(new LocaleArgument { Name = "request", Description = "Unique grant UUID; reuse it when retrying" }, RequiredIfNotHelp, true)) { }

    protected override void HandleValue(ServerContext context, ParserResult result)
    {
        var target = result.Find(FindArgument<VariableArgument<string>>(0));
        if (!Guid.TryParse(result.Find(FindArgument<VariableArgument<string>>(1)), out var speciesId) ||
            !Guid.TryParse(result.Find(FindArgument<VariableArgument<string>>(2)), out var requestId))
        {
            Console.WriteLine("Usage: grantmonster <character-name-or-UUID> <species-UUID> <request-UUID>");
            return;
        }
        try
        {
            using var db = DbInterface.CreatePlayerContext();
            var ownerId = Guid.TryParse(target, out var parsedId)
                ? db.Players.Where(player => player.Id == parsedId).Select(player => player.Id).SingleOrDefault()
                : db.Players.Where(player => player.Name == target).Select(player => player.Id).SingleOrDefault();
            if (ownerId == Guid.Empty) throw new InvalidOperationException("Character does not exist.");
            var monster = MonsterRoster.CreateService().Grant(ownerId, speciesId, requestId, out var created);
            var outcome = created ? "Created monster" : "Existing monster (retry; placement unchanged)";
            Console.WriteLine($"{outcome} {monster.Id} for character {ownerId}: current location {monster.Location} slot {monster.Slot + 1}, revision {monster.Revision}. Request {requestId} is safe to retry.");
            if (Player.FindOnline(ownerId) is { } online) MonsterRoster.Send(online);
        }
        catch (InvalidOperationException exception) { Console.WriteLine($"Monster grant failed: {exception.Message}"); }
        catch (Exception exception)
        {
            context.Logger.LogError(exception, "Administrative monster grant failed");
            Console.WriteLine("Monster grant failed. Check the server log, then retry with the same request UUID.");
        }
    }
}
