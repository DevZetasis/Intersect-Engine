using Intersect.Localization;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Core.CommandParsing;
using Intersect.Server.Core.CommandParsing.Arguments;
using Intersect.Server.Database;
using Intersect.Server.Monsters;
using Intersect.Server.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Intersect.Server.Core.Commands;

/// <summary>Read-only privileged server-console inspection, including offline-owned monsters.</summary>
internal sealed class InspectMonsterCommand : ServerCommand
{
    public InspectMonsterCommand() : base(Strings.Commands.InspectMonster,
        new VariableArgument<string>(new LocaleArgument { Name = "monster", Description = "Individual monster UUID" }, RequiredIfNotHelp, true)) { }
    protected override void HandleValue(ServerContext context, ParserResult result)
    {
        if (!Guid.TryParse(result.Find(FindArgument<VariableArgument<string>>(0)), out var id))
        { Console.WriteLine("Usage: inspectmonster <individual-monster-UUID>"); return; }
        try
        {
            using var db = DbInterface.CreatePlayerContext();
            var monster = db.OwnedMonsters.AsNoTracking().SingleOrDefault(item => item.Id == id);
            if (monster == null) { Console.WriteLine("Monster was not found."); return; }
            Console.WriteLine(MonsterDiagnostics.Describe(monster, MonsterSpeciesDescriptor.Get(monster.SpeciesId),
                monster.NatureId is { } natureId ? MonsterNatureDescriptor.Get(natureId) : null, Options.Instance.Monsters.Battle));
        }
        catch (Exception exception)
        {
            context.Logger.LogError(exception, "Monster inspection failed for {MonsterId}", id);
            Console.WriteLine("Monster inspection failed. Check the server log.");
        }
    }
}
