using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Database.PlayerData.Players;

namespace Intersect.Server.Monsters;

public sealed partial class MonsterOwnershipService
{
    internal sealed record BattleExperience(int Cap, IReadOnlyDictionary<Guid, long> Awards, List<string> Messages);

    private void StageExperience(Dictionary<Guid, MonsterInstance> monsters, BattleExperience? experience, Guid revision)
    {
        if (experience == null) return;
        foreach (var (id, reward) in experience.Awards)
        {
            var monster = monsters[id];
            if (reward <= 0 || monster.Level >= experience.Cap) continue;

            var species = _species(monster.SpeciesId) ?? throw new InvalidOperationException("Missing species for battle reward.");
            var oldLevel = monster.Level;
            var level = oldLevel;
            var minimum = MonsterExperienceCurveDescriptor.ForSpecies(species, level);
            var next = MonsterExperienceCurveDescriptor.ForSpecies(species, level + 1);
            // Legacy records only stored a placeholder. Also normalize safely after editing a curve.
            var start = monster.ExperienceInitialized ? Math.Clamp(monster.Experience, minimum, next - 1) : minimum;
            var limit = MonsterExperienceCurveDescriptor.ForSpecies(species, experience.Cap);
            var gained = Math.Min(reward, limit - start);
            var total = start + gained;
            while (level < experience.Cap && total >= MonsterExperienceCurveDescriptor.ForSpecies(species, level + 1))
                level++;

            monster.SetProgress(level, total);
            var name = string.IsNullOrWhiteSpace(monster.Nickname) ? species.Name : monster.Nickname;
            experience.Messages.Add($"{name} gained {gained} EXP!");
            if (level > oldLevel)
            {
                experience.Messages.Add($"{name} grew to Level {level}!");
                var pendingBefore = monster.Learning.Count;
                foreach (var entry in species.Learnset.Where(entry => entry.Level > oldLevel && entry.Level <= level)
                             .OrderBy(entry => entry.Level))
                {
                    if (_move(entry.MoveId) is not { } move || monster.MoveIds.Contains(move.Id) ||
                        monster.Learning.Any(decision => decision.MoveId == move.Id)) continue;
                    var slot = Array.IndexOf(monster.MoveIds, null);
                    if (slot >= 0)
                    {
                        monster.AssignMove(slot, move.Id);
                        experience.Messages.Add($"{name} learned {move.Name}!");
                    }
                    else
                    {
                        monster.QueueMove(move.Id);
                    }
                }
                var pending = monster.Learning.Count - pendingBefore;
                if (pending == 1 && _move(monster.Learning[^1].MoveId) is { } queued)
                    experience.Messages.Add($"{name} wants to learn {queued.Name}!");
                else if (pending > 1)
                    experience.Messages.Add($"{name} has {pending} new moves to review in Monsters.");
                ReconcileCondition(monster);
            }
            // Reconciliation or move assignment may have changed the revision in memory.
            monster.SetStatus(monster.StatusId, monster.StatusTurns, revision);
        }
    }

    internal bool ResolveMoveLearning(Guid owner, Guid monsterId, Guid decisionId, int slot)
    {
        if (decisionId == Guid.Empty || slot is < -1 or > 3) return false;
        lock (MonsterTypeRules.ContentGate)
        lock (Gate(owner))
        {
            using var db = _createContext();
            var monster = db.OwnedMonsters.SingleOrDefault(item => item.OwnerPlayerId == owner && item.Id == monsterId);
            if (monster?.Learning.FirstOrDefault() is not { } head || head.Id != decisionId ||
                _reserved(monster.Id)) return false;
            if (slot >= 0)
            {
                if (_move(head.MoveId) is not { } move || monster.MoveIds.Contains(move.Id)) return false;
                monster.AssignMove(slot, move.Id);
                ReconcileCondition(monster);
            }
            monster.CompleteMove(Guid.NewGuid());
            return db.SaveChanges() > 0;
        }
    }
}
