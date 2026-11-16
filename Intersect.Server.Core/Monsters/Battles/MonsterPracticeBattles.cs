using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Entities;
using Intersect.Server.Entities.Events;
using Intersect.Framework.Core.GameObjects.Items;
using Intersect.Server.Networking;
using Intersect.Framework.Core.GameObjects.Events.Commands;
using Intersect.Network.Packets.Server;
using Intersect.Server.Database.PlayerData.Players;
using Intersect.Server.Monsters.Calculations;
using Microsoft.Extensions.Logging;

namespace Intersect.Server.Monsters.Battles;

public static class MonsterPracticeBattles
{
    internal static void ValidateOwnedParty(Guid owner, Database.PlayerData.Players.MonsterInstance monster)
    {
        if (monster.OwnerPlayerId != owner || monster.Location != MonsterLocation.Party)
            throw new InvalidOperationException("The monster must belong to this character and be in the party.");
    }
    public static MonsterBattleRegistry Registry { get; } = new((owner, packet) =>
    {
        if (Player.FindOnline(owner) is { } player) { player.SendPacket(packet); MonsterRoster.Send(player); }
    },
        exception => Intersect.Core.ApplicationContext.Context.Value?.Logger.LogError(exception, "Could not publish monster battle state"));
    public static Guid Start(Player player, Guid monsterId, Guid opponentSpeciesId, int opponentLevel)
    {
        return Registry.Start(player.Id, () =>
        {
            if (!player.IsAvailableForMonsterPractice)
                throw new InvalidOperationException("Character must be online, in game, alive and not busy with another interaction.");
            var service = MonsterRoster.CreateService();
            var party = service.GetPartyForBattle(player.Id);
            var owned = party.SingleOrDefault(member => member.Id == monsterId)
                ?? throw new InvalidOperationException("Select a monster in this character's party.");
            return CreateSession(player, service, party, owned, opponentSpeciesId, opponentLevel, new SystemMonsterRandom());
        });
    }

    internal static bool TryStartWild(Player player, MonsterEncounterSelection.Encounter encounter, IMonsterRandom random)
    {
        // The caller holds the content/registry gate through resolution and start.
        if (!player.IsAvailableForWildEncounter || Registry.HasActive(player.Id)) return false;
        return TryStartWildCore(player, encounter.SpeciesId, encounter.Level, random, encounter.TableId, null, allowCapture: true) != null;
    }

    internal static (Guid Id, Task<MonsterBattleOutcome> Result)? TryStartEventBattle(
        Player player, StartMonsterBattleCommand command, Entities.Events.Event source)
    {
        lock (Registry.SyncRoot)
        {
            if (!player.IsAvailableForEventMonsterBattle(source) || Registry.HasActive(player.Id) ||
                !command.IsValid(Options.Instance.Monsters.Battle.MaxLevel, id => MonsterSpeciesDescriptor.Get(id) != null))
                return null;

            var result = new TaskCompletionSource<MonsterBattleOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            var id = TryStartWildCore(player, command.SpeciesId, command.Level, new SystemMonsterRandom(), null, result, command.AllowCapture);
            return id is { } battleId ? (battleId, result.Task) : null;
        }
    }

    private static Guid? TryStartWildCore(Player player, Guid speciesId, int level, IMonsterRandom random,
        Guid? tableId, TaskCompletionSource<MonsterBattleOutcome>? result, bool allowCapture)
    {
        var service = MonsterRoster.CreateService();
        var party = service.GetPartyForBattle(player.Id);
        var lead = party.FirstOrDefault(member => member is { ConditionInitialized: true, CurrentHP: > 0 } &&
            member.MoveIds.Select((id, slot) => (id, slot)).Any(move => move.id is { } id &&
                member.MovePP[move.slot].Current > 0 && MonsterMoveDescriptor.Get(id) is { } descriptor &&
                descriptor.ExecutableInBattle));
        if (lead == null) return null;

        return Registry.Start(player.Id, () => CreateSession(player, service, party, lead,
            speciesId, level, random, isWild: true, tableId: tableId, allowCapture: allowCapture), result);
    }

