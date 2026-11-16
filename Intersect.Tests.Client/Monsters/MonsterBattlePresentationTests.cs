using Intersect.Framework.Core.GameObjects.Animations;
using Intersect.Client.Framework.Core.Sounds;
using Intersect.Framework.Core.GameObjects.Monsters;
using System.Reflection;
using System.Runtime.CompilerServices;
using Intersect.Client.Framework.Graphics;
using Intersect.Client.Interface.Game.Monsters;
using Intersect.Client.MonoGame.Graphics;
using AtlasColor = Microsoft.Xna.Framework.Color;
using AtlasRectangle = Microsoft.Xna.Framework.Rectangle;
using Intersect.Network.Packets.Server;
using NUnit.Framework;

namespace Intersect.Tests.Client.Monsters;

[TestFixture]
public class MonsterBattlePresentationTests
{
    private static readonly Guid Player = Guid.NewGuid(), Opponent = Guid.NewGuid(), Battle = Guid.NewGuid(), Owner = Guid.NewGuid();
    private static MonsterBattleStatePacket State(long sequence, Guid revision, Guid previous = default, MonsterBattlePresentationEvent[]? events = null,
        long playerHP = 100, long opponentHP = 100, MonsterBattleOutcome outcome = MonsterBattleOutcome.Active) => new()
    {
        PlayerId = Owner, BattleId = Battle, Sequence = sequence, Turn = sequence == 1 ? 1 : 2, Revision = revision, PreviousRevision = previous, Outcome = outcome,
        Player = new() { Id = Player, HP = playerHP, MaximumHP = 100 }, Opponent = new() { Id = Opponent, HP = opponentHP, MaximumHP = 100 }, Events = events ?? [],
    };
    private static MonsterBattlePresentationEvent Event(MonsterBattleEventKind kind, int order, bool player, long before = 0, long after = 0) => new()
    { Kind = kind, Order = order, ActorId = player ? Player : Opponent, TargetId = player ? Opponent : Player, HPBefore = before, HPAfter = after, Text = kind.ToString() + order };
    private static (MonsterBattlePlayback Playback, MonsterBattleStatePacket Start) Ready()
    {
        var model = new MonsterBattlePlayback(); var start = State(1, Guid.NewGuid()); model.Accept(start); return (model, start);
    }
    [Test]
    public void CaptureResultAndPlacementArePresentedAfterTheAttempt()
    {
        var (model, start) = Ready();
        start.AllowsCapture = true;
        start.CaptureDevices = [new() { Id = Guid.NewGuid(), Name = "Basic Device", Quantity = 2 }];
        var decoded = MessagePack.MessagePackSerializer.Deserialize<MonsterBattleStatePacket>(
            MessagePack.MessagePackSerializer.Serialize(start));
        Assert.That(decoded.AllowsCapture, Is.True);
        Assert.That(decoded.CaptureDevices.Single().Id, Is.EqualTo(start.CaptureDevices.Single().Id));
        Assert.That(decoded.CaptureDevices.Single().Quantity, Is.EqualTo(2));

        Assert.That(model.BeginAwaiting(), Is.True);
        var used = Event(MonsterBattleEventKind.CaptureDeviceUsed, 0, true) with { Text = "You used Basic Device." };
        var result = Event(MonsterBattleEventKind.BattleFinished, 1, true) with { Text = "Captured in storage slot 1." };
        var end = State(2, Guid.NewGuid(), start.Revision, [used, result], outcome: MonsterBattleOutcome.Captured);
        end.Messages = [used.Text, result.Text];
        end.AwaitingPresentationClose = true;
        model.Accept(end);
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.PlayingSequence));
        Assert.That(model.BeginAwaiting(), Is.False);
        Assert.That(model.Text, Is.EqualTo(used.Text));
        model.Tick(.2);
        Assert.That(model.Text, Is.EqualTo(used.Text));
        model.Advance();
        Assert.That(model.Text, Is.EqualTo(result.Text));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.PlayingSequence));
        model.Advance();
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.ShowingResult));
        Assert.That(model.Text, Is.EqualTo(result.Text));
    }

    [Test]
    public void OrderedFactsDriveOpponentFirstAndSequentialHpEndpoints()
    {
        var (model, start) = Ready(); model.BeginAwaiting();
        model.Accept(State(2, Guid.NewGuid(), start.Revision, [Event(MonsterBattleEventKind.MoveUsed, 0, false), Event(MonsterBattleEventKind.DamageApplied, 1, false, 100, 60),
            Event(MonsterBattleEventKind.MoveUsed, 2, true), Event(MonsterBattleEventKind.DamageApplied, 3, true, 100, 70)], 60, 70));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.PlayingSequence));
        Assert.That(model.CurrentEvent!.ActorId, Is.EqualTo(Opponent)); Assert.That(model.PlayerHP, Is.EqualTo(100));
        Assert.That(model.BeginAwaiting(), Is.False);
        model.Advance(); model.Tick(.2); Assert.That(model.PlayerHP, Is.InRange(60.01, 99.99)); Assert.That(model.OpponentHP, Is.EqualTo(100));
        model.Tick(.4); Assert.That(model.PlayerHP, Is.EqualTo(60)); Assert.That(model.CurrentEvent!.ActorId, Is.EqualTo(Player));
        model.Tick(10); Assert.That(model.PlayerHP, Is.EqualTo(60)); Assert.That(model.OpponentHP, Is.EqualTo(70));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection));
    }
    [Test]
    public void PresentationCanFollowPlayerPriorityWithoutInferringOrderFromText()
    {
        var (model, start) = Ready(); var e = Event(MonsterBattleEventKind.MoveUsed, 0, true); e = e with { Text = "Arbitrary localized text" };
        model.Accept(State(2, Guid.NewGuid(), start.Revision, [e]));
        Assert.That(model.CurrentEvent!.ActorId, Is.EqualTo(Player)); Assert.That(model.Text, Is.EqualTo(e.Text));
    }
    [Test]
    public void MissesAndImmunityLeaveHpUntouchedThroughoutPlayback()
    {
        var (model, start) = Ready(); model.Accept(State(2, Guid.NewGuid(), start.Revision,
            [Event(MonsterBattleEventKind.MoveUsed, 0, true), Event(MonsterBattleEventKind.MoveMissed, 1, true),
             Event(MonsterBattleEventKind.MoveUsed, 2, false), Event(MonsterBattleEventKind.DamageApplied, 3, false, 100, 100)]));
        for (var i = 0; i < 50; i++) { model.Tick(.1); Assert.That(model.PlayerHP, Is.EqualTo(100)); Assert.That(model.OpponentHP, Is.EqualTo(100)); }
    }
    [Test]
    public void FaintFinishesBeforeResultAndSkipCompletesExactlyAtZero()
    {
        var (model, start) = Ready(); var end = State(2, Guid.NewGuid(), start.Revision,
            [Event(MonsterBattleEventKind.MoveUsed, 0, true), Event(MonsterBattleEventKind.DamageApplied, 1, true, 100, 0),
             Event(MonsterBattleEventKind.MonsterFainted, 2, true), Event(MonsterBattleEventKind.BattleFinished, 3, true)], opponentHP: 0, outcome: MonsterBattleOutcome.Victory);
        model.Accept(end); Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.PlayingSequence));
        model.Tick(.9); Assert.That(model.OpponentHP, Is.EqualTo(0)); Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.PlayingSequence));
        model.Skip(); Assert.That(model.OpponentFainted, Is.True); Assert.That(model.OpponentHP, Is.EqualTo(0));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.ShowingResult));
    }
    [Test]
    public void DuplicateStaleAndOutOfOrderBatchesNeverReplay()
    {
        var (model, start) = Ready(); var next = State(2, Guid.NewGuid(), start.Revision, [Event(MonsterBattleEventKind.MoveUsed, 0, true)]);
        model.Accept(next); model.Skip(); Assert.That(model.Accept(next), Is.False); Assert.That(model.Accept(start), Is.False);
        var duplicate = State(3, next.Revision, start.Revision, next.Events); Assert.That(model.Accept(duplicate), Is.False);
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection));
        Assert.That(model.Accept(State(2, Guid.NewGuid())), Is.False);
    }
    [Test]
    public void MissingRevisionChainAndInvalidEventsUseSnapshotCorrection()
    {
        var (model, _) = Ready(); model.Accept(State(2, Guid.NewGuid(), Guid.NewGuid(), [Event(MonsterBattleEventKind.MoveUsed, 0, true)], 50, 25));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection)); Assert.That(model.PlayerHP, Is.EqualTo(50));
        var previous = model.State!; model.Accept(State(3, Guid.NewGuid(), previous.Revision, [Event(MonsterBattleEventKind.DamageApplied, 4, true, 100, -1)], 50, 0));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection)); Assert.That(model.OpponentHP, Is.EqualTo(0));
    }
    [Test]
    public void RejectionUnlocksAwaitingWithoutPlayingOldTurnAgain()
    {
        var (model, start) = Ready(); Assert.That(model.BeginAwaiting(), Is.True); Assert.That(model.BeginAwaiting(), Is.False);
        var rejection = State(2, start.Revision); rejection.IsCorrection = true; model.Accept(rejection);
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection)); Assert.That(model.CurrentEvent, Is.Null);
    }
    [Test]
    public void CancellationInterruptsPlaybackAndDisconnectResetsEverything()
    {
        var (model, start) = Ready(); var result = State(2, Guid.NewGuid(), start.Revision, [Event(MonsterBattleEventKind.MoveUsed, 0, true)]); model.Accept(result);
        model.Accept(State(3, Guid.NewGuid(), result.Revision, outcome: MonsterBattleOutcome.Cancelled));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.ShowingResult)); Assert.That(model.CurrentEvent, Is.Null);
        model.Reset(); Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.Waiting)); Assert.That(model.State, Is.Null); Assert.That(model.PlayerHP, Is.Zero);
    }
    [Test]
    public void AllTerminalOutcomesStayTerminalAndClockInputsAreValidated()
    {
        foreach (var outcome in new[] { MonsterBattleOutcome.Victory, MonsterBattleOutcome.Defeat, MonsterBattleOutcome.Draw, MonsterBattleOutcome.Forfeit, MonsterBattleOutcome.Cancelled })
        {
            var model = new MonsterBattlePlayback(); model.Accept(State(1, Guid.NewGuid(), outcome: outcome));
            Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.ShowingResult)); Assert.That(model.BeginAwaiting(), Is.False);
        }
        var (active, _) = Ready(); Assert.Throws<ArgumentOutOfRangeException>(() => active.Tick(double.NaN)); Assert.Throws<ArgumentOutOfRangeException>(() => active.Tick(-1));
    }
    [Test]
    public void DedicatedArtFallbacksAndMissingAssetsAreSafe()
    {
        using var options = new OptionScope(); var texture = DispatchProxy.Create<IGameTexture, MonsterRosterLayoutTests.TextureStub>();
        var actor = new MonsterBattleActorView { FrontBattleSprite = "front", BackBattleSprite = "back", Sprite = "sheet" };
        IGameTexture? Load(string name) => name is "front" or "sheet" ? texture : null;
        var front = MonsterBattleArt.Resolve(actor, false, Load); var back = MonsterBattleArt.Resolve(actor, true, Load);
        Assert.That(front.Fallback, Is.False); Assert.That(front.Frame.Width, Is.EqualTo(texture.Width));
        Assert.That(back.Fallback, Is.True); Assert.That(back.Frame.Width, Is.EqualTo(texture.Width / Options.Instance.Sprites.NormalFrames));
        Assert.That(MonsterBattleArt.Resolve(actor, true, _ => throw new IOException()).Texture, Is.Null);
        Assert.That(MonsterBattleArt.Resolve(new(), false, _ => null).Texture, Is.Null);
    }
    [Test]
    public void BackgroundSelectionFallsBackToDefaultThenProcedural()
    {
        using var options = new OptionScope();
        var texture = DispatchProxy.Create<IGameTexture, MonsterRosterLayoutTests.TextureStub>();
        Assert.That(MonsterBattleArt.Background("custom", _ => texture), Is.SameAs(texture));
        Assert.That(MonsterBattleArt.Background("missing", name => name == "monster-battle-default.png" ? texture : null), Is.SameAs(texture));
        Assert.That(MonsterBattleArt.Background("missing", _ => null), Is.Null);
    }
    [Test]
    public void SpriteGeometryFitsVariedAspectRatiosAndDistinctSides()
    {
        foreach (var (w, h) in new[] { (600, 220), (900, 330), (1200, 460) })
        foreach (var (fw, fh) in new[] { (16, 32), (96, 96), (500, 100), (512, 1024) })
        foreach (var own in new[] { true, false })
        {
            var bounds = MonsterBattleArt.SpriteBounds(w, h, own, fw, fh);
            Assert.That(bounds.X, Is.GreaterThanOrEqualTo(0)); Assert.That(bounds.Y, Is.GreaterThanOrEqualTo(0));
            Assert.That(bounds.X + bounds.Width, Is.LessThanOrEqualTo(w)); Assert.That(bounds.Y + bounds.Height, Is.LessThanOrEqualTo(h));
            Assert.That(own ? bounds.X < w / 2 : bounds.X > w / 2, Is.True);
            Assert.That(Math.Abs(bounds.Width / (double)fw - bounds.Height / (double)fh), Is.LessThan(.07));
        }
    }
    private sealed class OptionScope : IDisposable
    {
        private readonly PropertyInfo _property = typeof(Options).GetProperty(nameof(Options.Instance))!;
        private readonly object? _previous;
        public OptionScope() { _previous = _property.GetValue(null); _property.SetValue(null, new Options()); }
        public void Dispose() => _property.SetValue(null, _previous);
    }


    [Test]
    public void SwitchKeepsOutgoingActorUntilSendOutAndDamagesIncomingActor()
    {
        var (model, start) = Ready();
        var incoming = new MonsterBattleActorView { Id = Guid.NewGuid(), Name = "Reserve", HP = 34, MaximumHP = 45, BackBattleSprite = "reserve.png" };
        var state = State(2, Guid.NewGuid(), start.Revision);
        state.Player = new() { Id = incoming.Id, Name = incoming.Name, HP = 20, MaximumHP = 45, BackBattleSprite = incoming.BackBattleSprite };
        state.Party = [new() { Slot = 0, Actor = start.Player }, new() { Slot = 1, Actor = state.Player }];
        state.Events = [
            new() { Kind = MonsterBattleEventKind.Withdraw, Order = 0, ActorId = Player, TargetId = Player },
            new() { Kind = MonsterBattleEventKind.SendOut, Order = 1, ActorId = incoming.Id, TargetId = incoming.Id, Incoming = incoming },
            new() { Kind = MonsterBattleEventKind.MoveUsed, Order = 2, ActorId = Opponent, TargetId = incoming.Id },
            new() { Kind = MonsterBattleEventKind.DamageApplied, Order = 3, ActorId = Opponent, TargetId = incoming.Id, HPBefore = 34, HPAfter = 20 },
        ];
        Assert.That(model.Accept(state), Is.True);
        Assert.That(model.DisplayedPlayer!.Id, Is.EqualTo(Player));
        Assert.That(model.PlayerHP, Is.EqualTo(100));
        model.Advance();
        Assert.That(model.DisplayedPlayer!.Id, Is.EqualTo(incoming.Id));
        Assert.That(model.DisplayedPlayer.BackBattleSprite, Is.EqualTo("reserve.png"));
        Assert.That(model.PlayerHP, Is.EqualTo(34));
        model.Tick(4);
        Assert.That(model.PlayerHP, Is.EqualTo(20));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection));
    }

    [Test]
    public void FaintPlaybackCompletesBeforeReplacementAndSendOutClearsFaintedState()
    {
        var (model, start) = Ready();
        var faint = State(2, Guid.NewGuid(), start.Revision,
            [Event(MonsterBattleEventKind.DamageApplied, 0, false, 100, 0),
                Event(MonsterBattleEventKind.MonsterFainted, 1, false)], playerHP: 0);
        faint.Phase = MonsterBattlePhase.Replacement;
        model.Accept(faint);
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.PlayingSequence));
        model.Advance();
        Assert.That(model.CurrentEvent!.Kind, Is.EqualTo(MonsterBattleEventKind.MonsterFainted));
        model.Advance();
        Assert.That(model.PlayerFainted, Is.True);
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.Replacement));
        Assert.That(model.BeginAwaiting(), Is.True);

        var state = State(3, Guid.NewGuid(), faint.Revision);
        state.Player = new() { Id = Guid.NewGuid(), HP = 18, MaximumHP = 20 };
        state.Party = [new() { Slot = 1, Actor = state.Player }];
        state.Events = [new() { Kind = MonsterBattleEventKind.SendOut, ActorId = state.Player.Id, TargetId = state.Player.Id, Incoming = state.Player }];
        model.Accept(state);
        Assert.That(model.PlayerFainted, Is.False);
        Assert.That(model.PlayerHP, Is.EqualTo(18));
        model.Skip();
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection));
    }

    [Test]
    public void AuthoredAnchorsFollowActorAndTargetInBothDirections()
    {
        foreach (var playerAttacks in new[] { true, false })
        {
            var e = Event(MonsterBattleEventKind.MoveUsed, 0, playerAttacks);
            Assert.That(MonsterBattleAnimation.Anchors(e), Is.EqualTo((e.TargetId, Guid.Empty)));
            e.AnimationPosition = BattleMoveAnimationPosition.Attacker;
            Assert.That(MonsterBattleAnimation.Anchors(e), Is.EqualTo((e.ActorId, Guid.Empty)));
            e.AnimationPosition = BattleMoveAnimationPosition.Both;
            Assert.That(MonsterBattleAnimation.Anchors(e), Is.EqualTo((e.ActorId, e.TargetId)));
            e.TargetId = e.ActorId;
            Assert.That(MonsterBattleAnimation.Anchors(e), Is.EqualTo((e.ActorId, Guid.Empty)));
        }
    }

    [Test]
    public void SoundStartsOnceOnMoveEventAndSkipStopsCompletedAndActiveAudio()
    {
        var (model, start) = Ready();
        var animation = new AnimationDescriptor(Guid.NewGuid()) { Sound = "practice.wav", CompleteSound = true };
        var sounds = new List<SoundStub>();
        var adapter = new MonsterBattleAnimation([], [], id => id == animation.Id ? animation : null,
            _ => throw new IOException("Missing texture"), name =>
            {
                Assert.That(name, Is.EqualTo("practice.wav"));
                var sound = new SoundStub();
                sounds.Add(sound);
                return sound;
            });
        model.EventStarted = adapter.Begin;
        model.EventEnded = adapter.End;
        model.Interrupted = adapter.Cancel;
        model.EffectDuration = entry => entry.Kind == MonsterBattleEventKind.MoveUsed ? adapter.Duration : .5;
        var first = Event(MonsterBattleEventKind.MoveUsed, 0, true) with { AnimationId = animation.Id };
        var second = Event(MonsterBattleEventKind.MoveUsed, 2, false) with { AnimationId = animation.Id };
        var state = State(2, Guid.NewGuid(), start.Revision, [first, Event(MonsterBattleEventKind.MoveMissed, 1, true),
            second, Event(MonsterBattleEventKind.DamageApplied, 3, false, 100, 100)]);
        model.Accept(state);
        Assert.That(sounds.Count, Is.EqualTo(1));
        Assert.That(model.Accept(state), Is.False);
        Assert.That(sounds.Count, Is.EqualTo(1));
        model.Tick(.35);
        Assert.That(sounds[0].Loaded, Is.True);
        model.Advance();
        Assert.That(sounds.Count, Is.EqualTo(2));
        Assert.That(model.PlayerHP, Is.EqualTo(100));
        Assert.That(model.OpponentHP, Is.EqualTo(100));
        model.Skip();
        Assert.That(sounds.All(sound => !sound.Loaded), Is.True);
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection));

        var missing = first with { AnimationId = Guid.NewGuid() };
        adapter.Begin(missing);
        Assert.That(adapter.Duration, Is.EqualTo(.35));
        Assert.That(adapter.HasAuthoredImpact(missing), Is.False);
        Assert.That(sounds.Count, Is.EqualTo(2));
    }

    [Test]
    public void LongAuthoredLoopsAreCappedAndMissingLayersFallBack()
    {
        using var options = new OptionScope();
        var texture = DispatchProxy.Create<IGameTexture, MonsterRosterLayoutTests.TextureStub>();
        var animation = new AnimationDescriptor(Guid.NewGuid())
        {
            Lower = new AnimationLayer { Sprite = "test.png", XFrames = 2, YFrames = 2, FrameCount = 999, FrameSpeed = 999, LoopCount = int.MaxValue },
            Upper = new AnimationLayer { Sprite = "test.png", XFrames = 0 },
        };
        var entry = Event(MonsterBattleEventKind.MoveUsed, 0, true) with { AnimationId = animation.Id };
        var adapter = new MonsterBattleAnimation([], [], _ => animation, _ => texture, _ => null);
        adapter.Begin(entry);
        Assert.That(adapter.Duration, Is.EqualTo(4));
        Assert.That(adapter.HasAuthoredImpact(entry), Is.True);
        adapter.Cancel();
        Assert.That(adapter.HasAuthoredImpact(entry), Is.False);
        animation.Lower.FrameSpeed = 0;
        adapter.Begin(entry);
        Assert.That(adapter.Duration, Is.EqualTo(.35));
        Assert.That(adapter.HasAuthoredImpact(entry), Is.False);
    }

    private sealed class SoundStub : ISound
    {
        public bool Loaded { get; set; } = true;
        public bool Loop { get; set; }
        public void Stop() => Loaded = false;
        public bool Update() => Loaded;
    }

    [Test]
    public void StageAndHealingFactsPlayInOrderAndNextAndSkipSnapCorrectly()
    {
        var model = new MonsterBattlePlayback();
        var start = State(1, Guid.NewGuid(), playerHP: 45);
        model.Accept(start);
        var used = Event(MonsterBattleEventKind.MoveUsed, 0, true) with { TargetId = Player, AnimationId = Guid.NewGuid() };
        var stage = Event(MonsterBattleEventKind.StatStageChanged, 1, true) with
        {
            TargetId = Player, Stat = MonsterStat.Defense, StageAfter = 2,
        };
        var heal = Event(MonsterBattleEventKind.HPRecovered, 2, true, 45, 75) with { TargetId = Player };
        var state = State(2, Guid.NewGuid(), start.Revision, [used, stage, heal], playerHP: 75);
        state.Player.StatStages = [0, 2, 0, 0, 0];
        var starts = new List<MonsterBattleEventKind>();
        model.EventStarted = entry => starts.Add(entry.Kind);
        Assert.That(model.Accept(state), Is.True);
        Assert.That(model.PlayerStages.Defense, Is.Zero);
        Assert.That(model.PlayerHP, Is.EqualTo(45));
        model.Advance();
        Assert.That(model.PlayerStages.Defense, Is.EqualTo(2));
        model.Advance();
        model.Tick(.25);
        Assert.That(model.PlayerHP, Is.EqualTo(60).Within(.001));
        Assert.That(model.Accept(state), Is.False);
        model.Advance();
        Assert.That(model.PlayerHP, Is.EqualTo(75));
        Assert.That(starts, Is.EqualTo(new[] { MonsterBattleEventKind.MoveUsed, MonsterBattleEventKind.StatStageChanged, MonsterBattleEventKind.HPRecovered }));

        var second = State(3, Guid.NewGuid(), state.Revision, [used, heal with { Order = 1, HPBefore = 75, HPAfter = 90 }], playerHP: 90);
        second.Player.StatStages = state.Player.StatStages;
        model.Accept(second);
        model.Skip();
        Assert.That(model.PlayerHP, Is.EqualTo(90));
        Assert.That(model.PlayerStages.Defense, Is.EqualTo(2));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection));
    }

    [Test]
    public void StatusEventsAnimateAndReplayOnlyAuthoritativeHpAndLabels()
    {
        var (model, start) = Ready();
        var started = new List<MonsterBattleEventKind>();
        model.EventStarted = entry => started.Add(entry.Kind);
        var application = Event(MonsterBattleEventKind.StatusApplied, 0, false) with { StatusName = "Custom", AnimationId = Guid.NewGuid() };
        var tick = Event(MonsterBattleEventKind.StatusDamage, 1, false, 100, 88) with { StatusName = "Custom", AnimationId = Guid.NewGuid() };
        var cure = Event(MonsterBattleEventKind.StatusRemoved, 2, false);
        var state = State(2, Guid.NewGuid(), start.Revision, [application, tick, cure], playerHP: 88);
        Assert.That(model.Accept(state), Is.True);
        Assert.That(model.PlayerStatus, Is.EqualTo("Custom"));
        model.Advance();
        model.Tick(.25);
        Assert.That(model.PlayerHP, Is.EqualTo(94).Within(.01));
        Assert.That(model.Accept(state), Is.False);
        model.Advance();
        Assert.That(model.PlayerHP, Is.EqualTo(88));
        Assert.That(model.PlayerStatus, Is.Empty);
        model.Skip();
        Assert.That(started, Is.EqualTo(new[] { MonsterBattleEventKind.StatusApplied, MonsterBattleEventKind.StatusDamage, MonsterBattleEventKind.StatusRemoved }));
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.CommandSelection));
    }

    [Test]
    public void RosterTooltipRepairsOnlyTheOldDefaultTemplate()
    {
        var original = Intersect.Client.Localization.Strings.Monsters.Entry;
        try
        {
            const string name = "Nivéole — 雪";
            const string species = "Pebbling";
            var expected = $"Party #1: {name} — {species} — Lv. 3";
            Assert.That(Intersect.Client.Localization.Strings.Monsters.FormatEntry("Party", 1, name, species, 3), Is.EqualTo(expected));

            Intersect.Client.Localization.Strings.Monsters.Entry = "{00} #{01}: {02} â€” {03} â€” Lv. {04}";
            Assert.That(Intersect.Client.Localization.Strings.Monsters.FormatEntry("Party", 1, name, species, 3), Is.EqualTo(expected));

            Intersect.Client.Localization.Strings.Monsters.Entry = "{02} «{03}» — niv. {04}";
            Assert.That(Intersect.Client.Localization.Strings.Monsters.FormatEntry("Party", 1, name, species, 3),
                Is.EqualTo($"{name} «{species}» — niv. 3"));
        }
        finally { Intersect.Client.Localization.Strings.Monsters.Entry = original; }
    }

    [Test]
    public void GenderGlyphsReplaceOnlyDedicatedMissingTilesAndPreserveNativeSymbols()
    {
        var missing = new AtlasRectangle(0, 0, 11, 12);
        var male = new AtlasRectangle(11, 0, 11, 12);
        var female = new AtlasRectangle(22, 0, 11, 12);
        var customMissing = new AtlasRectangle(33, 0, 11, 12);
        var customNative = new AtlasRectangle(44, 0, 11, 12);
        var placeholder = Enumerable.Repeat(AtlasColor.White, 11 * 12).ToArray();
        var nativePixels = placeholder.ToArray();
        nativePixels[0] = AtlasColor.Transparent;
        var tiles = new Dictionary<AtlasRectangle, AtlasColor[]>
        {
            [missing] = placeholder,
            [male] = placeholder.ToArray(),
            [female] = placeholder.ToArray(),
            [customMissing] = placeholder.ToArray(),
            [customNative] = nativePixels,
        };
        var glyphs = new Dictionary<char, AtlasRectangle>
        {
            ['\u2065'] = missing, ['\u2642'] = male, ['\u2640'] = female,
            ['\u2604'] = customMissing, ['\u2665'] = customNative,
        };
        var writes = new List<AtlasRectangle>();
        var coverage = new MonsterSymbolGlyphs(glyphs, region => tiles[region], (region, pixels) =>
        {
            writes.Add(region);
            tiles[region] = pixels;
        });
        Assert.That(coverage.Supports('\u2642'), Is.True);
        Assert.That(coverage.Supports('\u2640'), Is.True);
        Assert.That(coverage.Supports('\u2640'), Is.True);
        Assert.That(coverage.Supports('\u2665'), Is.True);
        Assert.That(coverage.Supports('\u2604'), Is.False);
        Assert.That(coverage.Supports('\u96EA'), Is.False);
        Assert.That(writes, Is.EqualTo(new[] { male, female }));
        foreach (var tile in new[] { tiles[male], tiles[female] })
        {
            Assert.That(tile.Any(pixel => pixel.A == 0), Is.True);
            Assert.That(tile.Any(pixel => pixel.A == 255), Is.True);
        }
        Assert.That(tiles[male], Is.Not.EqualTo(tiles[female]));
        Assert.That(tiles[missing], Is.EqualTo(placeholder));
        Assert.That(tiles[customNative], Is.EqualTo(nativePixels));

        var shared = new Dictionary<char, AtlasRectangle>(glyphs) { ['\u2642'] = missing };
        var sharedCoverage = new MonsterSymbolGlyphs(shared, region => tiles[region], (_, _) => Assert.Fail("Shared tile was modified."));
        Assert.That(sharedCoverage.Supports('\u2642'), Is.False);
    }

    [Test]
    public void GenderNameSurvivesMissingGlyphAndUnspecifiedIsDistinct()
    {
        var id = Guid.NewGuid();
        Assert.That(MonsterGenderText.Format(id, "Astral", "\u2604", null, 14), Is.EqualTo("Astral"));
        Assert.That(MonsterGenderText.Format(null, null, null, null, 14), Is.EqualTo("Unspecified"));
        Assert.That(MonsterGenderText.Format(id, "Genderless", "", null, 14), Is.EqualTo("Genderless"));
        Assert.That(MonsterGenderText.Format(id, null, null, null, 14), Is.EqualTo("Unavailable"));
        var state = State(1, Guid.NewGuid());
        state.Player.GenderId = id;
        state.Player.GenderName = "Astral";
        state.Player.Name = "Niv\u00E9ole \u2014 \u96EA";
        state.Player.GenderSymbol = "\u2604";
        state.Opponent.GenderId = MonsterGenderDescriptor.DefaultFemale;
        state.Opponent.GenderSymbol = "\u2640";
        state.Party = [new() { Actor = new() { GenderId = MonsterGenderDescriptor.DefaultMale, GenderSymbol = "\u2642" } }];
        var roundTrip = MessagePack.MessagePackSerializer.Deserialize<MonsterBattleStatePacket>(MessagePack.MessagePackSerializer.Serialize(state));
        Assert.That(roundTrip.Player.GenderId, Is.EqualTo(id));
        Assert.That(roundTrip.Player.GenderName, Is.EqualTo("Astral"));
        Assert.That(roundTrip.Player.Name, Is.EqualTo(state.Player.Name));
        Assert.That(roundTrip.Player.GenderSymbol, Is.EqualTo(state.Player.GenderSymbol));
        Assert.That(roundTrip.Opponent.GenderSymbol!.Single(), Is.EqualTo((char)0x2640));
        Assert.That(roundTrip.Party[0].Actor.GenderSymbol!.Single(), Is.EqualTo((char)0x2642));
    }
    [Test]
    public void WildEscapeSnapshotKeepsOpponentAndReturnsToResultImmediately()
    {
        var revision = Guid.NewGuid();
        var open = State(1, revision);
        open.IsWild = true;
        var model = new MonsterBattlePlayback();
        Assert.That(model.Accept(open), Is.True);
        var escape = State(2, Guid.NewGuid());
        escape.IsWild = true;
        escape.BattleId = open.BattleId;
        escape.Opponent.Id = open.Opponent.Id;
        escape.PreviousRevision = revision;
        escape.Outcome = MonsterBattleOutcome.Escaped;
        escape.Events = [new() { Kind = MonsterBattleEventKind.BattleFinished, Text = "Escaped" }];
        var packet = MessagePack.MessagePackSerializer.Deserialize<MonsterBattleStatePacket>(
            MessagePack.MessagePackSerializer.Serialize(escape));
        Assert.That(packet.IsWild, Is.True);
        Assert.That(model.Accept(packet), Is.True);
        Assert.That(model.Phase, Is.EqualTo(MonsterBattlePresentationPhase.ShowingResult));
        Assert.That(model.State!.Opponent.Id, Is.EqualTo(open.Opponent.Id));
    }
}
