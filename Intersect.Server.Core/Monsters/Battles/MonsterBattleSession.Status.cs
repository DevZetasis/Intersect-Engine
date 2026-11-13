using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Server;
using Intersect.Server.Monsters.Calculations;

namespace Intersect.Server.Monsters.Battles;

internal sealed record BattleMonsterStatus(MonsterStatusDescriptor Definition, int? Turns);

public sealed partial class MonsterBattleSession
{
    private readonly Dictionary<Guid, MonsterStatusDescriptor> _statuses = new();

    private void SnapshotStatuses(Func<Guid, MonsterStatusDescriptor?> findStatus)
    {
        foreach (var actor in _playerSide.Party.Concat(_opponentSide.Party))
        {
            if (actor.Status is { } status)
            {
                actor.Status = status with { Definition = CopyStatus(status.Definition) };
                _statuses[status.Definition.Id] = actor.Status.Definition;
            }
            foreach (var id in actor.Moves.SelectMany(move => move?.Definition.Effects ?? []).Select(effect => effect.StatusId).OfType<Guid>())
                if (!_statuses.ContainsKey(id) && findStatus(id) is { IsValid: true } definition)
                    _statuses.Add(id, CopyStatus(definition));
        }
    }

    private static MonsterStatusDescriptor CopyStatus(MonsterStatusDescriptor source)
    {
        var copy = new MonsterStatusDescriptor(source.Id);
        copy.Load(source.JsonData);
        return copy;
    }

    internal bool ReferencesStatus(Guid id) => _statuses.ContainsKey(id);

    private bool Roll(int chance) => chance == 100 || (chance > 0 && MonsterRandom.Draw(_random, 100) < chance);

    private int? RollDuration(MonsterStatusDescriptor definition) => definition.Duration switch
    {
        MonsterStatusDuration.Fixed => definition.MinimumTurns,
        MonsterStatusDuration.Random => definition.MinimumTurns +
            (int)MonsterRandom.Draw(_random, definition.MaximumTurns - definition.MinimumTurns + 1),
        _ => null,
    };

    private bool CanAct(MonsterBattleCombatant actor, List<string> messages, List<MonsterBattlePresentationEvent> events)
    {
        if (actor.Status is not { } status) return true;
        var definition = status.Definition;
        if (Roll(definition.RecoveryChance))
        {
            RemoveStatus(actor, messages, events);
            return true;
        }

        if (!Roll(definition.PreventActionChance)) return true;
        var message = string.IsNullOrWhiteSpace(definition.PreventActionMessage)
            ? $"{actor.Name} could not act because of {definition.Name}!"
            : $"{actor.Name}: {definition.PreventActionMessage}";
        StatusEvent(MonsterBattleEventKind.StatusBlocked, actor, actor, message, definition.ActivationAnimationId,
            definition.Name, messages, events);
        return false;
    }

    private void ResolveStatusEffect(MonsterBattleCombatant source, MonsterBattleCombatant target, MonsterMoveEffect effect,
        List<string> messages, List<MonsterBattlePresentationEvent> events)
    {
        string failure;
        if (effect.Kind == MonsterMoveEffectKind.CureStatus)
        {
            if (target.Status is { } existing && (effect.StatusId == null || existing.Definition.Id == effect.StatusId))
            {
                RemoveStatus(target, messages, events);
                return;
            }
            failure = $"{target.Name} has no matching status to cure.";
        }
        else if (effect.StatusId is not { } id || !_statuses.TryGetValue(id, out var definition))
        {
            failure = "The status is unavailable.";
        }
        else if (target.Status != null)
        {
            failure = $"{target.Name} already has {target.Status.Definition.Name}.";
        }
        else if (!Roll(effect.Chance))
        {
            failure = $"{definition.Name} did not take hold on {target.Name}.";
        }
        else
        {
            target.Status = new(definition, RollDuration(definition));
            StatusEvent(MonsterBattleEventKind.StatusApplied, source, target, $"{target.Name} gained {definition.Name}!",
                definition.AppliedAnimationId, definition.Name, messages, events);
            return;
        }
        StatusEvent(MonsterBattleEventKind.EffectFailed, source, target, failure, null, "", messages, events);
    }

    private void TickStatus(MonsterBattleCombatant actor, List<string> messages, List<MonsterBattlePresentationEvent> events)
    {
        if (actor.HP == 0 || actor.Status is not { } status) return;
        var definition = status.Definition;
        if (definition.Damage != MonsterStatusDamage.None)
        {
            var before = actor.HP;
            var damage = definition.Damage == MonsterStatusDamage.Fixed
                ? (long)definition.DamageAmount
                : Math.Max(1, (long)decimal.Floor(actor.MaximumHP * (definition.DamageAmount / 100)));
            actor.Damage(damage);
            StatusEvent(MonsterBattleEventKind.StatusDamage, actor, actor,
                $"{actor.Name} lost {before - actor.HP} HP from {definition.Name}!", definition.ActivationAnimationId,
                definition.Name, messages, events, before, actor.HP);
            if (actor.HP == 0)
                StatusEvent(MonsterBattleEventKind.MonsterFainted, actor, actor, $"{actor.Name} fainted!",
                    null, definition.Name, messages, events);
        }

        var remaining = status.Turns ?? RollDuration(definition);
        if (remaining == 1) RemoveStatus(actor, messages, events);
        else if (remaining is { } turns) actor.Status = status with { Turns = turns - 1 };
    }

    private static void RemoveStatus(MonsterBattleCombatant actor, List<string> messages, List<MonsterBattlePresentationEvent> events)
    {
        if (actor.Status is not { } status) return;
        actor.Status = null;
        StatusEvent(MonsterBattleEventKind.StatusRemoved, actor, actor, $"{actor.Name} recovered from {status.Definition.Name}!",
            status.Definition.RemovedAnimationId, "", messages, events);
    }

    private static void StatusEvent(MonsterBattleEventKind kind, MonsterBattleCombatant source, MonsterBattleCombatant target,
        string text, Guid? animation, string status, List<string> messages, List<MonsterBattlePresentationEvent> events,
        long before = 0, long after = 0)
    {
        messages.Add(text);
        events.Add(new()
        {
            Kind = kind, Order = events.Count, ActorId = source.Id, TargetId = target.Id,
            AnimationId = animation, Text = text, StatusName = status, HPBefore = before, HPAfter = after,
        });
    }
}
