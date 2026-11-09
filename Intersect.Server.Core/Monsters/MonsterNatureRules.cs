using Intersect.Framework.Core.GameObjects.Monsters;

namespace Intersect.Server.Monsters;

public static class MonsterNatureRules
{
    public static void Validate(MonsterNatureDescriptor nature)
    {
        if (nature.Modifiers?.HasConflict == true)
            throw new InvalidOperationException("A nature cannot increase and decrease the same stat. Choose different stats or None.");
        if (!nature.IsValid)
            throw new InvalidOperationException("A nature needs a name, non-HP stat selections, percentage magnitudes 0–50, and an integer selection weight 0–100000.");
    }
}
