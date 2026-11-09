using Intersect.Config;
using Intersect.Server.Database.PlayerData.Players;
using Intersect.Server.Monsters.Calculations;
using Microsoft.EntityFrameworkCore;

namespace Intersect.Server.Database.PlayerData;

public abstract partial class PlayerContext
{
    public MonsterBattleRules MonsterRules { get; set; } = Options.Instance?.Monsters?.Battle ?? MonsterBattleRules.Generation4;
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ValidateMonsterChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ValidateMonsterChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
    private void ValidateMonsterChanges()
    {
        // Do not change the existing autosave behavior of contexts that only track players/items.
        if (!ChangeTracker.Entries<MonsterInstance>().Any()) return;
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<MonsterInstance>().Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            MonsterStatCalculator.ValidateIVs(entry.Entity.IVs);
            var evsChanged = entry.State == EntityState.Added || entry.Properties.Any(property =>
                property.Metadata.Name.StartsWith("Ev", StringComparison.Ordinal) && !Equals(property.OriginalValue, property.CurrentValue));
            // Lowering configured EV caps must not prevent metadata/placement saves or truncate an existing profile.
            MonsterStatCalculator.ValidateEVs(entry.Entity.EVs, evsChanged ? MonsterRules : MonsterBattleRules.Generation4 with { MaxEvTotal = 1530 });
            if (entry.Entity.NatureId == Guid.Empty) throw new InvalidOperationException("Nature UUID cannot be empty; use null for neutral/unassigned.");
            if (entry.State == EntityState.Modified && entry.Properties.Any(property =>
                    (property.Metadata.Name.StartsWith("Iv", StringComparison.Ordinal) || property.Metadata.Name == nameof(MonsterInstance.NatureId)) &&
                    !Equals(property.OriginalValue, property.CurrentValue)))
                throw new InvalidOperationException("Initialized monster IVs and Nature UUID cannot be rerolled or reassigned.");
        }

    }
}
