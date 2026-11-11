using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;

namespace Intersect.Server.Monsters.Battles;

/// <summary>Serializes practice lifecycle/turns. Publication targets only the owning character.</summary>
public sealed class MonsterBattleRegistry(Action<Guid, MonsterBattleStatePacket> publish, Action<Exception>? reportFailure = null)
{
    public object SyncRoot => Intersect.Server.Monsters.MonsterTypeRules.ContentGate;
    private readonly Dictionary<Guid, MonsterBattleSession> _active = new();
    private long _sequence;
    public bool HasActive(Guid owner) { lock (SyncRoot) return _active.ContainsKey(owner); }
    public bool ReservesMonster(Guid monster) { lock (SyncRoot) return _active.Values.Any(s => s.Reserves(monster)); }
    public Guid Start(Guid owner, Func<MonsterBattleSession> create)
    {
        lock (SyncRoot)
        {
            if (_active.ContainsKey(owner)) throw new InvalidOperationException("This character already has an active monster battle.");
            var session = create();
            if (session.OwnerId != owner) throw new InvalidOperationException("Wrong battle owner.");
            _active.Add(owner, session);
            if (!Send(session)) throw new InvalidOperationException("Practice start failed while sending battle state.");
            return session.Id;
        }
    }
    public void Submit(Guid owner, MonsterBattleActionPacket action)
    {
        lock (SyncRoot)
        {
            if (!_active.TryGetValue(owner, out var session) || session.Id != action.BattleId)
                throw new InvalidOperationException("No matching battle for this character.");
            try { session.ValidateAction(owner, action); }
            catch (InvalidOperationException exception)
            {
                // Invalid client actions do not advance the turn. Publish only this owner's current state.
                Send(session, exception.Message);
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
                Send(session);
                throw;
            }
            if (session.Outcome != MonsterBattleOutcome.Active) _active.Remove(owner);
            Send(session);
        }
    }
    public void Cancel(Guid owner, string reason, bool disconnect = false)
    {
        lock (SyncRoot)
        {
            if (!_active.Remove(owner, out var session)) return;
            session.Cancel(reason, disconnect); Send(session);
        }
    }
    public void CancelAll()
    {
        lock (SyncRoot)
            foreach (var owner in _active.Keys.ToArray()) Cancel(owner, "Practice battle cancelled: server shutdown.");
    }
    public bool CancelBattle(Guid battleId)
    {
        lock (SyncRoot)
        {
            var session = _active.Values.SingleOrDefault(session => session.Id == battleId);
            if (session == null) return false;
            Cancel(session.OwnerId, "Practice battle cancelled by server operator.");
            return true;
        }
    }
    private bool Send(MonsterBattleSession session, string? rejection = null)
    {
        try { publish(session.OwnerId, session.Snapshot(checked(++_sequence), rejection)); return true; }
        catch (Exception exception)
        {
            session.Cancel("Practice cancelled because battle state could not be delivered.");
            _active.Remove(session.OwnerId);
            try
            {
                if (reportFailure != null) reportFailure(exception);
                else System.Diagnostics.Trace.TraceError("Monster battle publication failed: {0}", exception);
            }
            catch { /* A failing logger must not prevent releasing a practice session. */ }
            return false;
        }
    }
}
