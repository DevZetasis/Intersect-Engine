using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;

namespace Intersect.Server.Monsters.Battles;

/// <summary>Serializes battle lifecycle and turns. Publication targets only the owning character.</summary>
public sealed class MonsterBattleRegistry(Action<Guid, MonsterBattleStatePacket> publish, Action<Exception>? reportFailure = null)
{
    public object SyncRoot => Intersect.Server.Monsters.MonsterTypeRules.ContentGate;
    private sealed record ActiveBattle(MonsterBattleSession Session, TaskCompletionSource<MonsterBattleOutcome>? Completion);

    private readonly Dictionary<Guid, ActiveBattle> _active = new();
    private long _sequence;
    public bool HasActive(Guid owner) { lock (SyncRoot) return _active.ContainsKey(owner); }
    public bool HasActiveWild(Guid owner)
    {
        lock (SyncRoot) return _active.TryGetValue(owner, out var session) && session.Session.Wild != null;
    }

    public (Guid? TableId, GeneratedWildMonster Monster)? InspectWild(Guid owner)
    {
        lock (SyncRoot)
            return _active.TryGetValue(owner, out var session) && session.Session.Wild is { } wild
                ? (session.Session.EncounterTableId, wild)
                : null;
    }

    public bool ReservesMonster(Guid monster) { lock (SyncRoot) return _active.Values.Any(s => s.Session.Reserves(monster)); }
    public bool ReservesStatus(Guid status)
    {
        lock (SyncRoot) return _active.Values.Any(session => session.Session.ReferencesStatus(status));
    }

    public Guid Start(Guid owner, Func<MonsterBattleSession> create,
        TaskCompletionSource<MonsterBattleOutcome>? completion = null)
    {
        lock (SyncRoot)
        {
            if (_active.ContainsKey(owner)) throw new InvalidOperationException("This character already has an active monster battle.");
            var session = create();
            if (session.OwnerId != owner) throw new InvalidOperationException("Wrong battle owner.");
            var battle = new ActiveBattle(session, completion);
            _active.Add(owner, battle);
            if (!Send(battle)) throw new InvalidOperationException("Battle start failed while sending battle state.");
            return session.Id;
        }
    }
    public void Submit(Guid owner, MonsterBattleActionPacket action)
    {
        lock (SyncRoot)
        {
            if (!_active.TryGetValue(owner, out var battle) || battle.Session.Id != action.BattleId)
                throw new InvalidOperationException("No matching battle for this character.");
            var session = battle.Session;
            try { session.ValidateAction(owner, action); }
            catch (InvalidOperationException exception)
            {
                // Invalid client actions do not advance the turn. Publish only this owner's current state.
                Send(battle, exception.Message);
                throw;
            }
            var rollback = session.CaptureCheckpoint();
            try
            {
                session.Submit(owner, action);
                session.PersistResolvedTurn(); // Commit all HP/PP before any success publication.
            }
            catch
            {
                rollback();
                session.Cancel("Battle cancelled: turn resolution/persistence could not be confirmed. Last confirmed battle state shown; refresh your roster.");
                _active.Remove(owner);
                Send(battle);
                battle.Completion?.TrySetResult(MonsterBattleOutcome.Cancelled);
                throw;
            }
            if (session.Outcome != MonsterBattleOutcome.Active) _active.Remove(owner);
            if (Send(battle) && session.Outcome != MonsterBattleOutcome.Active)
                battle.Completion?.TrySetResult(session.Outcome);
        }
    }
    public void Cancel(Guid owner, string reason, bool disconnect = false)
    {
        lock (SyncRoot)
        {
            if (!_active.Remove(owner, out var battle)) return;
            battle.Session.Cancel(reason, disconnect);
            Send(battle);
            battle.Completion?.TrySetResult(MonsterBattleOutcome.Cancelled);
        }
    }
    public void CancelAll()
    {
        lock (SyncRoot)
            foreach (var owner in _active.Keys.ToArray()) Cancel(owner, "Monster battle cancelled: server shutdown.");
    }
    public bool CancelBattle(Guid battleId)
    {
        lock (SyncRoot)
        {
            var session = _active.Values.SingleOrDefault(battle => battle.Session.Id == battleId)?.Session;
            if (session == null) return false;
            Cancel(session.OwnerId, "Monster battle cancelled by server operator.");
            return true;
        }
    }
    internal void CancelEventBattle(Guid owner, Guid battleId)
    {
        lock (SyncRoot)
        {
            if (_active.TryGetValue(owner, out var battle) && battle.Session.Id == battleId && battle.Completion != null)
                Cancel(owner, "Monster battle cancelled because its event ended.");
        }
    }

    private bool Send(ActiveBattle battle, string? rejection = null)
    {
        var session = battle.Session;
        try { publish(session.OwnerId, session.Snapshot(checked(++_sequence), rejection)); return true; }
        catch (Exception exception)
        {
            session.Cancel("Battle cancelled because battle state could not be delivered.");
            _active.Remove(session.OwnerId);
            battle.Completion?.TrySetResult(MonsterBattleOutcome.Cancelled);
            try
            {
                if (reportFailure != null) reportFailure(exception);
                else System.Diagnostics.Trace.TraceError("Monster battle publication failed: {0}", exception);
            }
            catch { /* A failing logger must not prevent releasing a battle session. */ }
            return false;
        }
    }
}
