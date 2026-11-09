using Intersect.Server.Database.PlayerData;
using Microsoft.EntityFrameworkCore;

namespace Intersect.Server.Monsters;

public static class MonsterNatureReferences
{
    public static bool IsAssigned(Guid natureId, Func<PlayerContext> createContext)
    {
        using var db = createContext();
        return db.OwnedMonsters.AsNoTracking().Any(monster => monster.NatureId == natureId);
    }
    public static void EnsureUnreferenced(Guid natureId, Func<PlayerContext> createContext)
    {
        if (IsAssigned(natureId, createContext))
            throw new InvalidOperationException("This nature is assigned to owned monsters, including offline owners, and cannot be deleted.");
    }
}
