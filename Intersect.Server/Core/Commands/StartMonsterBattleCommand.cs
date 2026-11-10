using Intersect.Localization;
using Intersect.Server.Core.CommandParsing;
using Intersect.Server.Core.CommandParsing.Arguments;
using Intersect.Server.Entities;
using Intersect.Server.Localization;
using Intersect.Server.Monsters.Battles;
using Microsoft.Extensions.Logging;

namespace Intersect.Server.Core.Commands;

internal sealed class StartMonsterBattleCommand : ServerCommand
{
    public StartMonsterBattleCommand() : base(Strings.Commands.StartMonsterBattle,
        new VariableArgument<string>(new LocaleArgument { Name = "character", Description = "Online character name or UUID" }, RequiredIfNotHelp, true),
        new VariableArgument<string>(new LocaleArgument { Name = "monster", Description = "Owned party monster UUID" }, RequiredIfNotHelp, true),
        new VariableArgument<string>(new LocaleArgument { Name = "opponent", Description = "Opponent species UUID" }, RequiredIfNotHelp, true),
        new VariableArgument<string>(new LocaleArgument { Name = "level", Description = "Opponent level" }, RequiredIfNotHelp, true)) { }
    protected override void HandleValue(ServerContext context, ParserResult result)
    {
        var character = result.Find(FindArgument<VariableArgument<string>>(0));
        if (!Guid.TryParse(result.Find(FindArgument<VariableArgument<string>>(1)), out var monster) ||
            !Guid.TryParse(result.Find(FindArgument<VariableArgument<string>>(2)), out var opponent) ||
            !int.TryParse(result.Find(FindArgument<VariableArgument<string>>(3)), out var level))
        { Console.WriteLine("Usage: startmonsterbattle <character-name-or-UUID> <owned-monster-UUID> <opponent-species-UUID> <opponent-level>"); return; }
        try
        {
            var player = Guid.TryParse(character, out var owner) ? Player.FindOnline(owner) : Player.FindOnline(character);
            if (player == null) throw new InvalidOperationException("Character must be online and in game.");
            var battle = MonsterPracticeBattles.Start(player, monster, opponent, level);
            Console.WriteLine($"Started practice battle {battle}. No persistent HP, PP or rewards are written.");
        }
        catch (InvalidOperationException exception) { Console.WriteLine($"Practice battle refused: {exception.Message}"); }
        catch (Exception exception) { context.Logger.LogError(exception, "Practice battle start failed"); Console.WriteLine("Practice battle start failed. Check the server log."); }
    }
}

internal sealed class CancelMonsterBattleCommand : ServerCommand
{
    public CancelMonsterBattleCommand() : base(Strings.Commands.CancelMonsterBattle,
        new VariableArgument<string>(new LocaleArgument { Name = "battle", Description = "Battle UUID" }, RequiredIfNotHelp, true)) { }
    protected override void HandleValue(ServerContext context, ParserResult result)
    {
        if (!Guid.TryParse(result.Find(FindArgument<VariableArgument<string>>(0)), out var battle))
        { Console.WriteLine("Usage: cancelmonsterbattle <battle-UUID>"); return; }
        Console.WriteLine(MonsterPracticeBattles.Registry.CancelBattle(battle) ? "Practice battle cancelled." : "Active battle was not found.");
    }
}