    private static MonsterBattleSession CreateSession(Player player, MonsterOwnershipService service, MonsterInstance[] party,
        MonsterInstance owned, Guid opponentSpeciesId, int opponentLevel, IMonsterRandom random, bool isWild = false, Guid? tableId = null, bool allowCapture = false)
    {
        if (!owned.ConditionInitialized) throw new InvalidOperationException("Monster condition definitions are unavailable.");
        if (owned.CurrentHP == 0) throw new InvalidOperationException("This monster has fainted. Heal its HP before battling.");
        var expectedRevisions = party.ToDictionary(member => member.Id, member => member.Revision);
        ValidateOwnedParty(player.Id, owned);
        var species = MonsterSpeciesDescriptor.Get(owned.SpeciesId) ?? throw new InvalidOperationException("Owned species is missing.");
        var opponent = MonsterSpeciesDescriptor.Get(opponentSpeciesId) ?? throw new InvalidOperationException("Opponent species is missing.");
        var rules = Options.Instance.Monsters.Battle;
        rules.Validate();
        if (opponentLevel < 1 || opponentLevel > rules.MaxLevel) throw new InvalidOperationException("Opponent level is outside the battle rules.");
        var generated = GeneratedWildMonster.Create(opponent, opponentLevel, random);
        MonsterBattleCombatant Actor(Guid id, string name, MonsterSpeciesDescriptor template, int level, MonsterStatValues ivs, MonsterStatValues evs, MonsterNatureDescriptor? temperament, Guid?[] moveIds, Database.PlayerData.Players.MonsterInstance? condition = null, Guid? generatedGender = null)
        {
            if (!template.IsValid || template.TypeIds.Any(type => MonsterTypeDescriptor.Get(type) == null)) throw new InvalidOperationException("Invalid species/types.");
            var moves = moveIds.Select((moveId, slot) =>
            {
                if (moveId == null) return null;
                var move = MonsterMoveDescriptor.Get(moveId.Value) ?? throw new InvalidOperationException("An assigned move is missing.");
                return new MonsterBattleMove(move, MonsterTypeDescriptor.Get(move.TypeId) ?? throw new InvalidOperationException("A move type is missing."), condition?.MovePP[slot].Current);
            }).ToArray();
            var genderId = condition == null ? generatedGender : condition.GenderId;
            var gender = genderId is { } assigned ? MonsterGenderDescriptor.Get(assigned) : null;
            return new MonsterBattleCombatant(new(id, level, MonsterStatCalculator.Calculate(template.BaseStats, level, ivs, evs, temperament, rules), template.TypeIds), name, template.Sprite, moves, template.FrontBattleSprite, template.BackBattleSprite, condition?.CurrentHP, condition?.Slot ?? -1)
            {
                GenderId = genderId, GenderName = gender?.Name, GenderSymbol = gender?.Symbol,
                Status = condition?.StatusId is { } statusId &&
                    MonsterStatusDescriptor.Get(statusId) is { IsValid: true, PersistOutsideBattle: true } status
                    ? new(status, condition.StatusTurns) : null,
            };
        }
        var ownParty = party.Select(member =>
        {
            ValidateOwnedParty(player.Id, member);
            if (!member.ConditionInitialized) throw new InvalidOperationException("Party monster condition definitions are unavailable.");
            var template = MonsterSpeciesDescriptor.Get(member.SpeciesId) ?? throw new InvalidOperationException("Owned species is missing.");
            var nature = member.NatureId is { } id
                ? MonsterNatureDescriptor.Get(id) ?? throw new InvalidOperationException("Owned nature is missing.")
                : null;
            return Actor(member.Id, string.IsNullOrWhiteSpace(member.Nickname) ? template.Name : member.Nickname,
                template, member.Level, member.IVs, member.EVs, nature, member.MoveIds, member);
        }).ToArray();
        var opponentActor = Actor(generated.Id, opponent.Name, opponent, generated.Level, generated.IVs, generated.EVs,
            generated.NatureId is { } selected ? MonsterNatureDescriptor.Get(selected) : null,
            generated.MoveIds, generatedGender: generated.GenderId);
        MonsterOwnershipService.BattleCondition Condition(MonsterBattleCombatant actor) => new(actor.Id,
            expectedRevisions.GetValueOrDefault(actor.Id), actor.HP, actor.MaximumHP,
            actor.Moves.Select(move => new MonsterMovePP(move?.Definition.Id, move?.PP ?? 0, move?.Definition.MaximumPP ?? 0)).ToArray(),
            actor.PersistentStatus.Id, actor.PersistentStatus.Turns);

        IEnumerable<InventorySlot> CaptureSlots() =>
            player.Items.Where(slot => slot is { Quantity: > 0, BagId: null, Bag: null, Descriptor: { ItemType: ItemType.MonsterCaptureDevice } item } &&
                MonsterCaptureChance.IsDeviceMultiplier(item.CaptureMultiplier) &&
                Conditions.MeetsConditionLists(item.UsageRequirements, player, null));

        MonsterBattleSession.CaptureDevice? FindDevice(Guid id) =>
            CaptureSlots().FirstOrDefault(slot => slot.ItemId == id)?.Descriptor is { } item
                ? new(item.Name, item.CaptureMultiplier, item.GuaranteedCapture)
                : null;

        string? PersistCapture(MonsterBattleSession.CaptureTurn capture, IReadOnlyList<MonsterBattleCombatant> actors)
        {
            var (caught, slotIndex) = player.User.WithSaveLock(() =>
            {
                var slot = CaptureSlots().FirstOrDefault(slot => slot.ItemId == capture.ItemId)
                    ?? throw new InvalidOperationException("The capture device is no longer in your inventory.");
                var result = service.CommitCapture(player.Id, slot, capture.ItemId, capture.Revision,
                    actors.Select(Condition).ToArray(), capture.Wild, capture.Target == null ? null : Condition(capture.Target));
                return (result, slot.Slot);
            });
            foreach (var actor in actors) expectedRevisions[actor.Id] = capture.Revision;
            try { PacketSender.SendInventoryItemUpdate(player, slotIndex); }
            catch (Exception exception)
            {
                Intersect.Core.ApplicationContext.Context.Value?.Logger.LogWarning(exception, "Unable to send committed capture inventory change");
            }
            return caught == null ? null : $"{caught.Location.ToString().ToLowerInvariant()} slot {caught.Slot + 1}";
        }

        return new(new(player.Id, ownParty, owned.Id), new(null, [opponentActor]), rules, random,
            background: string.IsNullOrWhiteSpace(opponent.BattleBackground) ? species.BattleBackground : opponent.BattleBackground,
            persistParty: actors =>
            {
                var revision = service.CommitBattle(player.Id, actors.Select(Condition).ToArray());
                foreach (var actor in actors) expectedRevisions[actor.Id] = revision;
            }, wild: isWild ? generated : null, encounterTableId: tableId, baseCaptureChance: opponent.BaseCaptureChance,
            captureDevices: allowCapture ? () => CaptureSlots().GroupBy(slot => slot.ItemId).Select(group => new MonsterBattleCaptureDevice
            {
                Id = group.Key,
                Name = group.First().Descriptor.Name,
                Quantity = (int)Math.Min(int.MaxValue, group.Sum(slot => (long)slot.Quantity)),
            }).OrderBy(device => device.Name).ToArray() : null,
            captureDevice: allowCapture ? FindDevice : null, persistCapture: allowCapture ? PersistCapture : null);
    }
}
