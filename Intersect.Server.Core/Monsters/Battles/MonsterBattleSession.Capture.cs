using Intersect.Framework.Core.GameObjects.Monsters;
using Intersect.Network.Packets.Client;
using Intersect.Network.Packets.Server;
using Intersect.Server.Monsters.Calculations;

namespace Intersect.Server.Monsters.Battles;

public sealed partial class MonsterBattleSession
{
    public sealed record CaptureDevice(string Name, decimal Multiplier, bool Guaranteed);
    public sealed record CaptureTurn(Guid Revision, Guid ItemId, GeneratedWildMonster? Wild, MonsterBattleCombatant? Target);

    private readonly int _baseCaptureChance;
    private readonly Func<MonsterBattleCaptureDevice[]>? _captureDevices;
    private readonly Func<Guid, CaptureDevice?>? _captureDevice;
    private readonly Func<CaptureTurn, IReadOnlyList<MonsterBattleCombatant>, string?>? _persistCapture;
    private CaptureTurn? _captureTurn;

    private bool CanCapture => Wild != null && _captureDevice != null && (_persistCapture != null || PersistCaptureWithRewards != null) &&
        Outcome == MonsterBattleOutcome.Active && Phase == MonsterBattlePhase.Commands && Opponent.HP > 0;

    private (CaptureDevice Device, decimal Chance) ValidateCapture(Guid itemId)
    {
        if (!CanCapture) throw new InvalidOperationException("Capturing is unavailable in this battle or phase.");
        if (itemId == Guid.Empty || _captureDevice!(itemId) is not { } device)
            throw new InvalidOperationException("You do not have a usable capture device of that type.");
        var statuses = Opponent.Status is { } status ? new[] { status.Definition.CaptureMultiplier } : [];
        var chance = MonsterCaptureChance.Calculate(true, _baseCaptureChance, Opponent.HP, Opponent.MaximumHP,
            statuses, device.Multiplier, device.Guaranteed);
        return chance.HasValue ? (device, chance.Value) :
            throw new InvalidOperationException("This opponent or device is ineligible for capturing.");
    }

    private void AttemptCapture(MonsterBattleActionPacket action, CaptureDevice device, decimal chance,
        List<string> messages, List<MonsterBattlePresentationEvent> events)
    {
        var caught = chance == 100m || (chance > 0m && MonsterRandom.Draw(_random, 100_000_000) < chance * 1_000_000m);
        _captureTurn = new(action.Revision, action.ItemId, caught ? Wild : null, caught ? Opponent : null);

        void Present(MonsterBattleEventKind kind, string text)
        {
            messages.Add(text);
            events.Add(new() { Kind = kind, Order = events.Count, ActorId = Player.Id, TargetId = Opponent.Id, Text = text });
        }

        Present(MonsterBattleEventKind.CaptureDeviceUsed, $"You used {device.Name} to try to capture {Opponent.Name}.");
        if (caught)
        {
            Outcome = MonsterBattleOutcome.Captured;
            Phase = MonsterBattlePhase.Ended;
            ClearTemporaryState();
            return;
        }

        Present(MonsterBattleEventKind.CaptureFailed, $"{Opponent.Name} broke free!");
        if (ChooseOpponentMove() is { } move) Attack(Opponent, Player, move, messages, events);
        TickStatus(Player, messages, events);
        TickStatus(Opponent, messages, events);
        CompleteTurn(messages, events);
    }
}
