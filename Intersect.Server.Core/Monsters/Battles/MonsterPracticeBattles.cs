using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Entities;
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
            lock (MonsterTypeRules.ContentGate)
            {
                if (!player.IsAvailableForMonsterPractice) throw new InvalidOperationException("Character must be online, in game, alive and not busy with another interaction.");
                var service = MonsterRoster.CreateService();
                var owned = service.GetForBattle(player.Id, monsterId);
                if (!owned.ConditionInitialized) throw new InvalidOperationException("Monster condition definitions are unavailable.");
                if (owned.CurrentHP == 0) throw new InvalidOperationException("This monster has fainted. Heal its HP before battling.");
                var expectedRevision = owned.Revision;
                ValidateOwnedParty(player.Id, owned);
                var species = MonsterSpeciesDescriptor.Get(owned.SpeciesId) ?? throw new InvalidOperationException("Owned species is missing.");
                var opponent = MonsterSpeciesDescriptor.Get(opponentSpeciesId) ?? throw new InvalidOperationException("Opponent species is missing.");
                var nature = owned.NatureId is { } natureId ? MonsterNatureDescriptor.Get(natureId) ?? throw new InvalidOperationException("Owned nature is missing.") : null;
                var rules = Options.Instance.Monsters.Battle;
                rules.Validate();
                if (opponentLevel < 1 || opponentLevel > rules.MaxLevel) throw new InvalidOperationException("Opponent level is outside the battle rules.");
                var random = new SystemMonsterRandom();
                var opponentNatureId = MonsterAttributeGeneration.SelectNature(MonsterNatureDescriptor.Lookup.Values.Cast<MonsterNatureDescriptor>(), random);
                MonsterBattleCombatant Actor(Guid id, string name, MonsterSpeciesDescriptor template, int level, MonsterStatValues ivs, MonsterStatValues evs, MonsterNatureDescriptor? temperament, Guid?[] moveIds, Database.PlayerData.Players.MonsterInstance? condition = null)
                {
                    if (!template.IsValid || template.TypeIds.Any(type => MonsterTypeDescriptor.Get(type) == null)) throw new InvalidOperationException("Invalid species/types.");
                    var moves = moveIds.Select((moveId, slot) =>
                    {
                        if (moveId == null) return null;
                        var move = MonsterMoveDescriptor.Get(moveId.Value) ?? throw new InvalidOperationException("An assigned move is missing.");
                        return new MonsterBattleMove(move, MonsterTypeDescriptor.Get(move.TypeId) ?? throw new InvalidOperationException("A move type is missing."), condition?.MovePP[slot].Current);
                    }).ToArray();
                    return new(new(id, level, MonsterStatCalculator.Calculate(template.BaseStats, level, ivs, evs, temperament, rules), template.TypeIds), name, template.Sprite, moves, template.FrontBattleSprite, template.BackBattleSprite, condition?.CurrentHP);
                }
                var ownActor = Actor(owned.Id, string.IsNullOrWhiteSpace(owned.Nickname) ? species.Name : owned.Nickname, species,
                    owned.Level, owned.IVs, owned.EVs, nature, owned.MoveIds, owned);
                var opponentActor = Actor(Guid.NewGuid(), opponent.Name, opponent, opponentLevel, MonsterAttributeGeneration.GenerateIVs(random), new(),
                    opponentNatureId is { } selected ? MonsterNatureDescriptor.Get(selected) : null,
                    MonsterMoveAssignment.Initial(opponent, opponentLevel, id => MonsterMoveDescriptor.Get(id) != null));
                return new(new(player.Id, [ownActor]), new(null, [opponentActor]), rules, random, background: string.IsNullOrWhiteSpace(opponent.BattleBackground) ? species.BattleBackground : opponent.BattleBackground,
                    persist: actor => expectedRevision = service.CommitBattle(player.Id, owned.Id, expectedRevision, actor.HP, actor.MaximumHP,
                        actor.Moves.Select(m => new Database.PlayerData.Players.MonsterMovePP(m?.Definition.Id, m?.PP ?? 0, m?.Definition.MaximumPP ?? 0)).ToArray()));
            }
        });
    }
}
