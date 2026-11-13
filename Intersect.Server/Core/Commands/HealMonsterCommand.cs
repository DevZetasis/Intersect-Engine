using Intersect.Localization;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Core.CommandParsing;
using Intersect.Server.Core.CommandParsing.Arguments;
using Intersect.Server.Database;
using Intersect.Server.Entities;
using Intersect.Server.Localization;
using Intersect.Server.Monsters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
namespace Intersect.Server.Core.Commands;
internal sealed class HealMonsterCommand : ServerCommand
{
    public HealMonsterCommand() : base(Strings.Commands.HealMonster,
        new VariableArgument<string>(new LocaleArgument { Name = "monster", Description = "Individual monster UUID" }, RequiredIfNotHelp, true),
        new VariableArgument<string>(new LocaleArgument { Name = "resource", Description = "hp, pp, status or all" }, RequiredIfNotHelp, true)) { }
    protected override void HandleValue(ServerContext context, ParserResult result)
    {
        var mode = result.Find(FindArgument<VariableArgument<string>>(1))?.ToLowerInvariant();
        if (!Guid.TryParse(result.Find(FindArgument<VariableArgument<string>>(0)), out var id) || mode is not ("hp" or "pp" or "status" or "all"))
        { Console.WriteLine("Usage: healmonster <individual-monster-UUID> <hp|pp|status|all>"); return; }
        try
        {
            using var db = DbInterface.CreatePlayerContext();
            var owner = db.OwnedMonsters.AsNoTracking().Where(m => m.Id == id).Select(m => m.OwnerPlayerId).SingleOrDefault();
            if (owner == Guid.Empty) throw new InvalidOperationException("Monster was not found.");
            new MonsterHealingService(MonsterRoster.CreateService(context.Logger)).HealOne(owner, id, mode switch
            {
                "hp" => MonsterRecovery.HP,
                "pp" => MonsterRecovery.PP,
                "status" => MonsterRecovery.Status,
                _ => MonsterRecovery.All,
            });
            if (Player.FindOnline(owner) is { } player) MonsterRoster.Send(player);
            Console.WriteLine($"Healed {mode} for monster {id}.");
        }
        catch (InvalidOperationException exception) { Console.WriteLine($"Healing refused: {exception.Message}"); }
        catch (Exception exception) { context.Logger.LogError(exception, "Monster healing failed"); Console.WriteLine("Healing failed; no successful healing was confirmed. Check the log."); }
    }
}
