using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Database.PlayerData;
using Microsoft.EntityFrameworkCore;

namespace Intersect.Server.Monsters;

public static class MonsterMoveAssignment
{
    public static Guid?[] Initial(MonsterSpeciesDescriptor? species, int level, Func<Guid, bool> exists)
    {
        var selected = species?.Learnset.Where(entry => entry.IsValid && entry.Level <= level && exists(entry.MoveId))
            .GroupBy(entry => entry.MoveId).Select(group => group.OrderByDescending(entry => entry.Level).First())
            .OrderByDescending(entry => entry.Level).ThenBy(entry => entry.MoveId).Take(4)
            .OrderBy(entry => entry.Level).ThenBy(entry => entry.MoveId).Select(entry => (Guid?)entry.MoveId).ToList() ?? [];
        while (selected.Count < 4) selected.Add(null);
        return selected.ToArray();
    }
    public static void Validate(IReadOnlyList<Guid?> moves)
    {
        if (moves.Count != 4 || moves.Any(id => id == Guid.Empty) || moves.Where(id => id != null).Distinct().Count() != moves.Count(id => id != null))
            throw new InvalidOperationException("A monster requires four distinct-or-empty active move slots.");
    }
    public static bool IsAssigned(Guid moveId, Func<PlayerContext> createContext)
    {
        using var db = createContext();
        return db.OwnedMonsters.AsNoTracking().Any(monster => monster.Move1Id == moveId || monster.Move2Id == moveId || monster.Move3Id == moveId || monster.Move4Id == moveId);
    }
    public static void EnsureUnreferenced(Guid moveId, Func<PlayerContext> createContext)
    {
        if (IsAssigned(moveId, createContext)) throw new InvalidOperationException("This move is assigned to owned monsters, including offline owners, and cannot be deleted.");
    }
}
