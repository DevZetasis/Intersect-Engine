using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Server.Database.PlayerData.Players;
using Intersect.Server.Monsters.Battles;
using Microsoft.EntityFrameworkCore;

namespace Intersect.Server.Monsters;

public sealed partial class MonsterOwnershipService
{
    internal OwnedMonsterSummary? CommitCapture(Guid owner, InventorySlot liveSlot, Guid itemId, Guid receipt,
        IReadOnlyList<BattleCondition> changes, GeneratedWildMonster? wild, BattleCondition? target, BattleExperience? experience = null)
    {
        if (owner == Guid.Empty || itemId == Guid.Empty || receipt == Guid.Empty ||
            liveSlot.ItemId != itemId || liveSlot.Quantity <= 0 || liveSlot.BagId != null || liveSlot.Bag != null ||
            (liveSlot.PlayerId != Guid.Empty && liveSlot.PlayerId != owner) ||
            (wild == null) != (target == null) || (wild != null && (target!.Id != wild.Id || target.HP <= 0)))
            throw new InvalidOperationException("The capture inventory or opponent changed.");

        lock (MonsterTypeRules.ContentGate)
        lock (Gate(owner))
        {
            using var db = _createContext();
            var slot = db.Player_Items.SingleOrDefault(slot => slot.PlayerId == owner && slot.Slot == liveSlot.Slot);
            if (slot?.LastCaptureAttempt == receipt)
                throw new InvalidOperationException("The capture attempt was already committed.");
            if (liveSlot.Id != Guid.Empty && liveSlot.Id != slot?.Id)
                throw new InvalidOperationException("The persisted inventory slot changed.");
            if (slot == null)
            {
                slot = InventorySlot.CreateForCapture(owner, liveSlot.Slot);
                db.Player_Items.Add(slot);
            }

            // The caller holds the user autosave lock. The live slot is newer than the database after pickups or grants.
            slot.StageCapture(liveSlot, receipt);
            var party = StageBattleConditions(db, owner, changes, receipt);
            MonsterInstance? caught = null;
            if (wild != null)
            {
                var owned = db.OwnedMonsters.AsNoTracking().Where(monster => monster.OwnerPlayerId == owner).ToArray();
                var partySlot = FindSlot(owned, MonsterLocation.Party, PartyCapacity);
                var location = partySlot >= 0 ? MonsterLocation.Party : MonsterLocation.Storage;
                var position = partySlot >= 0 ? partySlot : FindSlot(owned, location, int.MaxValue);
                if (position < 0) throw new InvalidOperationException("No monster storage slot is available.");
                caught = MonsterInstance.FromCapture(owner, receipt, location, position, wild, target!, _rules);
                db.OwnedMonsters.Add(caught);
                StageExperience(party, experience, receipt);
            }

            try
            {
                // EF commits the inventory slot, existing conditions and captured monster in a single transaction.
                if (db.SaveChanges() <= 0) throw new InvalidOperationException("Capture attempt was not committed.");
            }
            catch
            {
                using var verify = _createContext();
                var saved = verify.Player_Items.AsNoTracking().SingleOrDefault(row => row.PlayerId == owner &&
                    row.Slot == liveSlot.Slot && row.LastCaptureAttempt == receipt);
                if (saved == null) throw;
                slot = saved;
            }
            liveSlot.ConfirmCapture(slot);
            return caught?.ToSummary();
        }
    }
}
