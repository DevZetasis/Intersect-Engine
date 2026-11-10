using System.ComponentModel.DataAnnotations.Schema;
using Intersect.Framework.Core.GameObjects.Monsters;
using Newtonsoft.Json;

namespace Intersect.Server.Database.PlayerData.Players;

[NotMapped]
public sealed record MonsterMovePP(Guid? MoveId, int Current, int Maximum);

public sealed partial class MonsterInstance
{
    public bool ConditionInitialized { get; private set; }
    public long CurrentHP { get; private set; }
    public long MaximumHPAtLastUpdate { get; private set; }
    private MonsterMovePP[] _movePP = [];
    [NotMapped] public IReadOnlyList<MonsterMovePP> MovePP => _movePP;
    [Column("MovePP")] public string MovePPJson
    {
        get => JsonConvert.SerializeObject(_movePP);
        set => _movePP = JsonConvert.DeserializeObject<MonsterMovePP[]>(value ?? "[]") ?? [];
    }
    internal bool ReconcileCondition(long maximumHP, MonsterMoveDescriptor?[] moves)
    {
        if (maximumHP < 1 || moves.Length != 4) throw new InvalidOperationException("Invalid condition definitions.");
        if (ConditionInitialized && _movePP.Length != 4) throw new InvalidOperationException("Malformed stored PP slots.");
        var hp = !ConditionInitialized ? maximumHP : CurrentHP == 0 ? 0 :
            maximumHP > MaximumHPAtLastUpdate ? maximumHP - Math.Min(maximumHP, MaximumHPAtLastUpdate - CurrentHP) : Math.Min(CurrentHP, maximumHP);
        var pp = MoveIds.Select((id, slot) =>
        {
            var maximum = id == null ? 0 : moves[slot]?.MaximumPP ?? 0;
            var old = _movePP.ElementAtOrDefault(slot);
            var current = !ConditionInitialized || old == null || old.MoveId != id || old.Maximum == 0 && maximum > 0 ? maximum : Math.Clamp(old.Current, 0, maximum);
            return new MonsterMovePP(id, current, maximum);
        }).ToArray();
        if (ConditionInitialized && hp == CurrentHP && maximumHP == MaximumHPAtLastUpdate && pp.SequenceEqual(_movePP)) return false;
        SetCondition(hp, maximumHP, pp, Guid.NewGuid()); return true;
    }
    internal void SetCondition(long hp, long maximumHP, MonsterMovePP[] pp, Guid revision)
    {
        if (maximumHP < 1 || hp < 0 || hp > maximumHP || pp.Length != 4 || !pp.Select(p => p.MoveId).SequenceEqual(MoveIds) ||
            pp.Any(p => p.Current < 0 || p.Maximum is < 0 or > 999 || p.Current > p.Maximum || p.MoveId == null && (p.Current != 0 || p.Maximum != 0)))
            throw new InvalidOperationException("Invalid monster HP/PP.");
        CurrentHP = hp; MaximumHPAtLastUpdate = maximumHP; _movePP = pp.ToArray(); ConditionInitialized = true; Revision = revision;
    }
    internal void AssignMove(int slot, Guid? move)
    {
        var ids = MoveIds; if (slot is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(slot)); ids[slot] = move;
        Intersect.Server.Monsters.MonsterMoveAssignment.Validate(ids);
        (Move1Id, Move2Id, Move3Id, Move4Id) = (ids[0], ids[1], ids[2], ids[3]); MovesInitialized = true; Revision = Guid.NewGuid();
    }
    internal void ValidateCondition()
    {
        if (!ConditionInitialized)
        { if (CurrentHP != 0 || MaximumHPAtLastUpdate != 0 || _movePP.Length != 0) throw new InvalidOperationException("Uninitialized condition has resource values."); return; }
        if (CurrentHP < 0 || MaximumHPAtLastUpdate < 1 || CurrentHP > MaximumHPAtLastUpdate || _movePP.Length != 4 ||
            !_movePP.Select(p => p.MoveId).SequenceEqual(MoveIds) || _movePP.Any(p => p.Current < 0 || p.Maximum is < 0 or > 999 || p.Current > p.Maximum || p.MoveId == null && (p.Current != 0 || p.Maximum != 0)))
            throw new InvalidOperationException("Invalid persistent monster condition.");
    }
}
