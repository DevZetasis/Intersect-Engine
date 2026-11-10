using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Server;
using Intersect.Server.Monsters.Calculations;

namespace Intersect.Server.Monsters.Battles;

public sealed class MonsterBattleMove
{
    internal MonsterMoveDescriptor Definition { get; }
    internal MonsterTypeDescriptor Type { get; }
    public int PP { get; private set; }
    public bool Executable => Definition.Category != MonsterMoveCategory.Status && Definition.Target == MonsterMoveTarget.OneOpponent;
    public bool Usable => Executable && PP > 0;
    public MonsterBattleMove(MonsterMoveDescriptor move, MonsterTypeDescriptor type)
    {
        if (!move.IsValid || !type.IsValid || move.TypeId != type.Id) throw new InvalidOperationException("Invalid battle move/type.");
        Definition = new MonsterMoveDescriptor(move.Id); Definition.Load(move.JsonData);
        Type = new MonsterTypeDescriptor(type.Id); Type.Load(type.JsonData);
        PP = Definition.MaximumPP;
    }
    internal void Consume() { if (!Usable) throw new InvalidOperationException("Move is not usable."); PP--; }
    public MonsterBattleMoveView View(int slot) => new()
    {
        Slot = slot, Name = Definition.Name, Type = Type.Name, Category = Definition.Category.ToString(),
        PP = PP, MaximumPP = Definition.MaximumPP, Usable = Usable,
        UnavailableReason = !Executable ? "Effect/target not implemented" : PP == 0 ? "No PP remaining" : string.Empty,
    };
}

public sealed class MonsterBattleCombatant
{
    internal MonsterCalculationActor Calculation { get; }
    internal MonsterBattleMove?[] Moves { get; }
    public Guid Id => Calculation.Id;
    public string Name { get; }
    public string Sprite { get; }
    public long HP { get; private set; }
    public long MaximumHP => Calculation.Stats.HP;
    public MonsterBattleCombatant(MonsterCalculationActor actor, string name, string sprite, MonsterBattleMove?[] moves)
    {
        if (actor.Id == Guid.Empty || actor.Level < 1 || actor.Stats.ToArray().Any(value => value <= 0) ||
            moves.Length != 4 || moves.Where(move => move != null).Select(move => move!.Definition.Id).Distinct().Count() != moves.Count(move => move != null))
            throw new InvalidOperationException("Invalid battle participant or move inventory.");
        if (actor.TypeIds == null || actor.TypeIds.Count > 2 || actor.TypeIds.Any(id => id == Guid.Empty) || actor.TypeIds.Distinct().Count() != actor.TypeIds.Count)
            throw new InvalidOperationException("Invalid battle types.");
        Calculation = actor with { TypeIds = actor.TypeIds.ToArray() };
        Name = name; Sprite = sprite;
        Moves = moves.Select(move => move == null ? null : new MonsterBattleMove(move.Definition, move.Type)).ToArray();
        HP = MaximumHP;
    }
    internal MonsterBattleCombatant Copy() => new(Calculation, Name, Sprite, Moves);
    internal void Damage(long value) { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); HP -= Math.Min(HP, value); }
    internal int[] LegalSlots() => Enumerable.Range(0, Moves.Length).Where(slot => Moves[slot]?.Usable == true).ToArray();
    public MonsterBattleActorView View() => new() { Id = Id, Name = Name, Sprite = Sprite, Level = Calculation.Level, HP = HP, MaximumHP = MaximumHP };
}

/// <summary>Side slots allow future two-active formats; this resolver currently requires exactly one per side.</summary>
public sealed record MonsterBattleSide(Guid? ControllerId, IReadOnlyList<MonsterBattleCombatant> Active);

public sealed record MonsterBattleDecision(IReadOnlyList<int> LegalMoveSlots, MonsterBattleActorView Self, IReadOnlyList<MonsterBattleActorView> Opponents);
public interface IMonsterBattleDecisionProvider
{
    int ChooseMove(MonsterBattleDecision view, IMonsterRandom random);
}
public sealed class RandomPracticeDecisionProvider : IMonsterBattleDecisionProvider
{
    public int ChooseMove(MonsterBattleDecision view, IMonsterRandom random) => view.LegalMoveSlots[(int)MonsterRandom.Draw(random, view.LegalMoveSlots.Count)];
}
