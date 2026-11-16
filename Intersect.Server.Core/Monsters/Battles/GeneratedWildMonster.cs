using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Monsters.Calculations;

namespace Intersect.Server.Monsters.Battles;

/// <summary>Generated opponent identity retained for the lifetime of a battle, independent of ownership.</summary>
public sealed record GeneratedWildMonster(Guid Id, Guid SpeciesId, int Level, MonsterStatValues IVs,
    Guid? NatureId, Guid? GenderId, IReadOnlyList<Guid?> Moves)
{
    public long Experience { get; init; }
    public MonsterStatValues EVs { get; init; } = new();

    internal Guid?[] MoveIds => Moves.ToArray();

    public static GeneratedWildMonster Create(MonsterSpeciesDescriptor species, int level, IMonsterRandom random)
    {
        var nature = MonsterAttributeGeneration.SelectNature(MonsterNatureDescriptor.Lookup.Values.Cast<MonsterNatureDescriptor>(), random);
        var ivs = MonsterAttributeGeneration.GenerateIVs(random);
        var gender = MonsterAttributeGeneration.SelectGender(species, random);
        var moves = MonsterMoveAssignment.Initial(species, level, id => MonsterMoveDescriptor.Get(id) != null);
        return new(Guid.NewGuid(), species.Id, level, ivs, nature, gender, Array.AsReadOnly(moves));
    }
}
