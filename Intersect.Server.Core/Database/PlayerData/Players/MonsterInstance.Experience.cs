using System.ComponentModel.DataAnnotations.Schema;
using Intersect.Framework.Core.GameObjects.Monsters;
using Newtonsoft.Json;

namespace Intersect.Server.Database.PlayerData.Players;

public sealed partial class MonsterInstance
{
    private List<MonsterMoveLearning> _learning = [];

    public bool ExperienceInitialized { get; private set; }

    [NotMapped]
    public IReadOnlyList<MonsterMoveLearning> Learning => _learning;

    [Column("MoveLearning")]
    public string MoveLearningJson
    {
        get => JsonConvert.SerializeObject(_learning);
        set => _learning = JsonConvert.DeserializeObject<List<MonsterMoveLearning>>(value ?? "[]") ?? [];
    }

    internal void SetProgress(int level, long experience)
    {
        if (level < Level || experience < 0) throw new ArgumentOutOfRangeException(nameof(level));
        Level = level;
        Experience = experience;
        ExperienceInitialized = true;
    }

    internal void QueueMove(Guid moveId) => _learning.Add(new(Guid.NewGuid(), moveId));

    internal void CompleteMove(Guid revision)
    {
        _learning.RemoveAt(0);
        Revision = revision;
    }
}
