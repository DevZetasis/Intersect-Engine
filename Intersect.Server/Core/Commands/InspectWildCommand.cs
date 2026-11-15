using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Localization;
using Intersect.Server.Core.CommandParsing;
using Intersect.Server.Core.CommandParsing.Arguments;
using Intersect.Server.Entities;
using Intersect.Server.Localization;
using Intersect.Server.Monsters.Battles;

namespace Intersect.Server.Core.Commands;

internal sealed class InspectWildCommand : ServerCommand
{
    public InspectWildCommand() : base(Strings.Commands.InspectWild,
        new VariableArgument<string>(new LocaleArgument { Name = "character", Description = "Online character name or UUID" }, RequiredIfNotHelp, true))
    {
    }

    protected override void HandleValue(ServerContext context, ParserResult result)
    {
        var character = result.Find(FindArgument<VariableArgument<string>>(0));
        var player = Guid.TryParse(character, out var id) ? Player.FindOnline(id) : Player.FindOnline(character);
        if (player == null || MonsterPracticeBattles.Registry.InspectWild(player.Id) is not { } encounter)
        {
            Console.WriteLine("This character has no active wild encounter.");
            return;
        }

        var monster = encounter.Monster;
        var species = MonsterSpeciesDescriptor.Get(monster.SpeciesId)?.Name ?? "Unavailable";
        var nature = monster.NatureId is { } natureId ? MonsterNatureDescriptor.Get(natureId)?.Name ?? "Unavailable" : "Unassigned";
        var gender = monster.GenderId is { } genderId ? MonsterGenderDescriptor.Get(genderId)?.Name ?? "Unavailable" : "Unspecified";
        Console.WriteLine($"Table {encounter.TableId}; individual {monster.Id}; species {species} ({monster.SpeciesId}); level {monster.Level}.");
        Console.WriteLine($"IVs (HP/Atk/Def/SpA/SpD/Spe): {string.Join("/", monster.IVs.ToArray())}. Nature: {nature} ({monster.NatureId}). Gender: {gender} ({monster.GenderId}).");
        Console.WriteLine("Moves: " + string.Join(", ", monster.Moves.Select(move =>
            move is { } moveId ? MonsterMoveDescriptor.Get(moveId)?.Name ?? moveId.ToString() : "Empty")));
    }
}
