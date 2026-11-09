using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Server.Monsters;

public static class MonsterTypeRules
{
    // Serialize editor mutations so validation cannot race a type deletion.
    public static object ContentGate { get; } = new();
    public static void Validate(MonsterTypeDescriptor type, Func<Guid, bool> exists)
    {
        if (!type.IsValid || type.Effectiveness.Keys.Any(id => !exists(id)))
            throw new InvalidOperationException("A type needs a name, a #RRGGBB color, and valid multipliers referencing existing types.");
    }
    public static void Validate(MonsterSpeciesDescriptor species, Func<Guid, bool> exists)
    {
        if (!species.IsValid || species.TypeIds.Any(id => !exists(id)))
            throw new InvalidOperationException("A species needs a name, six positive stats, and at most two distinct existing types.");
    }
    public static bool IsReferenced(Guid id, IEnumerable<MonsterSpeciesDescriptor> species, IEnumerable<MonsterTypeDescriptor> types, IEnumerable<MonsterMoveDescriptor>? moves = null) =>
        species.Any(item => item.TypeIds?.Contains(id) == true) ||
        types.Any(item => item.Id != id && item.Effectiveness?.ContainsKey(id) == true) ||
        moves?.Any(move => move.TypeId == id) == true;
}
