using Intersect.Framework.Core.GameObjects.Items;
using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Localization;
using Intersect.Server.Core.CommandParsing;
using Intersect.Server.Core.CommandParsing.Arguments;
using Intersect.Server.Entities;
using Intersect.Server.Localization;
using Intersect.Server.Monsters.Battles;

namespace Intersect.Server.Core.Commands;

internal sealed class GrantCaptureDeviceCommand : ServerCommand
{
    public GrantCaptureDeviceCommand() : base(Strings.Commands.GrantCaptureDevice,
        new VariableArgument<string>(new LocaleArgument { Name = "character", Description = "Online character name or UUID" }, RequiredIfNotHelp, true),
        new VariableArgument<string>(new LocaleArgument { Name = "item", Description = "Capture device item UUID" }, RequiredIfNotHelp, true),
        new VariableArgument<string>(new LocaleArgument { Name = "quantity", Description = "Positive quantity" }, RequiredIfNotHelp, true)) { }

    protected override void HandleValue(ServerContext context, ParserResult result)
    {
        var target = result.Find(FindArgument<VariableArgument<string>>(0));
        var player = Guid.TryParse(target, out var playerId) ? Player.FindOnline(playerId) : Player.FindOnline(target);
        if (player is not { InGame: true } ||
            !Guid.TryParse(result.Find(FindArgument<VariableArgument<string>>(1)), out var itemId) ||
            !int.TryParse(result.Find(FindArgument<VariableArgument<string>>(2)), out var quantity) || quantity <= 0 ||
            ItemDescriptor.Get(itemId) is not { ItemType: ItemType.MonsterCaptureDevice } item ||
            !MonsterCaptureChance.IsDeviceMultiplier(item.CaptureMultiplier))
        {
            Console.WriteLine("Select an online character, a capture device UUID, and a positive quantity.");
            return;
        }

        lock (player.EntityLock)
        {
            if (!player.InGame || MonsterPracticeBattles.Registry.HasActive(player.Id))
            {
                Console.WriteLine("Grant capture devices while the character is online and outside battle.");
                return;
            }

            Console.WriteLine(player.TryGiveItem(item.Id, quantity)
                ? $"Granted {quantity} × {item.Name} to {player.Name}."
                : "The character's inventory could not accept the devices.");
        }
    }
}
