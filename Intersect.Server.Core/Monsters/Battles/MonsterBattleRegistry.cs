using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;

namespace Intersect.Server.Monsters.Battles;

/// <summary>Serializes battle lifecycle and turns. Publication targets only the owning character.</summary>
public sealed class MonsterBattleRegistry(Action<Guid, MonsterBattleStatePacket> publish,
    Action<Exception>? reportFailure = null, TimeProvider? timeProvider = null)
{
    public object SyncRoot => Intersect.Server.Monsters.MonsterTypeRules.ContentGate;
    private sealed record ActiveBattle(MonsterBattleSession Session, TaskCompletionSource<MonsterBattleOutcome>? Completion);

    private sealed record PendingPresentation(MonsterBattleSession Session, TaskCompletionSource<MonsterBattleOutcome> Completion)
    {
        public ITimer? Timeout { get; set; }
    }

    private static readonly TimeSpan PresentationTimeout = TimeSpan.FromMinutes(5);
    private readonly Dictionary<Guid, ActiveBattle> _active = new();
    private readonly Dictionary<Guid, PendingPresentation> _pending = new();
    private long _sequence;
    public bool HasActive(Guid owner) { lock (SyncRoot) return _active.ContainsKey(owner); }
    // Keep the trainer on the event tile until the terminal result is closed or abandoned.
    public bool HasActiveWild(Guid owner)
    {
        lock (SyncRoot) return _pending.ContainsKey(owner) || (_active.TryGetValue(owner, out var session) && session.Session.Wild != null);
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
            if (_active.ContainsKey(owner) || _pending.ContainsKey(owner)) throw new InvalidOperationException("This character already has a battle or a battle result to close.");
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
            if (action.Kind == MonsterBattleActionKind.PresentationClosed)
            {
                if (_pending.TryGetValue(owner, out var pending) && pending.Session.Id == action.BattleId &&
                    pending.Session.Revision == action.Revision && pending.Session.Turn == action.Turn)
                    CompletePresentation(owner, pending.Session.Outcome);
                return;
            }

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
            if (session.Outcome == MonsterBattleOutcome.Active)
            {
                Send(battle);
                return;
            }

            _active.Remove(owner);
            if (battle.Completion is { } completion && session.Outcome is
                MonsterBattleOutcome.Victory or MonsterBattleOutcome.Defeat or MonsterBattleOutcome.Escaped)
            {
                var presentation = new PendingPresentation(session, completion);
                _pending.Add(owner, presentation);
                presentation.Timeout = (timeProvider ?? TimeProvider.System).CreateTimer(_ =>
                {
                    lock (SyncRoot)
                    {
                        if (_pending.TryGetValue(owner, out var current) && ReferenceEquals(current, presentation))
                            CompletePresentation(owner, MonsterBattleOutcome.Cancelled);
                    }
                }, null, PresentationTimeout, Timeout.InfiniteTimeSpan);
                Send(battle, awaitingPresentationClose: true);
                return;
            }

            if (Send(battle)) battle.Completion?.TrySetResult(session.Outcome);
        }
    }
    public void Cancel(Guid owner, string reason, bool disconnect = false)
    {
        lock (SyncRoot)
        {
            CompletePresentation(owner, MonsterBattleOutcome.Cancelled);
            if (!_active.Remove(owner, out var battle)) return;
            battle.Session.Cancel(reason, disconnect);
            Send(battle);
            battle.Completion?.TrySetResult(MonsterBattleOutcome.Cancelled);
        }
    }
    public void CancelAll()
    {
        lock (SyncRoot)
            foreach (var owner in _active.Keys.Concat(_pending.Keys).ToArray()) Cancel(owner, "Monster battle cancelled: server shutdown.");
    }
    public bool CancelBattle(Guid battleId)
    {
        lock (SyncRoot)
        {
            var session = _active.Values.Select(battle => battle.Session)
                .Concat(_pending.Values.Select(presentation => presentation.Session)).SingleOrDefault(session => session.Id == battleId);
            if (session == null) return false;
            Cancel(session.OwnerId, "Monster battle cancelled by server operator.");
            return true;
        }
    }
    internal void CancelEventBattle(Guid owner, Guid battleId)
    {
        lock (SyncRoot)
        {
            if ((_active.TryGetValue(owner, out var battle) && battle.Session.Id == battleId && battle.Completion != null) ||
                (_pending.TryGetValue(owner, out var pending) && pending.Session.Id == battleId))
                Cancel(owner, "Monster battle cancelled because its event ended.");
        }
    }

    private void CompletePresentation(Guid owner, MonsterBattleOutcome outcome)
    {
        if (!_pending.Remove(owner, out var pending)) return;
        pending.Timeout?.Dispose();
        pending.Completion.TrySetResult(outcome);
    }

    private bool Send(ActiveBattle battle, string? rejection = null, bool awaitingPresentationClose = false)
    {
        var session = battle.Session;
        try
        {
            var state = session.Snapshot(checked(++_sequence), rejection);
            state.AwaitingPresentationClose = awaitingPresentationClose;
            publish(session.OwnerId, state);
            return true;
        }
        catch (Exception exception)
        {
            session.Cancel("Battle cancelled because battle state could not be delivered.");
            _active.Remove(session.OwnerId);
            CompletePresentation(session.OwnerId, MonsterBattleOutcome.Cancelled);
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
