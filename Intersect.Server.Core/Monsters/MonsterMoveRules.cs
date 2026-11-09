using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Server.Monsters;

public static class MonsterMoveRules
{
    public static void Validate(MonsterMoveDescriptor move, Func<Guid, bool> typeExists, Func<Guid, bool> animationExists)
    {
        if (!move.IsValid || !typeExists(move.TypeId) || (move.AnimationId is { } animation && !animationExists(animation)))
            throw new InvalidOperationException("A move needs a name, an existing type, valid category/target, positive damaging power, accuracy 1–100, and an existing optional animation.");
    }
    public static void ValidateLearnset(MonsterSpeciesDescriptor species, Func<Guid, bool> moveExists)
    {
        if (species.Learnset == null || species.Learnset.Any(entry => entry is not { IsValid: true } || !moveExists(entry.MoveId)))
            throw new InvalidOperationException("Learnset entries need an existing move and a positive learn level.");
    }
    public static bool IsReferenced(Guid id, IEnumerable<MonsterSpeciesDescriptor> species) =>
        species.Any(item => item.Learnset?.Any(entry => entry.MoveId == id) == true);
}
